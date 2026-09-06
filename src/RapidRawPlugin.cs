namespace Loupedeck.RapidRawPlugin
{
    using System;
    using System.Threading;

    /// <summary>
    /// Logi Actions plugin that drives RapidRAW's develop sliders live over its
    /// external control API (TCP 127.0.0.1:47820, newline-delimited JSON).
    /// </summary>
    public class RapidRawPlugin : Plugin
    {
        /// <summary>Everything goes over the socket; no keyboard shortcuts are sent.</summary>
        public override Boolean UsesApplicationApiOnly => true;

        /// <summary>False = this is an application plugin, bound to RapidRAW.</summary>
        public override Boolean HasNoApplication => false;

        // 1 while a warning is on display, so it can be cleared once the
        // condition that raised it has passed.
        private Int32 _degraded;

        internal RapidRawClient Client { get; } = new RapidRawClient();

        /// <summary>How long an armed reset waits for a dial movement.</summary>
        internal const Int32 ResetArmMs = 5000;

        private Int64 _resetArmedUntil;
        private Timer _resetArmTimer;

        /// <summary>
        /// "Press Reset, then turn the dial you mean." Options+ never tells a
        /// plugin which Actions Ring item is highlighted and the MX dial cannot be
        /// pressed, so the reset key arms instead: the next dial movement within
        /// <see cref="ResetArmMs"/> resets that slider rather than adjusting it.
        /// </summary>
        internal Boolean IsResetArmed => Environment.TickCount64 < Volatile.Read(ref this._resetArmedUntil);

        /// <summary>Raised when the armed state changes, so the key can repaint.</summary>
        internal event EventHandler ResetArmChanged;

        internal void ArmReset()
        {
            Volatile.Write(ref this._resetArmedUntil, Environment.TickCount64 + ResetArmMs);
            this.ResetArmChanged?.Invoke(this, EventArgs.Empty);

            // Repaint again when it expires untouched.
            this._resetArmTimer?.Dispose();
            this._resetArmTimer = new Timer(_ =>
            {
                if (!this.IsResetArmed)
                {
                    this.ResetArmChanged?.Invoke(this, EventArgs.Empty);
                }
            }, null, ResetArmMs + 50, Timeout.Infinite);
        }

        internal void DisarmReset()
        {
            Volatile.Write(ref this._resetArmedUntil, 0);
            this._resetArmTimer?.Dispose();
            this._resetArmTimer = null;
            this.ResetArmChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>True once: the dial movement that consumes the armed reset.</summary>
        internal Boolean TryConsumeArmedReset()
        {
            if (!this.IsResetArmed)
            {
                return false;
            }

            this.DisarmReset();
            return true;
        }

        /// <summary>
        /// Raised when something other than a parameter value changes what the
        /// dial readouts should show: the link came up or went down, an image was
        /// opened or closed. The actions repaint on it.
        /// </summary>
        internal event EventHandler ReadoutsInvalidated;

        public RapidRawPlugin()
        {
            // Deliberately empty. PluginLog.Init / PluginResources.Init from the
            // template are not part of PluginApi.dll; Diag logs to a file and
            // to Plugin.Log instead.
        }

        public override void Load()
        {
            Diag.Attach(this.Log);
            Diag.Info("Plugin loading.");

            this.Client.Connected += (s, e) =>
            {
                this.ClearDegradedStatus("Connected to RapidRAW", force: true);
                this.ReadoutsInvalidated?.Invoke(this, EventArgs.Empty);
            };

            this.Client.Disconnected += (s, e) =>
            {
                this.SetDegradedStatus(
                    global::Loupedeck.PluginStatus.Warning,
                    "RapidRAW not reachable. Start RapidRAW (the external-control build) and open an image.",
                    "https://github.com/CyberTimon/RapidRAW",
                    "RapidRAW");
                this.ReadoutsInvalidated?.Invoke(this, EventArgs.Empty);
            };

            this.Client.StateChanged += (s, e) =>
            {
                if (e.ContextChanged)
                {
                    this.ReadoutsInvalidated?.Invoke(this, EventArgs.Empty);
                }
            };

            this.Client.ErrorReceived += (s, e) => Diag.Warning("RapidRAW reported: " + e.Message);

            this.Client.Start();
        }

        private void SetDegradedStatus(global::Loupedeck.PluginStatus status, String message, String url = null, String urlTitle = null)
        {
            Volatile.Write(ref this._degraded, 1);
            if (url == null)
            {
                this.OnPluginStatusChanged(status, message);
            }
            else
            {
                this.OnPluginStatusChanged(status, message, url, urlTitle);
            }
        }

        private void ClearDegradedStatus(String message, Boolean force = false)
        {
            if (Interlocked.Exchange(ref this._degraded, 0) == 1 || force)
            {
                this.OnPluginStatusChanged(global::Loupedeck.PluginStatus.Normal, message);
            }
        }

        public override void Unload()
        {
            this._resetArmTimer?.Dispose();
            this.Client.Dispose();
        }
    }
}
