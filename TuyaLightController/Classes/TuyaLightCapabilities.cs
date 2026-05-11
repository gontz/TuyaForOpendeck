using Newtonsoft.Json;

namespace TuyaLightController {
    public class TuyaLightCapabilities {
        [JsonProperty("switchCode")]
        public string SwitchCode { get; set; } = "switch_led";

        [JsonProperty("workModeCode")]
        public string WorkModeCode { get; set; } = "work_mode";

        [JsonProperty("brightnessCode")]
        public string BrightnessCode { get; set; } = "bright_value";

        [JsonProperty("brightnessMin")]
        public int BrightnessMin { get; set; } = 25;

        [JsonProperty("brightnessMax")]
        public int BrightnessMax { get; set; } = 255;

        [JsonProperty("tempCode")]
        public string TempCode { get; set; } = "temp_value";

        [JsonProperty("tempMin")]
        public int TempMin { get; set; } = 0;

        [JsonProperty("tempMax")]
        public int TempMax { get; set; } = 255;

        [JsonProperty("colorCode")]
        public string ColorCode { get; set; } = "colour_data";

        [JsonProperty("colorHueMax")]
        public int ColorHueMax { get; set; } = 360;

        [JsonProperty("colorSatMax")]
        public int ColorSatMax { get; set; } = 255;

        [JsonProperty("colorValMax")]
        public int ColorValMax { get; set; } = 255;

        [JsonProperty("supportsWhiteMode")]
        public bool SupportsWhiteMode { get; set; } = true;

        [JsonProperty("supportsColorMode")]
        public bool SupportsColorMode { get; set; } = true;
    }
}
