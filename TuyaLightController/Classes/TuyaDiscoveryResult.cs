using Newtonsoft.Json;
using System.Collections.Generic;

namespace TuyaLightController {
    public class TuyaDiscoveryResult {
        [JsonProperty("plugs")]
        public List<TuyaPlug> Plugs { get; set; } = new List<TuyaPlug>();

        [JsonProperty("lights")]
        public List<TuyaLight> Lights { get; set; } = new List<TuyaLight>();

        [JsonProperty("totalDevices")]
        public int TotalDevices { get; set; }

        [JsonProperty("ignoredDevices")]
        public int IgnoredDevices { get; set; }
    }
}
