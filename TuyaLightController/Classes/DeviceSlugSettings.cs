using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TuyaLightController {
    /// <summary>
    /// Per-action and global device list. Source of truth is the CSV string written by the PI;
    /// the parsed slug list is rebuilt on every set.
    /// </summary>
    public class DeviceSlugSettings {
        private static readonly Regex SlugPattern = new Regex(@"^[a-z0-9-]+$", RegexOptions.Compiled);

        private string _deviceSlugListString;

        [JsonProperty(PropertyName = "deviceSlugListString")]
        public string DeviceSlugListString {
            get => _deviceSlugListString;
            set {
                _deviceSlugListString = value;
                UpdateSlugList(value);
            }
        }

        [JsonProperty(PropertyName = "validatedDeviceSlugListString")]
        public string ValidatedDeviceSlugListString {
            get => string.Join(",\n", DeviceSlugList).Trim();
            set { }
        }

        [JsonIgnore]
        public List<string> DeviceSlugList { get; private set; } = new List<string>();

        [JsonProperty(PropertyName = "useGlobalSettingsOption")]
        public string UseGlobalSettingsOption { get; set; }

        [JsonIgnore]
        public bool UseGlobalSettings => UseGlobalSettingsOption == "global";

        public DeviceSlugSettings() {
            _deviceSlugListString = "";
            UseGlobalSettingsOption = "global";
        }

        public void Normalize() {
            UpdateSlugList(_deviceSlugListString);
        }

        public static bool IsValidSlug(string slug) =>
            !string.IsNullOrWhiteSpace(slug) && SlugPattern.IsMatch(slug);

        private void UpdateSlugList(string input) {
            input = input ?? string.Empty;
            string[] parts = input.Split(new[] { ',', '\n', '\r', ' ', '\t' },
                                         StringSplitOptions.RemoveEmptyEntries);
            DeviceSlugList = parts.Select(p => p.Trim().ToLowerInvariant())
                                  .Where(IsValidSlug)
                                  .Distinct()
                                  .ToList();
        }
    }
}
