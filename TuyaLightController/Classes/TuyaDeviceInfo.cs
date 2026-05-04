using Newtonsoft.Json;

namespace TuyaLightController {
    /// <summary>
    /// Mirrors entries returned by GET /devices on the smart-room API.
    /// Both plugs and lights are flattened into a single shape for PI consumption.
    /// </summary>
    public class TuyaDeviceInfo {
        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("rgb")]
        public bool Rgb { get; set; }

        [JsonProperty("isPlug")]
        public bool IsPlug { get; set; }
    }
}
