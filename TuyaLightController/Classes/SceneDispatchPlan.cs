using System.Collections.Generic;

namespace TuyaLightController {
    public class SceneDispatchPlan {
        public List<string> OfflineSlugs { get; } = new List<string>();
        public List<string> PlugOnSlugs { get; } = new List<string>();
        public List<string> OffLightSlugs { get; } = new List<string>();
        public List<string> OnLightSlugs { get; } = new List<string>();
    }
}
