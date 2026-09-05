namespace Loupedeck.RapidRawPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Client for RapidRAW's external control API: one persistent TCP connection
    /// to 127.0.0.1:47820 carrying newline-delimited JSON both ways. Protocol:
    /// docs/EXTERNAL_CONTROL_API.md in the RapidRAW fork.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike the Bridge plugin's one-connection-per-request transport, RapidRAW
    /// keeps the socket open and pushes <c>state</c> snapshots on its own, so this
    /// client runs a reader loop for the lifetime of the connection and a small
    /// writer pump that coalesces dial ticks: rotations accumulate per parameter
    /// and go out as one <c>step</c> message per pump interval. A fast spin is a
    /// few messages, not hundreds, and the app never has to catch up.
    /// </para>
    /// <para>
    /// Everything here runs on thread-pool threads. Events fire on the reader
    /// thread; subscribers must marshal or use thread-safe state.
    /// </para>
    /// </remarks>
    public sealed class RapidRawClient : IDisposable
    {
        private const Int32 PumpIntervalMs = 20;
        private const Int32 OfflineRetryMs = 2000;
        private const Int32 ConnectTimeoutMs = 1500;

        private readonly String _host;
        private readonly Int32 _port;

        /// <summary>Accumulated dial rotation per parameter, awaiting the next flush.</summary>
        private readonly ConcurrentDictionary<String, Int32> _ticks =
            new ConcurrentDictionary<String, Int32>();

        /// <summary>One-shot messages (resets, actions, queries) in the order they were issued.</summary>
        private readonly ConcurrentQueue<Object> _outbox = new ConcurrentQueue<Object>();

        /// <summary>Latest value of every parameter, from the last <c>state</c> snapshot.</summary>
        private readonly ConcurrentDictionary<String, Double> _values =
            new ConcurrentDictionary<String, Double>();

        /// <summary>Parameter table as reported by <c>get_params</c>.</summary>
        private readonly ConcurrentDictionary<String, ParamInfo> _params =
            new ConcurrentDictionary<String, ParamInfo>();

        private CancellationTokenSource _cts;
        private volatile Boolean _online;
        private volatile Boolean _imageOpen;
        private volatile String _imageName;
        private volatile String _view;
        private volatile Boolean _canUndo;
        private volatile Boolean _canRedo;
        private volatile Boolean _showOriginal;
        private Int32 _rating;

        public event EventHandler Connected;
        public event EventHandler Disconnected;

        /// <summary>A <c>state</c> snapshot arrived; <c>ChangedParams</c> lists the ids whose value moved.</summary>
        public event EventHandler<StateEventArgs> StateChanged;

        /// <summary>The parameter table arrived (ranges, steps, defaults).</summary>
        public event EventHandler ParamsReceived;

        public event EventHandler<ErrorEventArgs> ErrorReceived;

        public Boolean IsConnected => this._online;
        public Boolean IsImageOpen => this._imageOpen;
        public String ImageName => this._imageName;
        public String View => this._view;
        public Boolean CanUndo => this._canUndo;
        public Boolean CanRedo => this._canRedo;
        public Boolean ShowOriginal => this._showOriginal;
        public Int32 Rating => Volatile.Read(ref this._rating);

        public RapidRawClient(String host = "127.0.0.1", Int32 port = 47820)
        {
            this._host = host;
            this._port = port;
        }

        public void Start()
        {
            this._cts = new CancellationTokenSource();
            _ = Task.Run(() => this.RunAsync(this._cts.Token));
        }

        public void Stop()
        {
            try { this._cts?.Cancel(); } catch { /* already gone */ }
        }

        public void Dispose() => this.Stop();

        // ---- lookups ---------------------------------------------------------

        public Boolean TryGetValue(String id, out Double value) => this._values.TryGetValue(id, out value);

        public Boolean TryGetParam(String id, out ParamInfo info) => this._params.TryGetValue(id, out info);

        // ---- outbound --------------------------------------------------------

        /// <summary>
        /// Accumulates dial rotation. Nothing goes on the wire until the next pump,
        /// so a burst of detents becomes a single <c>step</c>.
        /// </summary>
        public void SendStep(String id, Int32 ticks)
        {
            if (String.IsNullOrEmpty(id) || ticks == 0)
            {
                return;
            }

            this._ticks.AddOrUpdate(id, ticks, (_, existing) => existing + ticks);
        }

        public void SendReset(String id) =>
            this._outbox.Enqueue(new { type = "reset", param = id });

        public void SendSet(String id, Double value) =>
            this._outbox.Enqueue(new { type = "set", param = id, value });

        public void SendAction(String id) =>
            this._outbox.Enqueue(new { type = "action", id });

        public void RequestState() => this._outbox.Enqueue(new { type = "get_state" });

        public void RequestParams() => this._outbox.Enqueue(new { type = "get_params" });

        // ---- connection loop -------------------------------------------------

        private async Task RunAsync(CancellationToken ct)
        {
            Diag.Info($"Client started, target {this._host}:{this._port}.");
            var failedProbes = 0;

            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp = null;
                try
                {
                    tcp = new TcpClient { NoDelay = true };
                    using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        connectCts.CancelAfter(ConnectTimeoutMs);
                        await tcp.ConnectAsync(this._host, this._port, connectCts.Token).ConfigureAwait(false);
                    }

                    failedProbes = 0;
                    await this.ServeConnectionAsync(tcp, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    if (this._online)
                    {
                        Diag.Info("Link to RapidRAW dropped: " + Describe(ex));
                    }
                    else if (failedProbes++ % 15 == 0)
                    {
                        // ~30 s heartbeat while waiting, so the log proves the loop is alive
                        // without filling up on an idle machine.
                        Diag.Info($"Waiting for RapidRAW on {this._host}:{this._port} (probe {failedProbes}).");
                    }
                }
                finally
                {
                    try { tcp?.Dispose(); } catch { /* ignore */ }
                    this.SetOnline(false);
                }

                if (!await QuietDelay(OfflineRetryMs, ct).ConfigureAwait(false))
                {
                    break;
                }
            }

            Diag.Info("Client stopped.");
            this.SetOnline(false);
        }

        private async Task ServeConnectionAsync(TcpClient tcp, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = linked.Token;
            var stream = tcp.GetStream();

            // RapidRAW splits on '\n'; StreamWriter would emit "\r\n" on Windows.
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
            var reader = new StreamReader(stream, new UTF8Encoding(false));

            // Drop anything queued while offline: stale ticks and actions must not
            // fire into a freshly opened image.
            this._ticks.Clear();
            while (this._outbox.TryDequeue(out _)) { }

            // The server greets first, then replays its last state if it has one.
            var hello = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (hello == null)
            {
                throw new IOException("connection closed before hello");
            }

            this.Dispatch(hello);
            this.SetOnline(true);

            this.RequestParams();
            this.RequestState();

            var writerTask = this.PumpAsync(writer, token);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line == null)
                    {
                        break;
                    }

                    this.Dispatch(line);
                }
            }
            finally
            {
                linked.Cancel();
                try { await writerTask.ConfigureAwait(false); } catch { /* cancelled */ }
            }
        }

        /// <summary>Writer side: flushes coalesced ticks and queued messages.</summary>
        private async Task PumpAsync(StreamWriter writer, CancellationToken ct)
        {
            var sb = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                sb.Clear();

                while (this._outbox.TryDequeue(out var message))
                {
                    sb.Append(JsonSerializer.Serialize(message)).Append('\n');
                }

                foreach (var id in this._ticks.Keys.ToArray())
                {
                    if (!this._ticks.TryRemove(id, out var ticks) || ticks == 0)
                    {
                        continue;
                    }

                    sb.Append(JsonSerializer.Serialize(new { type = "step", param = id, ticks })).Append('\n');
                }

                if (sb.Length > 0)
                {
                    await writer.WriteAsync(sb.ToString()).ConfigureAwait(false);
                }

                if (!await QuietDelay(PumpIntervalMs, ct).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        private static async Task<Boolean> QuietDelay(Int32 ms, CancellationToken ct)
        {
            try
            {
                await Task.Delay(ms, ct).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private static String Describe(Exception ex) =>
            ex is OperationCanceledException
                ? "timed out"
                : ex.GetType().Name + ": " + ex.Message;

        private void SetOnline(Boolean value)
        {
            if (this._online == value)
            {
                return;
            }

            this._online = value;
            if (value)
            {
                Diag.Info("Connected to RapidRAW.");
                this.Connected?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                this._imageOpen = false;
                this._imageName = null;
                this.Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        // ---- inbound ---------------------------------------------------------

        private void Dispatch(String line)
        {
            if (String.IsNullOrWhiteSpace(line))
            {
                return;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException ex)
            {
                Diag.Warning($"Unparseable line from RapidRAW: {ex.Message} :: {line}");
                return;
            }

            using (doc)
            {
                try
                {
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                    switch (type)
                    {
                        case "hello":
                            Diag.Info("RapidRAW " +
                                      (root.TryGetProperty("version", out var v) ? v.GetString() : "?") +
                                      ", protocol " +
                                      (root.TryGetProperty("protocol", out var p) ? p.GetRawText() : "?"));
                            break;

                        case "state":
                            this.HandleState(root);
                            break;

                        case "params":
                            this.HandleParams(root);
                            break;

                        case "error":
                            this.ErrorReceived?.Invoke(this, new ErrorEventArgs(
                                root.TryGetProperty("message", out var m) ? m.GetString() : "unknown"));
                            break;

                        case "ignored":
                            // e.g. a dial turned with no image open. Not an error, but
                            // worth a line so a silent dial is explainable.
                            Diag.Info("RapidRAW ignored a command: " +
                                      (root.TryGetProperty("reason", out var r) ? r.GetString() : "?"));
                            break;

                        case "action-result":
                        case "actions":
                        case "pong":
                            break;

                        default:
                            Diag.Info("Unhandled message type: " + type);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // A surprising field must not take the reader down.
                    Diag.Warning("Could not handle message from RapidRAW: " + ex.Message);
                }
            }
        }

        private void HandleState(JsonElement root)
        {
            this._view = root.TryGetProperty("view", out var view) ? view.GetString() : null;
            this._canUndo = root.TryGetProperty("canUndo", out var cu) && cu.ValueKind == JsonValueKind.True;
            this._canRedo = root.TryGetProperty("canRedo", out var cr) && cr.ValueKind == JsonValueKind.True;
            this._showOriginal = root.TryGetProperty("showOriginal", out var so) && so.ValueKind == JsonValueKind.True;

            var imageOpen = false;
            String imageName = null;
            var rating = 0;
            if (root.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object)
            {
                imageOpen = image.TryGetProperty("isReady", out var ready) && ready.ValueKind == JsonValueKind.True;
                imageName = image.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (image.TryGetProperty("rating", out var rt) && rt.ValueKind == JsonValueKind.Number)
                {
                    rating = rt.GetInt32();
                }
            }

            var contextChanged = imageOpen != this._imageOpen ||
                                 !String.Equals(imageName, this._imageName, StringComparison.Ordinal) ||
                                 Interlocked.Exchange(ref this._rating, rating) != rating;
            this._imageOpen = imageOpen;
            this._imageName = imageName;

            var changed = new List<String>();
            if (root.TryGetProperty("params", out var values) && values.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in values.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    var value = prop.Value.GetDouble();
                    if (!this._values.TryGetValue(prop.Name, out var previous) || previous != value)
                    {
                        this._values[prop.Name] = value;
                        changed.Add(prop.Name);
                    }
                }
            }

            this.StateChanged?.Invoke(this, new StateEventArgs(changed, contextChanged));
        }

        private void HandleParams(JsonElement root)
        {
            if (!root.TryGetProperty("params", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var count = 0;
            foreach (var el in list.EnumerateArray())
            {
                if (!el.TryGetProperty("id", out var idEl))
                {
                    continue;
                }

                var info = new ParamInfo(
                    idEl.GetString(),
                    el.TryGetProperty("group", out var g) ? g.GetString() : null,
                    Num(el, "min", 0),
                    Num(el, "max", 0),
                    Num(el, "step", 1),
                    Num(el, "default", 0));
                this._params[info.Id] = info;
                count++;
            }

            Diag.Info($"Parameter table received: {count} params.");
            this.ParamsReceived?.Invoke(this, EventArgs.Empty);
        }

        private static Double Num(JsonElement el, String name, Double fallback) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
    }

    public sealed class ParamInfo
    {
        public ParamInfo(String id, String group, Double min, Double max, Double step, Double defaultValue)
        {
            this.Id = id;
            this.Group = group;
            this.Min = min;
            this.Max = max;
            this.Step = step;
            this.Default = defaultValue;
        }

        public String Id { get; }
        public String Group { get; }
        public Double Min { get; }
        public Double Max { get; }
        public Double Step { get; }
        public Double Default { get; }
    }

    public sealed class StateEventArgs : EventArgs
    {
        public StateEventArgs(IReadOnlyList<String> changedParams, Boolean contextChanged)
        {
            this.ChangedParams = changedParams;
            this.ContextChanged = contextChanged;
        }

        /// <summary>Parameter ids whose value differs from the previous snapshot.</summary>
        public IReadOnlyList<String> ChangedParams { get; }

        /// <summary>The open image, its readiness or its rating changed.</summary>
        public Boolean ContextChanged { get; }
    }

    public sealed class ErrorEventArgs : EventArgs
    {
        public ErrorEventArgs(String message) => this.Message = message;

        public String Message { get; }
    }
}
