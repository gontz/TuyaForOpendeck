using Newtonsoft.Json;

namespace TuyaLightController {
    public class SceneSettings {
        [JsonProperty(PropertyName = "devices")]
        public DeviceSlugSettings Devices { get; set; } = new DeviceSlugSettings();

        [JsonProperty(PropertyName = "brightness")]
        public int Brightness { get; set; } = 75;

        [JsonProperty(PropertyName = "warmth")]
        public int Warmth { get; set; } = 50;
    }
}
