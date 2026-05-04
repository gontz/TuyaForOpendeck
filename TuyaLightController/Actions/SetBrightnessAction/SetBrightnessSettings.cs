using Newtonsoft.Json;

namespace TuyaLightController {

    public class SetBrightnessSettings : DeviceListSettings {

        [JsonProperty(PropertyName = "brightness")]
        public int Brightness { get; set; }

        public SetBrightnessSettings() : base() {
            Brightness = 100;
        }
    }
}

