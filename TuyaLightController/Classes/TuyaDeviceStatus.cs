using Newtonsoft.Json;

namespace TuyaLightController {
    public class TuyaDeviceStatus {
        [JsonProperty("reachable")]
        public bool Reachable { get; set; }

        [JsonProperty("state")]
        public bool? State { get; set; }

        [JsonProperty("isLight")]
        public bool IsLight { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("error")]
        public string Error { get; set; } = "";
    }
}
