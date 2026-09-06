namespace Loupedeck.RapidRawPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Dial-assignable RapidRAW develop parameters. Each detent becomes a
    /// <c>step</c> message; RapidRAW renders a live preview while the dial turns
    /// and a full-quality frame plus auto-save when it stops. Reset: the key action
    /// "Reset (then turn dial)" arms a reset that the next dial movement consumes;
    /// on devices with a pressable dial the press resets directly.
    /// </summary>
    public sealed class ParamDef
    {
        public ParamDef(String id, String label, String group)
        {
            this.Id = id;
            this.Label = label;
            this.Group = group;
        }

        public String Id { get; }
        public String Label { get; }
        public String Group { get; }
    }

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

        /// <summary>Human label for a parameter id, for keys that name the active setting.</summary>
        internal static String LabelOf(String id) =>
            id != null && Entries.TryGetValue(id, out var e) ? e.Label : id;

        /// <summary>
        /// How much <c>diff</c> the SDK reports for one physical detent of the
        /// dial. The Logi SDK does not document this per device; the plugin log
        /// records every raw diff (`dial exposure diff=+2`), so read a few slow
        /// single-click turns from the plugin log (see Diag) and set
        /// this to the smallest magnitude you see. Remainders are carried, so an
        /// odd diff during a fast spin is not lost.
        /// </summary>
        private const Int32 SdkDiffPerDetent = 2;

        /// <summary>Dial movement is ignored on this parameter until the dial has been
        /// still for this long, after an armed reset consumed the first detent.</summary>
        private const Int32 SwallowQuietMs = 500;
        private String _swallowParam;
        private Int64 _swallowUntil;

        /// <summary>Leftover SDK diff per parameter, below one detent.</summary>
        private readonly Dictionary<String, Int32> _carry = new Dictionary<String, Int32>(StringComparer.Ordinal);

        private RapidRawPlugin RrPlugin => (RapidRawPlugin)this.Plugin;

        private RapidRawClient Client => this.RrPlugin.Client;

        /// <summary>Every dial parameter, in display order. Shared with the reset keys.</summary>
        internal static readonly IReadOnlyList<ParamDef> All;

        static RapidRawAdjustments()
        {
            BuildTable();
            var list = new List<ParamDef>(Entries.Count);
            foreach (var e in Order)
            {
                list.Add(new ParamDef(e.Id, e.Label, e.Group));
            }
            All = list;
        }

        private static readonly List<Entry> Order = new List<Entry>();

        public RapidRawAdjustments()
            : base(hasReset: true)
        {
            this.DisplayName = "RapidRAW";
            this.Description = "Adjusts RapidRAW's develop sliders live.";
            this.GroupName = "Develop";

            foreach (var entry in Order)
            {
                this.AddParameter(entry.Id, entry.Label, entry.Group);
            }
        }

        private static void BuildTable()
        {
            // ---- Basic ------------------------------------------------------
            Ev("exposure", "Exposure", "Basic");
            Ev("brightness", "Brightness", "Basic");
            Int("contrast", "Contrast", "Basic");
            Int("highlights", "Highlights", "Basic");
            Int("shadows", "Shadows", "Basic");
            Int("whites", "Whites", "Basic");
            Int("blacks", "Blacks", "Basic");

            // ---- Color ------------------------------------------------------
            Int("temperature", "Temperature", "Color");
            Int("tint", "Tint", "Color");
            Int("vibrance", "Vibrance", "Color");
            Int("saturation", "Saturation", "Color");
            Deg("hue", "Hue shift", "Color", -180, 180);

            foreach (var color in new[] { "reds", "oranges", "yellows", "greens", "aquas", "blues", "purples", "magentas" })
            {
                var name = Char.ToUpperInvariant(color[0]) + color.Substring(1);
                Int($"hsl.{color}.hue", $"{name} hue", $"HSL###{name}");
                Int($"hsl.{color}.saturation", $"{name} saturation", $"HSL###{name}");
                Int($"hsl.{color}.luminance", $"{name} luminance", $"HSL###{name}");
            }

            foreach (var range in new[] { "shadows", "midtones", "highlights", "global" })
            {
                var name = Char.ToUpperInvariant(range[0]) + range.Substring(1);
                Deg($"colorGrading.{range}.hue", $"{name} hue", $"Color grading###{name}", 0, 360);
                Uns($"colorGrading.{range}.saturation", $"{name} saturation", $"Color grading###{name}");
                Int($"colorGrading.{range}.luminance", $"{name} luminance", $"Color grading###{name}");
            }
            Uns("colorGrading.blending", "Blending", "Color grading", 0, 100, 50);
            Int("colorGrading.balance", "Balance", "Color grading");

            Int("colorCalibration.shadowsTint", "Shadows tint", "Calibration");
            Int("colorCalibration.redHue", "Red hue", "Calibration");
            Int("colorCalibration.redSaturation", "Red saturation", "Calibration");
            Int("colorCalibration.greenHue", "Green hue", "Calibration");
            Int("colorCalibration.greenSaturation", "Green saturation", "Calibration");
            Int("colorCalibration.blueHue", "Blue hue", "Calibration");
            Int("colorCalibration.blueSaturation", "Blue saturation", "Calibration");

            // ---- Details ----------------------------------------------------
            Int("sharpness", "Sharpness", "Details");
            Uns("sharpnessThreshold", "Sharpness threshold", "Details", 0, 80, 15);
            Int("clarity", "Clarity", "Details");
            Int("dehaze", "Dehaze", "Details");
            Int("structure", "Structure", "Details");
            Int("centr\u00e9", "Centre", "Details"); // RapidRAW's real field name has the accent
            Uns("lumaNoiseReduction", "Luminance NR", "Details");
            Uns("colorNoiseReduction", "Color NR", "Details");
            Int("chromaticAberrationRedCyan", "CA red/cyan", "Details");
            Int("chromaticAberrationBlueYellow", "CA blue/yellow", "Details");

            // ---- Effects ----------------------------------------------------
            Uns("glowAmount", "Glow", "Effects");
            Uns("halationAmount", "Halation", "Effects");
            Uns("flareAmount", "Light flares", "Effects");
            Uns("lensBlurAmount", "Lens blur amount", "Effects###Lens blur", 0, 100, 40);
            Uns("lensBlurDiffusion", "Lens blur diffusion", "Effects###Lens blur");
            Int("vignetteAmount", "Vignette amount", "Effects###Vignette");
            Uns("vignetteMidpoint", "Vignette midpoint", "Effects###Vignette", 0, 100, 50);
            Int("vignetteRoundness", "Vignette roundness", "Effects###Vignette");
            Uns("vignetteFeather", "Vignette feather", "Effects###Vignette", 0, 100, 50);
            Uns("grainAmount", "Grain amount", "Effects###Grain");
            Uns("grainSize", "Grain size", "Effects###Grain", 0, 100, 25);
            Uns("grainRoughness", "Grain roughness", "Effects###Grain", 0, 100, 50);
            Uns("lutIntensity", "LUT intensity", "Effects", 0, 100, 100);

            // ---- Transform --------------------------------------------------
            Tenth("rotation", "Straighten", "Transform", -45, 45);
            Tenth("transformRotate", "Rotate", "Transform", -45, 45);
            Int("transformVertical", "Vertical", "Transform");
            Int("transformHorizontal", "Horizontal", "Transform");
            Int("transformDistortion", "Distortion", "Transform");
            Int("transformAspect", "Aspect", "Transform");
            Uns("transformScale", "Scale", "Transform", 50, 150, 100);
            Int("transformXOffset", "X offset", "Transform");
            Int("transformYOffset", "Y offset", "Transform");
        }

        // Helpers: one per readout style. Ranges are fallbacks; get_params wins.
        private static void Ev(String id, String label, String group) =>
            Add(new Entry(id, label, group, "0.00", " EV", true, 5, -5, 5, 0));

        private static void Int(String id, String label, String group) =>
            Add(new Entry(id, label, group, "0", "", true, 1, -100, 100, 0));

        private static void Uns(String id, String label, String group, Double min = 0, Double max = 100, Double def = 0) =>
            Add(new Entry(id, label, group, "0", "", false, 1, min, max, def));

        private static void Deg(String id, String label, String group, Double min, Double max) =>
            Add(new Entry(id, label, group, "0", "\u00b0", min < 0, 1, min, max, 0));

        private static void Tenth(String id, String label, String group, Double min, Double max) =>
            Add(new Entry(id, label, group, "0.0", "\u00b0", true, 1, min, max, 0));

        private static void Add(Entry entry)
        {
            Entries[entry.Id] = entry;
            Order.Add(entry);
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
            if (diff == 0 || !Entries.TryGetValue(actionParameter, out var entry))
            {
                return;
            }

            Diag.Info($"dial {actionParameter} diff={(diff > 0 ? "+" : "")}{diff}");

            var now = Environment.TickCount64;

            // A physical nudge is a burst of detents, not one. Once the first one has
            // consumed the armed reset, the rest of that burst is the same gesture:
            // swallow movement on this dial until it has been still for a moment.
            if (actionParameter == this._swallowParam && now < this._swallowUntil)
            {
                this._swallowUntil = now + SwallowQuietMs;
                return;
            }

            if (this.RrPlugin.TryConsumeArmedReset())
            {
                // "Press Reset, then turn": this movement selects, it does not adjust.
                lock (this._carry)
                {
                    this._carry.Remove(actionParameter);
                }

                this._swallowParam = actionParameter;
                this._swallowUntil = now + SwallowQuietMs;
                Diag.Info($"reset {actionParameter} (armed); ignoring the rest of this turn");
                this.Client.SendReset(actionParameter);
                return;
            }

            Int32 detents;
            lock (this._carry)
            {
                this._carry.TryGetValue(actionParameter, out var carry);
                var total = carry + diff;
                // Truncate toward zero so a reversal cancels the carry instead of
                // producing a phantom step.
                detents = total / SdkDiffPerDetent;
                this._carry[actionParameter] = total - detents * SdkDiffPerDetent;
            }

            if (detents != 0)
            {
                this.Client.SendStep(actionParameter, detents * entry.StepsPerDetent);
            }
        }

        /// <summary>Dial press (devices that have one): back to the parameter's default.</summary>
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
