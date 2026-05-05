using Newtonsoft.Json;

namespace TuyaLightController {
    public class TuyaPlug {
        [JsonProperty("button")]
        public int Button { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";
    }
}
