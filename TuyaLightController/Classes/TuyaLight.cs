using Newtonsoft.Json;

namespace TuyaLightController {
    public class TuyaLight {
        [JsonProperty("slug")]
        public string Slug { get; set; } = "";

        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        // Tuya product category code: dj=Light, dd=Light strip, xdd=Smart string light, etc.
        [JsonProperty("category")]
        public string Category { get; set; } = "";

        [JsonProperty("v2")]
        public bool V2 { get; set; }

        [JsonProperty("rgb")]
        public bool Rgb { get; set; }

        [JsonProperty("capabilities")]
        public TuyaLightCapabilities Capabilities { get; set; }
    }
}
