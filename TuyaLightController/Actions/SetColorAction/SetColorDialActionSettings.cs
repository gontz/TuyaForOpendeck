using Newtonsoft.Json;

namespace TuyaLightController {
    public class SetColorDialActionSettings {
        [JsonProperty(PropertyName = "devices")]
        public DeviceSlugSettings Devices { get; set; } = new DeviceSlugSettings();

        [JsonProperty(PropertyName = "hue")]
        public int Hue { get; set; } = 0;

        [JsonProperty(PropertyName = "stepIndex")]
        public int StepIndex { get; set; } = 0;
    }
}
