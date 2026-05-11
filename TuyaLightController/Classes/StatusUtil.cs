using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TuyaLightController {
    public static class StatusUtil {
        public static bool? ReadSwitchState(JArray status, string code) {
            if (status == null || string.IsNullOrWhiteSpace(code)) return null;
            foreach (var row in status.OfType<JObject>()) {
                var rowCode = (string)row["code"];
                if (!string.Equals(rowCode, code, StringComparison.OrdinalIgnoreCase)) continue;
                return (bool?)row["value"];
            }
            return null;
        }
    }
}
