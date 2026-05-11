using System;

namespace TuyaLightController {
    public static class ScaleUtil {
        public static int ScalePercent(int percent, int outMin, int outMax) {
            var v = Math.Max(0, Math.Min(100, percent));
            var scaled = outMin + (int)Math.Round(v * (outMax - outMin) / 100.0);
            return Math.Max(outMin, Math.Min(outMax, scaled));
        }
    }
}
