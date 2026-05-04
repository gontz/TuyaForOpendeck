using Newtonsoft.Json;

namespace TuyaLightController {
    public class SetBrightnessSettings {
        [JsonProperty(PropertyName = "devices")]
        public DeviceSlugSettings Devices { get; set; } = new DeviceSlugSettings();

        [JsonProperty(PropertyName = "brightness")]
        public int Brightness { get; set; } = 50;
    }
}
