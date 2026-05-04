using Newtonsoft.Json;

namespace TuyaLightController {
    public class TurnOnOffSettings {
        [JsonProperty(PropertyName = "devices")]
        public DeviceSlugSettings Devices { get; set; } = new DeviceSlugSettings();

        [JsonProperty(PropertyName = "targetState")]
        public string TargetState { get; set; } = "toggle";
    }
}
