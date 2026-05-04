using Newtonsoft.Json;

namespace TuyaLightController {
    public class SetColorActionSettings {
        [JsonProperty(PropertyName = "devices")]
        public DeviceSlugSettings Devices { get; set; } = new DeviceSlugSettings();

        [JsonProperty(PropertyName = "hue")]
        public int Hue { get; set; } = 0;

        [JsonProperty(PropertyName = "saturation")]
        public int Saturation { get; set; } = 100;

        [JsonProperty(PropertyName = "value")]
        public int Value { get; set; } = 100;
    }
}
