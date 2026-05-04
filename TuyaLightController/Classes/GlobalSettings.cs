using Newtonsoft.Json;

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
    }
}
