using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TuyaLightController {
    public class TurnOnOffSettings {
        [JsonProperty(PropertyName = "devices")]
        public DeviceSlugSettings Devices { get; set; } = new DeviceSlugSettings();

        [JsonProperty(PropertyName = "targetState")]
        public string TargetState { get; set; } = "toggle";

        // Per-device overrides applied when turning on. Same format as SceneSettings —
        // a JSON-encoded object string keyed by slug, parsed via the shared converter.
        [JsonProperty(PropertyName = "deviceOverrides")]
        [JsonConverter(typeof(DeviceOverridesConverter))]
        public Dictionary<string, DeviceOverride> ParsedOverrides { get; set; }
            = new Dictionary<string, DeviceOverride>(StringComparer.OrdinalIgnoreCase);

        public bool TryGetBrightness(string slug, out int value) {
            if (ParsedOverrides != null && ParsedOverrides.TryGetValue(slug, out var ov) && ov.Brightness.HasValue) {
                value = ov.Brightness.Value;
                return true;
            }
            value = 0;
            return false;
        }

        public bool TryGetWarmth(string slug, out int value) {
            if (ParsedOverrides != null && ParsedOverrides.TryGetValue(slug, out var ov) && ov.Warmth.HasValue) {
                value = ov.Warmth.Value;
                return true;
            }
            value = 0;
            return false;
        }
    }
}
