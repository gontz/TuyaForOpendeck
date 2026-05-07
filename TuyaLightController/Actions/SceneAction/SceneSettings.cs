using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TuyaLightController {
    public class SceneSettings {
        [JsonProperty(PropertyName = "devices")]
        public DeviceSlugSettings Devices { get; set; } = new DeviceSlugSettings();

        [JsonProperty(PropertyName = "brightness")]
        public int Brightness { get; set; } = 75;

        [JsonProperty(PropertyName = "warmth")]
        public int Warmth { get; set; } = 50;

        // Tolerant of two on-disk formats:
        //  - new: string containing JSON object {"slug":{"brightness":N,"warmth":N}}
        //  - old: JSON array [{"slug":"...","brightness":N,"warmth":N,"useSceneDefaults":true}]
        [JsonProperty(PropertyName = "deviceOverrides")]
        [JsonConverter(typeof(DeviceOverridesConverter))]
        public Dictionary<string, DeviceOverride> ParsedOverrides { get; set; }
            = new Dictionary<string, DeviceOverride>(StringComparer.OrdinalIgnoreCase);

        public int GetBrightness(string slug) {
            if (ParsedOverrides != null && ParsedOverrides.TryGetValue(slug, out var ov) && ov.Brightness.HasValue)
                return ov.Brightness.Value;
            return Brightness;
        }

        public int GetWarmth(string slug) {
            if (ParsedOverrides != null && ParsedOverrides.TryGetValue(slug, out var ov) && ov.Warmth.HasValue)
                return ov.Warmth.Value;
            return Warmth;
        }
    }

    public class DeviceOverride {
        [JsonProperty(PropertyName = "brightness")]
        public int? Brightness { get; set; }

        [JsonProperty(PropertyName = "warmth")]
        public int? Warmth { get; set; }
    }

    /// <summary>
    /// Reads deviceOverrides from either the old array form or the new string-of-JSON-object form
    /// and produces a slug→override dictionary.
    /// </summary>
    public class DeviceOverridesConverter : JsonConverter {
        public override bool CanConvert(Type objectType) =>
            objectType == typeof(Dictionary<string, DeviceOverride>);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            var result = new Dictionary<string, DeviceOverride>(StringComparer.OrdinalIgnoreCase);
            JToken token;
            try { token = JToken.Load(reader); }
            catch { return result; }
            if (token == null || token.Type == JTokenType.Null) return result;

            try {
                JToken parsed = token;
                if (parsed.Type == JTokenType.String) {
                    var s = (string)parsed;
                    if (string.IsNullOrWhiteSpace(s)) return result;
                    parsed = JToken.Parse(s);
                }
                if (parsed.Type == JTokenType.Object) {
                    foreach (var prop in ((JObject)parsed).Properties()) {
                        var ov = prop.Value.ToObject<DeviceOverride>(serializer);
                        if (ov != null) result[prop.Name] = ov;
                    }
                }
                else if (parsed.Type == JTokenType.Array) {
                    foreach (var item in (JArray)parsed) {
                        if (item.Type != JTokenType.Object) continue;
                        var slug = (string)item["slug"];
                        if (string.IsNullOrWhiteSpace(slug)) continue;
                        var useDefaults = (bool?)item["useSceneDefaults"] ?? false;
                        result[slug] = new DeviceOverride {
                            Brightness = useDefaults ? null : (int?)item["brightness"],
                            Warmth = useDefaults ? null : (int?)item["warmth"]
                        };
                    }
                }
            } catch {
                // Tolerate malformed input — empty result means defaults are used.
            }
            return result;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
            // Persist as a JSON-string of a {slug:{brightness,warmth}} object — matches the PI shape.
            var dict = (Dictionary<string, DeviceOverride>)value
                       ?? new Dictionary<string, DeviceOverride>();
            var inner = JsonConvert.SerializeObject(dict);
            writer.WriteValue(inner);
        }
    }
}
