using System;
using System.Collections.Generic;

namespace TuyaLightController {
    public static class SceneDispatchPlanner {
        public static SceneDispatchPlan Build(
            IEnumerable<string> slugs,
            IDictionary<string, TuyaDeviceStatus> statuses,
            Func<string, bool> isLight)
        {
            var plan = new SceneDispatchPlan();
            foreach (var slug in slugs) {
                if (!statuses.TryGetValue(slug, out var status) || !status.Reachable) {
                    plan.OfflineSlugs.Add(slug);
                    continue;
                }

                if (!isLight(slug)) {
                    plan.PlugOnSlugs.Add(slug);
                    continue;
                }

                if (status.State == false) plan.OffLightSlugs.Add(slug);
                else plan.OnLightSlugs.Add(slug);
            }
            return plan;
        }
    }
}
