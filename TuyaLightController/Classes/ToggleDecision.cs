using System.Collections.Generic;
using System.Linq;

namespace TuyaLightController {
    public static class ToggleDecision {
        public static bool ShouldTurnOn(
            IEnumerable<string> reachableSlugs,
            IDictionary<string, TuyaDeviceStatus> statuses)
        {
            var allOn = true;
            foreach (var slug in reachableSlugs) {
                if (!statuses.TryGetValue(slug, out var status) || status.State != true) {
                    allOn = false;
                    break;
                }
            }
            return !allOn;
        }

        public static List<string> ReachableSlugs(
            IEnumerable<string> selectedSlugs,
            IDictionary<string, TuyaDeviceStatus> statuses)
        {
            return selectedSlugs
                .Where(slug => statuses.TryGetValue(slug, out var status) && status.Reachable)
                .ToList();
        }
    }
}
