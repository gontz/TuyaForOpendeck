using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.setbrightnessdialaction")]
    public class SetBrightnessDialAction : TuyaDialActionBase<SetBrightnessDialAction.Settings> {
        public class Settings {
            [JsonProperty(PropertyName = "devices")]
            public DeviceSlugSettings Devices { get; set; } = new DeviceSlugSettings();

            [JsonProperty(PropertyName = "brightness")]
            public int Brightness { get; set; } = 50;

            [JsonProperty(PropertyName = "stepIndex")]
            public int StepIndex { get; set; } = 0;
        }

        private static readonly int[] StepSizes = { 1, 5, 10 };

        public SetBrightnessDialAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            UpdateDialDisplay();
        }

        public override async void DialRotate(DialRotatePayload payload) {
            int step = StepSizes[((localSettings.StepIndex % StepSizes.Length) + StepSizes.Length) % StepSizes.Length];
            int v = localSettings.Brightness + payload.Ticks * step;
            v = v < 0 ? 0 : (v > 100 ? 100 : v);
            localSettings.Brightness = v;
            await SaveLocalSettings();
            UpdateDialDisplay();

            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count > 0) {
                await TuyaApiClient.SetBrightness(slugs, v);
            }
        }

        public override async void DialUp(DialPayload payload) {
            localSettings.StepIndex = (localSettings.StepIndex + 1) % StepSizes.Length;
            await SaveLocalSettings();
            UpdateDialDisplay();
        }

        public override void DialDown(DialPayload payload) { }

        public override async void TouchPress(TouchpadPressPayload payload) {
            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count == 0) return;
            await TuyaApiClient.SetBrightness(slugs, localSettings.Brightness);
        }

        private async void UpdateDialDisplay() {
            int step = StepSizes[((localSettings.StepIndex % StepSizes.Length) + StepSizes.Length) % StepSizes.Length];
            var feedback = new Dictionary<string, string> {
                ["value"] = localSettings.Brightness + "%",
                ["indicator"] = localSettings.Brightness.ToString(),
                ["title"] = "Brightness (±" + step + ")"
            };
            await Connection.SetFeedbackAsync(feedback);
        }
    }
}
