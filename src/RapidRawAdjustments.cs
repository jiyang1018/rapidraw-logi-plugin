namespace Loupedeck.RapidRawPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Dial-assignable RapidRAW develop parameters. Each detent becomes a
    /// <c>step</c> message; RapidRAW renders a live preview while the dial turns
    /// and a full-quality frame plus auto-save when it stops. Dial press resets
    /// the parameter to its default.
    /// </summary>
    public class RapidRawAdjustments : PluginDynamicAdjustment
    {
        private sealed class Entry
        {
            public Entry(String id, String label, String group, String format, String suffix, Boolean signed,
                         Int32 stepsPerDetent, Double min, Double max, Double def)
            {
                this.Id = id;
                this.Label = label;
                this.Group = group;
                this.Format = format;
                this.Suffix = suffix;
                this.Signed = signed;
                this.StepsPerDetent = stepsPerDetent;
                this.Min = min;
                this.Max = max;
                this.Default = def;
            }

            public String Id { get; }
            public String Label { get; }
            public String Group { get; }
            public String Format { get; }
            public String Suffix { get; }
            public Boolean Signed { get; }

            /// <summary>Native RapidRAW steps per dial detent (exposure: 5 x 0.01 EV).</summary>
            public Int32 StepsPerDetent { get; }

            // Fallback range until RapidRAW's own table arrives via get_params.
            public Double Min { get; }
            public Double Max { get; }
            public Double Default { get; }
        }

        private static readonly Dictionary<String, Entry> Entries = new Dictionary<String, Entry>(StringComparer.Ordinal);

        private RapidRawPlugin RrPlugin => (RapidRawPlugin)this.Plugin;

        private RapidRawClient Client => this.RrPlugin.Client;

        public RapidRawAdjustments()
            : base(hasReset: true)
        {
            this.DisplayName = "RapidRAW";
            this.Description = "Adjusts RapidRAW's develop sliders live. Press the dial to reset.";
            this.GroupName = "Develop";

            // ---- Basic ------------------------------------------------------
            this.Ev("exposure", "Exposure", "Basic");
            this.Ev("brightness", "Brightness", "Basic");
            this.Int("contrast", "Contrast", "Basic");
            this.Int("highlights", "Highlights", "Basic");
            this.Int("shadows", "Shadows", "Basic");
            this.Int("whites", "Whites", "Basic");
            this.Int("blacks", "Blacks", "Basic");

            // ---- Color ------------------------------------------------------
            this.Int("temperature", "Temperature", "Color");
            this.Int("tint", "Tint", "Color");
            this.Int("vibrance", "Vibrance", "Color");
            this.Int("saturation", "Saturation", "Color");
            this.Deg("hue", "Hue shift", "Color", -180, 180);

            foreach (var color in new[] { "reds", "oranges", "yellows", "greens", "aquas", "blues", "purples", "magentas" })
            {
                var name = Char.ToUpperInvariant(color[0]) + color.Substring(1);
                this.Int($"hsl.{color}.hue", $"{name} hue", $"HSL###{name}");
                this.Int($"hsl.{color}.saturation", $"{name} saturation", $"HSL###{name}");
                this.Int($"hsl.{color}.luminance", $"{name} luminance", $"HSL###{name}");
            }

            foreach (var range in new[] { "shadows", "midtones", "highlights", "global" })
            {
                var name = Char.ToUpperInvariant(range[0]) + range.Substring(1);
                this.Deg($"colorGrading.{range}.hue", $"{name} hue", $"Color grading###{name}", 0, 360);
                this.Uns($"colorGrading.{range}.saturation", $"{name} saturation", $"Color grading###{name}");
                this.Int($"colorGrading.{range}.luminance", $"{name} luminance", $"Color grading###{name}");
            }
            this.Uns("colorGrading.blending", "Blending", "Color grading", 0, 100, 50);
            this.Int("colorGrading.balance", "Balance", "Color grading");

            this.Int("colorCalibration.shadowsTint", "Shadows tint", "Calibration");
            this.Int("colorCalibration.redHue", "Red hue", "Calibration");
            this.Int("colorCalibration.redSaturation", "Red saturation", "Calibration");
            this.Int("colorCalibration.greenHue", "Green hue", "Calibration");
            this.Int("colorCalibration.greenSaturation", "Green saturation", "Calibration");
            this.Int("colorCalibration.blueHue", "Blue hue", "Calibration");
            this.Int("colorCalibration.blueSaturation", "Blue saturation", "Calibration");

            // ---- Details ----------------------------------------------------
            this.Int("sharpness", "Sharpness", "Details");
            this.Uns("sharpnessThreshold", "Sharpness threshold", "Details", 0, 80, 15);
            this.Int("clarity", "Clarity", "Details");
            this.Int("dehaze", "Dehaze", "Details");
            this.Int("structure", "Structure", "Details");
            this.Int("centr\u00e9", "Centre", "Details"); // RapidRAW's real field name has the accent
            this.Uns("lumaNoiseReduction", "Luminance NR", "Details");
            this.Uns("colorNoiseReduction", "Color NR", "Details");
            this.Int("chromaticAberrationRedCyan", "CA red/cyan", "Details");
            this.Int("chromaticAberrationBlueYellow", "CA blue/yellow", "Details");

            // ---- Effects ----------------------------------------------------
            this.Uns("glowAmount", "Glow", "Effects");
            this.Uns("halationAmount", "Halation", "Effects");
            this.Uns("flareAmount", "Light flares", "Effects");
            this.Uns("lensBlurAmount", "Lens blur amount", "Effects###Lens blur", 0, 100, 40);
            this.Uns("lensBlurDiffusion", "Lens blur diffusion", "Effects###Lens blur");
            this.Int("vignetteAmount", "Vignette amount", "Effects###Vignette");
            this.Uns("vignetteMidpoint", "Vignette midpoint", "Effects###Vignette", 0, 100, 50);
            this.Int("vignetteRoundness", "Vignette roundness", "Effects###Vignette");
            this.Uns("vignetteFeather", "Vignette feather", "Effects###Vignette", 0, 100, 50);
            this.Uns("grainAmount", "Grain amount", "Effects###Grain");
            this.Uns("grainSize", "Grain size", "Effects###Grain", 0, 100, 25);
            this.Uns("grainRoughness", "Grain roughness", "Effects###Grain", 0, 100, 50);
            this.Uns("lutIntensity", "LUT intensity", "Effects", 0, 100, 100);

            // ---- Transform --------------------------------------------------
            this.Tenth("rotation", "Straighten", "Transform", -45, 45);
            this.Tenth("transformRotate", "Rotate", "Transform", -45, 45);
            this.Int("transformVertical", "Vertical", "Transform");
            this.Int("transformHorizontal", "Horizontal", "Transform");
            this.Int("transformDistortion", "Distortion", "Transform");
            this.Int("transformAspect", "Aspect", "Transform");
            this.Uns("transformScale", "Scale", "Transform", 50, 150, 100);
            this.Int("transformXOffset", "X offset", "Transform");
            this.Int("transformYOffset", "Y offset", "Transform");
        }

        // Helpers: one per readout style. Ranges are fallbacks; get_params wins.
        private void Ev(String id, String label, String group) =>
            this.Add(new Entry(id, label, group, "0.00", " EV", true, 5, -5, 5, 0));

        private void Int(String id, String label, String group) =>
            this.Add(new Entry(id, label, group, "0", "", true, 1, -100, 100, 0));

        private void Uns(String id, String label, String group, Double min = 0, Double max = 100, Double def = 0) =>
            this.Add(new Entry(id, label, group, "0", "", false, 1, min, max, def));

        private void Deg(String id, String label, String group, Double min, Double max) =>
            this.Add(new Entry(id, label, group, "0", "\u00b0", min < 0, 1, min, max, 0));

        private void Tenth(String id, String label, String group, Double min, Double max) =>
            this.Add(new Entry(id, label, group, "0.0", "\u00b0", true, 1, min, max, 0));

        private void Add(Entry entry)
        {
            Entries[entry.Id] = entry;
            this.AddParameter(entry.Id, entry.Label, entry.Group);
        }

        protected override Boolean OnLoad()
        {
            this.RrPlugin.ReadoutsInvalidated += (s, e) => this.AdjustmentValueChanged();
            this.Client.ParamsReceived += (s, e) => this.AdjustmentValueChanged();

            this.Client.StateChanged += (s, e) =>
            {
                foreach (var id in e.ChangedParams)
                {
                    if (Entries.ContainsKey(id))
                    {
                        this.AdjustmentValueChanged(id);
                    }
                }
            };

            // Plugin.Load() starts the client before the actions exist; if the
            // handshake already happened, ask for a fresh snapshot now.
            if (this.Client.IsConnected)
            {
                this.Client.RequestState();
            }

            return true;
        }

        protected override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            if (!Entries.TryGetValue(actionParameter, out var entry))
            {
                return;
            }

            this.Client.SendStep(actionParameter, diff * entry.StepsPerDetent);
        }

        /// <summary>Dial press: back to the parameter's default.</summary>
        protected override void RunCommand(String actionParameter) =>
            this.Client.SendReset(actionParameter);

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            "Reset";

        protected override Nullable<Double> GetAdjustmentMinValue(String actionParameter) =>
            this.Client.TryGetParam(actionParameter, out var p) ? p.Min
                : Entries.TryGetValue(actionParameter, out var e) ? e.Min : (Double?)null;

        protected override Nullable<Double> GetAdjustmentMaxValue(String actionParameter) =>
            this.Client.TryGetParam(actionParameter, out var p) ? p.Max
                : Entries.TryGetValue(actionParameter, out var e) ? e.Max : (Double?)null;

        protected override Nullable<Double> GetAdjustmentDefaultValue(String actionParameter) =>
            this.Client.TryGetParam(actionParameter, out var p) ? p.Default
                : Entries.TryGetValue(actionParameter, out var e) ? e.Default : (Double?)null;

        protected override String GetAdjustmentValue(String actionParameter)
        {
            if (String.IsNullOrEmpty(actionParameter) || !Entries.TryGetValue(actionParameter, out var entry))
            {
                return "--";
            }

            if (!this.Client.IsConnected)
            {
                return "off";
            }

            if (!this.Client.IsImageOpen)
            {
                return "--";
            }

            if (!this.Client.TryGetValue(actionParameter, out var value))
            {
                return "--";
            }

            var text = Math.Abs(value).ToString(entry.Format, CultureInfo.InvariantCulture);
            if (value < 0)
            {
                text = "-" + text;
            }
            else if (entry.Signed && value > 0)
            {
                text = "+" + text;
            }

            return text + entry.Suffix;
        }
    }
}
