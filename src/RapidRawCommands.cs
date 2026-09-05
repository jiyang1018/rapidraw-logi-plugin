namespace Loupedeck.RapidRawPlugin
{
    using System;

    /// <summary>
    /// Button actions: each maps to one of RapidRAW's named editor actions (the
    /// same handlers its keyboard shortcuts use), fired over the socket.
    /// </summary>
    public class RapidRawCommands : PluginDynamicCommand
    {
        private RapidRawPlugin RrPlugin => (RapidRawPlugin)this.Plugin;

        public RapidRawCommands()
            : base()
        {
            this.DisplayName = "RapidRAW";
            this.Description = "Editor actions in RapidRAW: history, navigation, rating, view.";
            this.GroupName = "Actions";

            this.AddParameter("undo", "Undo", "Edit");
            this.AddParameter("redo", "Redo", "Edit");
            this.AddParameter("copy_adjustments", "Copy adjustments", "Edit");
            this.AddParameter("paste_adjustments", "Paste adjustments", "Edit");
            this.AddParameter("show_original", "Before / after", "Edit");
            this.AddParameter("rotate_left", "Rotate left", "Edit");
            this.AddParameter("rotate_right", "Rotate right", "Edit");

            this.AddParameter("preview_prev", "Previous image", "Navigate");
            this.AddParameter("preview_next", "Next image", "Navigate");
            this.AddParameter("open_image", "Open selected image", "Navigate");

            this.AddParameter("rate_0", "Clear rating", "Rating");
            for (var stars = 1; stars <= 5; stars++)
            {
                this.AddParameter($"rate_{stars}", $"Rate {stars}", "Rating");
            }

            this.AddParameter("color_label_none", "Label: none", "Label");
            this.AddParameter("color_label_red", "Label: red", "Label");
            this.AddParameter("color_label_yellow", "Label: yellow", "Label");
            this.AddParameter("color_label_green", "Label: green", "Label");
            this.AddParameter("color_label_blue", "Label: blue", "Label");
            this.AddParameter("color_label_purple", "Label: purple", "Label");

            this.AddParameter("zoom_fit", "Zoom to fit", "View");
            this.AddParameter("zoom_100", "Zoom 100%", "View");
            this.AddParameter("zoom_in", "Zoom in", "View");
            this.AddParameter("zoom_out", "Zoom out", "View");
            this.AddParameter("cycle_zoom", "Cycle zoom", "View");
            this.AddParameter("toggle_fullscreen", "Full screen", "View");
            this.AddParameter("toggle_left_panel", "Toggle left panel", "View");
            this.AddParameter("toggle_right_panel", "Toggle right panel", "View");
            this.AddParameter("toggle_bottom_panel", "Toggle filmstrip", "View");

            this.AddParameter("toggle_adjustments", "Panel: adjustments", "Panels");
            this.AddParameter("toggle_crop_panel", "Panel: crop", "Panels");
            this.AddParameter("toggle_masks", "Panel: masks", "Panels");
            this.AddParameter("toggle_presets", "Panel: presets", "Panels");
            this.AddParameter("toggle_export", "Panel: export", "Panels");
            this.AddParameter("toggle_metadata", "Panel: metadata", "Panels");

            this.AddParameter("brush_size_up", "Brush size +", "Masks");
            this.AddParameter("brush_size_down", "Brush size -", "Masks");
        }

        protected override Boolean OnLoad()
        {
            // Repaint the keys that show live state.
            this.RrPlugin.ReadoutsInvalidated += (s, e) => this.ActionImageChanged();
            this.RrPlugin.Client.StateChanged += (s, e) =>
            {
                if (e.ContextChanged)
                {
                    this.ActionImageChanged();
                }
            };
            return true;
        }

        protected override void RunCommand(String actionParameter) =>
            this.RrPlugin.Client.SendAction(actionParameter);

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            var client = this.RrPlugin.Client;
            if (actionParameter != null && actionParameter.StartsWith("rate_", StringComparison.Ordinal) &&
                client.IsConnected && client.IsImageOpen)
            {
                // Show the current rating on whichever rate key is assigned.
                var stars = actionParameter.Substring(5);
                return stars == "0" ? "Clear\n" + Stars(client.Rating) : "Rate " + stars + "\n" + Stars(client.Rating);
            }

            return base.GetCommandDisplayName(actionParameter, imageSize);
        }

        private static String Stars(Int32 rating) =>
            rating <= 0 ? "-" : new String('*', Math.Min(rating, 5));
    }
}
