namespace Loupedeck.RapidRawPlugin
{
    using System;

    // Button actions: dialpad's four buttons, keypad LCD keys, or Actions Ring
    public class RapidRawCommands : PluginDynamicCommand
    {
        private RapidRawClient Client => ((RapidRawPlugin)this.Plugin).Client;

        public RapidRawCommands()
        {
            this.AddParameter("undo",                "Undo",              "Edit");
            this.AddParameter("redo",                "Redo",              "Edit");
            this.AddParameter("reset_all",           "Reset All Edits",   "Edit");
            this.AddParameter("copy_settings",       "Copy Settings",     "Edit");
            this.AddParameter("paste_settings",      "Paste Settings",    "Edit");
            this.AddParameter("toggle_before_after", "Before / After",    "View");
            this.AddParameter("zoom_fit",            "Zoom to Fit",       "View");
            this.AddParameter("zoom_100",            "Zoom 100%",         "View");
            this.AddParameter("next_image",          "Next Image",        "Culling");
            this.AddParameter("prev_image",          "Previous Image",    "Culling");
            for (var stars = 0; stars <= 5; stars++)
                this.AddParameter($"rate:{stars}", $"Rate {stars}★", "Culling");
        }

        protected override void RunCommand(String actionParameter) =>
            this.Client.SendCommand(actionParameter);
    }
}
