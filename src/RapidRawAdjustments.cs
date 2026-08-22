namespace Loupedeck.RapidRawPlugin
{
    using System;
    using System.Collections.Generic;

    // One multi-parameter adjustment class exposes every RapidRAW slider as a
    // dial-assignable action. In Options+ these appear under "RapidRAW >
    // Adjustments" and can be dropped onto the Dialpad dial/roller or picked
    // from the Actions Ring — same UX as the official Lightroom plugin.
    public class RapidRawAdjustments : PluginDynamicAdjustment
    {
        private RapidRawClient Client => ((RapidRawPlugin)this.Plugin).Client;

        // Cached live values for the LCD / ring readout
        private readonly Dictionary<String, Double> _values = new Dictionary<String, Double>();

        public RapidRawAdjustments()
            : base(hasReset: true) // dial press = reset to default
        {
            // Static fallback list; replaced by list_adjustments once connected
            this.AddParameter("exposure",    "Exposure",    "Tone");
            this.AddParameter("contrast",    "Contrast",    "Tone");
            this.AddParameter("highlights",  "Highlights",  "Tone");
            this.AddParameter("shadows",     "Shadows",     "Tone");
            this.AddParameter("whites",      "Whites",      "Tone");
            this.AddParameter("blacks",      "Blacks",      "Tone");
            this.AddParameter("temperature", "Temperature", "Color");
            this.AddParameter("tint",        "Tint",        "Color");
            this.AddParameter("vibrance",    "Vibrance",    "Color");
            this.AddParameter("saturation",  "Saturation",  "Color");
            this.AddParameter("clarity",     "Clarity",     "Detail");
            this.AddParameter("sharpening",  "Sharpening",  "Detail");
            this.AddParameter("structure",   "Structure",   "Detail");
            this.AddParameter("dehaze",      "Dehaze",      "Effects");
            this.AddParameter("vignette",    "Vignette",    "Effects");
            this.AddParameter("grain",       "Film Grain",  "Effects");
        }

        protected override Boolean OnLoad()
        {
            // Rebuild the parameter list from RapidRAW's registry when it connects,
            // so new sliders (or HSL bands) appear without a plugin update.
            this.Client.AdjustmentsListed += (s, items) =>
            {
                foreach (var item in items)
                    this.AddParameter(item.Id, item.Label, item.Group ?? "Adjustments");
                this.ParametersChanged();
            };

            // Live value pushes → refresh LCD labels
            this.Client.StateChanged += (s, e) =>
            {
                this._values[e.Id] = e.Value;
                this.AdjustmentValueChanged(e.Id);
            };

            return true;
        }

        // Dial rotated: diff is signed detent count (with SDK acceleration)
        protected override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            this.Client.SendAdjust(actionParameter, diff);
            this.Client.SendSelect(actionParameter); // drive RapidRAW's HUD highlight
        }

        // Dial pressed: reset this slider to default
        protected override void RunCommand(String actionParameter) =>
            this.Client.SendReset(actionParameter);

        // Value shown on keypad LCD / Actions Ring
        protected override String GetAdjustmentValue(String actionParameter) =>
            this._values.TryGetValue(actionParameter, out var v)
                ? v.ToString("0.##")
                : "--";
    }
}
