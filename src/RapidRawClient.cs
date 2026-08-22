namespace Loupedeck.RapidRawPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class AdjustmentInfo
    {
        public String Id { get; set; }
        public String Label { get; set; }
        public String Group { get; set; }
        public Double Min { get; set; }
        public Double Max { get; set; }
        public Double Step { get; set; }
    }

    public sealed class StateChangedEventArgs : EventArgs
    {
        public String Id { get; set; }
        public Double Value { get; set; }
    }

    // Thin JSON-over-WebSocket client for RapidRAW's Control Surface API.
    // Owns a background connect/reconnect loop; all Send* methods are
    // fire-and-forget and silently drop when disconnected (dial ticks are
    // not worth queuing).
    public sealed class RapidRawClient
    {
        private readonly Uri _uri;
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<List<AdjustmentInfo>> AdjustmentsListed;
        public event EventHandler<StateChangedEventArgs> StateChanged;

        public Boolean IsConnected => this._ws?.State == WebSocketState.Open;

        public RapidRawClient(String url) => this._uri = new Uri(url);

        public void Start()
        {
            this._cts = new CancellationTokenSource();
            _ = Task.Run(() => this.RunAsync(this._cts.Token));
        }

        public void Stop()
        {
            this._cts?.Cancel();
            this._ws?.Dispose();
        }

        // ---- outbound -------------------------------------------------------

        public void SendAdjust(String id, Int32 ticks) =>
            this.Send(new { type = "adjust", id, ticks, delta = (Double?)null });

        public void SendSelect(String id) =>
            this.Send(new { type = "select_adjustment", id });

        public void SendReset(String id) =>
            this.Send(new { type = "set", id, value = (Object)null }); // null = default

        public void SendCommand(String id) =>
            this.Send(new { type = "command", id });

        private void Send(Object msg)
        {
            if (!this.IsConnected)
                return;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(msg,
                new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            _ = this._ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        // ---- connection loop ------------------------------------------------

        private async Task RunAsync(CancellationToken ct)
        {
            var buffer = new Byte[64 * 1024];
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    this._ws = new ClientWebSocket();
                    await this._ws.ConnectAsync(this._uri, ct);

                    this.Send(new { type = "hello", client = "logi-actions-rapidraw", version = 1 });
                    this.Send(new { type = "list_adjustments" });
                    Connected?.Invoke(this, EventArgs.Empty);

                    while (this._ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var result = await this._ws.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                        this.HandleMessage(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                }
                catch (Exception)
                {
                    // RapidRAW closed or API disabled — fall through to retry
                }

                Disconnected?.Invoke(this, EventArgs.Empty);
                try { await Task.Delay(TimeSpan.FromSeconds(3), ct); }
                catch (TaskCanceledException) { break; }
            }
        }

        private void HandleMessage(String json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "adjustments":
                    var items = new List<AdjustmentInfo>();
                    foreach (var el in root.GetProperty("items").EnumerateArray())
                        items.Add(JsonSerializer.Deserialize<AdjustmentInfo>(el.GetRawText(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
                    AdjustmentsListed?.Invoke(this, items);
                    break;

                case "state_changed":
                    StateChanged?.Invoke(this, new StateChangedEventArgs
                    {
                        Id = root.GetProperty("id").GetString(),
                        Value = root.GetProperty("value").GetDouble(),
                    });
                    break;
            }
        }
    }
}
