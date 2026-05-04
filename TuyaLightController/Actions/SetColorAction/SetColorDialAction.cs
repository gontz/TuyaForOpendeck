using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using BarRaider.SdTools.Wrappers;
using System.Collections.Generic;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.setcolordialaction")]
    public class SetColorDialAction : TuyaDialActionBase<SetColorDialActionSettings> {
        private static readonly int[] StepSizes = { 1, 5, 15 };

        public SetColorDialAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            UpdateDialDisplay();
        }

        public override async void DialRotate(DialRotatePayload payload) {
            int step = StepSizes[((localSettings.StepIndex % StepSizes.Length) + StepSizes.Length) % StepSizes.Length];
            int hue = localSettings.Hue + payload.Ticks * step;
            hue = ((hue % 360) + 360) % 360;
            localSettings.Hue = hue;
            await SaveLocalSettings();
            UpdateDialDisplay();

            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count > 0) {
                await TuyaApiClient.SetColor(slugs, hue, 100, 100);
            }
        }

        public override void DialDown(DialPayload payload) { }

        public override async void DialUp(DialPayload payload) {
            localSettings.StepIndex = (localSettings.StepIndex + 1) % StepSizes.Length;
            await SaveLocalSettings();
            UpdateDialDisplay();
        }

        public override async void TouchPress(TouchpadPressPayload payload) {
            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count == 0) return;
            await TuyaApiClient.SetColor(slugs, localSettings.Hue, 100, 100);
        }

        private async void UpdateDialDisplay() {
            int step = StepSizes[((localSettings.StepIndex % StepSizes.Length) + StepSizes.Length) % StepSizes.Length];
            string imageString = "";
            var color = HsvToRgb(localSettings.Hue, 1.0, 1.0);
            using (var img = ImageTools.GetBitmapFromFilePath("./Actions/SetColorAction/ColorRect.png"))
            using (var tinted = ImageTools.ReplaceColor(img, System.Drawing.Color.Black, color)) {
                imageString = Tools.ImageToBase64(tinted, true);
            }
            var feedback = new Dictionary<string, string> {
                ["value"] = localSettings.Hue + "°",
                ["indicator"] = localSettings.Hue.ToString(),
                ["title"] = "Hue (±" + step + ")",
                ["colorIcon"] = imageString
            };
            await Connection.SetFeedbackAsync(feedback);
        }

        private static System.Drawing.Color HsvToRgb(int hueDeg, double s, double v) {
            double h = (((hueDeg % 360) + 360) % 360) / 60.0;
            double c = v * s;
            double x = c * (1 - System.Math.Abs((h % 2) - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 1) { r = c; g = x; }
            else if (h < 2) { r = x; g = c; }
            else if (h < 3) { g = c; b = x; }
            else if (h < 4) { g = x; b = c; }
            else if (h < 5) { r = x; b = c; }
            else { r = c; b = x; }
            return System.Drawing.Color.FromArgb(
                (int)System.Math.Round((r + m) * 255),
                (int)System.Math.Round((g + m) * 255),
                (int)System.Math.Round((b + m) * 255));
        }
    }
}
