using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.settemperaturedialaction")]
    public class SetTemperatureDialAction : TuyaDialActionBase<SetTemperatureDialAction.Settings> {
        public class Settings {
            [JsonProperty(PropertyName = "devices")]
            public DeviceSlugSettings Devices { get; set; } = new DeviceSlugSettings();

            [JsonProperty(PropertyName = "warmth")]
            public int Warmth { get; set; } = 50;

            [JsonProperty(PropertyName = "stepIndex")]
            public int StepIndex { get; set; } = 0;
        }

        private static readonly int[] StepSizes = { 1, 5, 10 };

        public SetTemperatureDialAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            UpdateDialDisplay();
        }

        public override async void DialRotate(DialRotatePayload payload) {
            int step = StepSizes[((localSettings.StepIndex % StepSizes.Length) + StepSizes.Length) % StepSizes.Length];
            int v = localSettings.Warmth + payload.Ticks * step;
            v = v < 0 ? 0 : (v > 100 ? 100 : v);
            localSettings.Warmth = v;
            await SaveLocalSettings();
            UpdateDialDisplay();

            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count > 0) {
                await TuyaApiClient.SetTemperature(slugs, v);
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
            await TuyaApiClient.SetTemperature(slugs, localSettings.Warmth);
        }

        private async void UpdateDialDisplay() {
            int step = StepSizes[((localSettings.StepIndex % StepSizes.Length) + StepSizes.Length) % StepSizes.Length];
            var feedback = new Dictionary<string, string> {
                ["value"] = localSettings.Warmth + "%",
                ["indicator"] = localSettings.Warmth.ToString(),
                ["title"] = "Warmth (±" + step + ")"
            };
            await Connection.SetFeedbackAsync(feedback);
        }
    }
}
