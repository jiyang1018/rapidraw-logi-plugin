// RapidRAW plugin for Logi Actions SDK (MX Creative Console / Loupedeck)
// Skeleton — generate the real project with:
//   dotnet tool install -g LogiPluginTool
//   logiplugintool generate RapidRaw
// then drop these classes in. API names follow the Loupedeck-heritage SDK;
// verify against the template the tool generates for your SDK version.

namespace Loupedeck.RapidRawPlugin
{
    using System;

    public class RapidRawPlugin : Plugin
    {
        // Dialpad/keypad control of a desktop app: no elevated rights needed
        public override Boolean UsesApplicationApiOnly => true;
        public override Boolean HasNoApplication => false;

        internal RapidRawClient Client { get; } = new RapidRawClient("ws://127.0.0.1:43917");

        public override void Load()
        {
            this.Info.DisplayName = "RapidRAW";

            this.Client.Connected += (s, e) =>
                this.OnPluginStatusChanged(PluginStatus.Normal, "Connected to RapidRAW", null, null);

            this.Client.Disconnected += (s, e) =>
                this.OnPluginStatusChanged(
                    PluginStatus.Warning,
                    "RapidRAW not reachable — enable Settings → Control Surface API",
                    "https://github.com/CyberTimon/RapidRAW", "RapidRAW setup");

            this.Client.Start(); // async connect + auto-reconnect loop
        }

        public override void Unload() => this.Client.Stop();
    }
}
