namespace TuyaLightController {
    /// <summary>
    /// Tuya DPS mapping for a specific light device.
    /// Uses discovered per-device capabilities when available,
    /// then falls back to category defaults.
    /// </summary>
    public sealed class LightSpec {
        public string SwitchCode { get; private set; } = "switch_led";
        public string WorkModeCode { get; private set; } = "work_mode";

        public string BrightnessCode { get; private set; }
        public int BrightnessMin { get; private set; }
        public int BrightnessMax { get; private set; }

        public string TempCode { get; private set; }
        public int TempMin { get; private set; }
        public int TempMax { get; private set; }

        public string ColorCode { get; private set; }
        public int ColorHueMax { get; private set; }
        public int ColorSatMax { get; private set; }
        public int ColorValMax { get; private set; }

        public bool SupportsWhiteMode { get; private set; } = true;
        public bool SupportsColorMode { get; private set; } = true;

        public static LightSpec For(TuyaLight light) {
            var caps = light?.Capabilities;
            if (caps != null && !string.IsNullOrWhiteSpace(caps.BrightnessCode)) {
                return new LightSpec {
                    SwitchCode = string.IsNullOrWhiteSpace(caps.SwitchCode) ? "switch_led" : caps.SwitchCode,
                    WorkModeCode = string.IsNullOrWhiteSpace(caps.WorkModeCode) ? "work_mode" : caps.WorkModeCode,
                    BrightnessCode = caps.BrightnessCode,
                    BrightnessMin = caps.BrightnessMin,
                    BrightnessMax = caps.BrightnessMax,
                    TempCode = caps.TempCode,
                    TempMin = caps.TempMin,
                    TempMax = caps.TempMax,
                    ColorCode = caps.ColorCode,
                    ColorHueMax = caps.ColorHueMax <= 0 ? 360 : caps.ColorHueMax,
                    ColorSatMax = caps.ColorSatMax <= 0 ? 255 : caps.ColorSatMax,
                    ColorValMax = caps.ColorValMax <= 0 ? 255 : caps.ColorValMax,
                    SupportsWhiteMode = caps.SupportsWhiteMode,
                    SupportsColorMode = caps.SupportsColorMode
                };
            }

            return BuildDefault(light);
        }

        private static LightSpec BuildDefault(TuyaLight light) {
            var category = (light?.Category ?? "").ToLowerInvariant();
            bool v2 = light?.V2 ?? false;
            string suffix = v2 ? "_v2" : "";

            switch (category) {
                case "dj":
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

                case "dd":
                case "dc":
                case "xdd":
                case "fwd":
                case "fsd":
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
