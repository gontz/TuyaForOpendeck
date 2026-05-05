using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace TuyaLightController {
    internal static class SettingsCache {
        private const string GlobalSettingsActionId = "com.gontz.tuyalightcontroller.globalsettingsaction";
        private static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "opendeck",
            "plugins",
            "com.gontz.tuyalightcontroller.sdPlugin",
            "global-settings-cache.json");
        private static readonly string ProfilesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "opendeck",
            "profiles");

        public static GlobalSettings Load() {
            try {
                var settings = LoadFromCacheFile();
                if (HasUsableSettings(settings)) {
                    return settings;
                }

                var fromProfiles = TryLoadFromProfiles();
                if (fromProfiles != null) {
                    MergeMissing(settings, fromProfiles);
                    if (HasUsableSettings(settings)) {
                        Save(settings);
                    }
                }
                return settings;
            }
            catch {
                return new GlobalSettings();
            }
        }

        public static void Save(GlobalSettings settings) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                File.WriteAllText(CachePath, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch {
            }
        }

        private static GlobalSettings LoadFromCacheFile() {
            if (!File.Exists(CachePath)) {
                return new GlobalSettings();
            }

            var json = File.ReadAllText(CachePath);
            return JsonConvert.DeserializeObject<GlobalSettings>(json) ?? new GlobalSettings();
        }

        private static GlobalSettings TryLoadFromProfiles() {
            if (!Directory.Exists(ProfilesPath)) {
                return null;
            }

            foreach (var profileFile in Directory.EnumerateFiles(ProfilesPath, "*.json", SearchOption.AllDirectories)) {
                try {
                    var root = JObject.Parse(File.ReadAllText(profileFile));
                    foreach (var token in EnumerateActionEntries(root)) {
                        var uuid = token["action"]?["uuid"]?.ToString();
                        if (!string.Equals(uuid, GlobalSettingsActionId, StringComparison.OrdinalIgnoreCase)) {
                            continue;
                        }

                        var settingsToken = token["settings"];
                        if (settingsToken == null || settingsToken.Type != JTokenType.Object) {
                            continue;
                        }

                        var settings = settingsToken.ToObject<GlobalSettings>();
                        if (settings != null) {
                            return settings;
                        }
                    }
                }
                catch {
                }
            }

            return null;
        }

        private static IEnumerable<JToken> EnumerateActionEntries(JObject root) {
            foreach (var collectionName in new[] { "keys", "sliders", "touchscreens" }) {
                if (!(root[collectionName] is JArray array)) {
                    continue;
                }

                foreach (var item in array) {
                    if (item != null && item.Type == JTokenType.Object) {
                        yield return item;
                    }
                }
            }
        }

        private static bool HasUsableSettings(GlobalSettings settings) {
            if (settings == null) {
                return false;
            }

            return !string.IsNullOrWhiteSpace(settings.ApiUrl)
                && (
                    !string.IsNullOrWhiteSpace(settings.ApiToken)
                    || (settings.DefaultDevices?.DeviceSlugList?.Count ?? 0) > 0
                );
        }

        private static void MergeMissing(GlobalSettings target, GlobalSettings source) {
            if (target == null || source == null) {
                return;
            }

            if (string.IsNullOrWhiteSpace(target.ApiUrl) && !string.IsNullOrWhiteSpace(source.ApiUrl)) {
                target.ApiUrl = source.ApiUrl;
            }

            if (string.IsNullOrWhiteSpace(target.ApiToken) && !string.IsNullOrWhiteSpace(source.ApiToken)) {
                target.ApiToken = source.ApiToken;
            }

            if ((target.DefaultDevices?.DeviceSlugList?.Count ?? 0) == 0 && source.DefaultDevices != null) {
                target.DefaultDevices = source.DefaultDevices;
            }
        }
    }
}
