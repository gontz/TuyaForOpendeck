namespace TuyaLightController {
    /// <summary>
    /// Per-device specification: which DPS code names and value ranges Tuya expects
    /// for THIS specific device. Resolved from the device's product category and the
    /// V2-suffix flag.
    ///
    /// Reference: https://developer.tuya.com/en/docs/iot/dj
    /// Categories observed in this user's account:
    ///   dj  = Light (bulbs, RGB+CCT, etc) — V1 or V2 codes
    ///   dd  = Light strip (LED tape) — V1 codes, 0..1000 range (non-standard)
    ///   xdd = Smart string light / Ceiling light — V1 codes, 0..1000 range (non-standard)
    /// </summary>
    public sealed class LightSpec {
        public string BrightnessCode { get; private set; }
        public int BrightnessMin { get; private set; }
        public int BrightnessMax { get; private set; }

        public string TempCode { get; private set; }
        public int TempMin { get; private set; }
        public int TempMax { get; private set; }

        public string ColorCode { get; private set; }
        public int ColorHueMax { get; private set; }   // always 360
        public int ColorSatMax { get; private set; }
        public int ColorValMax { get; private set; }

        public string WorkModeCode => "work_mode";
        public string SwitchCode   => "switch_led";

        /// <summary>Resolve the spec for a given light, falling back to safe defaults.</summary>
        public static LightSpec For(TuyaLight light) {
            var category = (light?.Category ?? "").ToLowerInvariant();
            bool v2 = light?.V2 ?? false;
            string suffix = v2 ? "_v2" : "";

            switch (category) {
                case "dj":
                    // Standard Tuya light. V2 codes: 10..1000 ranges; V1 codes: 25..255 ranges.
                    return new LightSpec {
                        BrightnessCode = "bright_value" + suffix,
                        BrightnessMin = v2 ? 10 : 25,
                        BrightnessMax = v2 ? 1000 : 255,
                        TempCode = "temp_value" + suffix,
                        TempMin = 0,
                        TempMax = v2 ? 1000 : 255,
                        ColorCode = "colour_data" + suffix,
                        ColorHueMax = 360,
                        ColorSatMax = v2 ? 1000 : 255,
                        ColorValMax = v2 ? 1000 : 255
                    };

                case "dd":   // Light strip (LSC Led Strip 5M etc) — V1 names but 0..1000 range
                case "xdd":  // Smart string / ceiling light (LSC ceiling halo etc) — V1 names but 0..1000 range
                    return new LightSpec {
                        BrightnessCode = "bright_value",
                        BrightnessMin = 10,
                        BrightnessMax = 1000,
                        TempCode = "temp_value",
                        TempMin = 0,
                        TempMax = 1000,
                        ColorCode = "colour_data",
                        ColorHueMax = 360,
                        ColorSatMax = 1000,
                        ColorValMax = 1000
                    };

                default:
                    // Unknown category — use V2 if flagged, else conservative V1 ranges.
                    return new LightSpec {
                        BrightnessCode = "bright_value" + suffix,
                        BrightnessMin = v2 ? 10 : 25,
                        BrightnessMax = v2 ? 1000 : 255,
                        TempCode = "temp_value" + suffix,
                        TempMin = 0,
                        TempMax = v2 ? 1000 : 255,
                        ColorCode = "colour_data" + suffix,
                        ColorHueMax = 360,
                        ColorSatMax = v2 ? 1000 : 255,
                        ColorValMax = v2 ? 1000 : 255
                    };
            }
        }
    }
}
