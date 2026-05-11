using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TuyaLightController {
    /// <summary>
    /// Stream Deck global settings (managed by GlobalSettingsManager).
    /// Owned by GlobalSettingsAction; read by every other action via ReceivedGlobalSettings.
    /// </summary>
    public class GlobalSettings {
        [JsonProperty(PropertyName = "apiUrl")]
        public string ApiUrl { get; set; } = "http://localhost:5000";

        [JsonProperty(PropertyName = "apiToken")]
        public string ApiToken { get; set; } = "";

        [JsonProperty(PropertyName = "defaultDevices")]
        public DeviceSlugSettings DefaultDevices { get; set; } = new DeviceSlugSettings();

        [JsonProperty(PropertyName = "autoStartServer")]
        public bool AutoStartServer { get; set; } = true;

        [JsonProperty(PropertyName = "serverPort")]
        public int ServerPort { get; set; } = 5000;

        [JsonProperty(PropertyName = "tuyaRegion")]
        public string TuyaRegion { get; set; } = "us";

        [JsonProperty(PropertyName = "tuyaClientId")]
        public string TuyaClientId { get; set; } = "";

        [JsonProperty(PropertyName = "tuyaClientSecret")]
        public string TuyaClientSecret { get; set; } = "";

        [JsonProperty(PropertyName = "plugs")]
        public List<TuyaPlug> Plugs { get; set; } = new List<TuyaPlug>();

        [JsonProperty(PropertyName = "lights")]
        public List<TuyaLight> Lights { get; set; } = new List<TuyaLight>();

        public void Normalize() {
            DefaultDevices?.Normalize();
            if (ServerPort <= 0 || ServerPort > 65535) ServerPort = 5000;
            if (string.IsNullOrWhiteSpace(TuyaRegion)) TuyaRegion = "us";
            ApiUrl = (ApiUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(ApiUrl)) ApiUrl = "http://localhost:5000";
            ApiToken = (ApiToken ?? "").Trim();
            TuyaClientId = (TuyaClientId ?? "").Trim();
            TuyaClientSecret = (TuyaClientSecret ?? "").Trim();
            if (Plugs == null) Plugs = new List<TuyaPlug>();
            if (Lights == null) Lights = new List<TuyaLight>();
            Plugs = Plugs
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id))
                .GroupBy(p => p.Id, System.StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.Button)
                .ThenBy(p => p.Name)
                .ToList();
            Lights = Lights
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Id))
                .GroupBy(l => l.Id, System.StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(l => l.Name)
                .ToList();
        }
    }
}
