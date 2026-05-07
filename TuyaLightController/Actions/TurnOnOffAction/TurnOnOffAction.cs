using System.Collections.Generic;
using System.Threading.Tasks;
using BarRaider.SdTools;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.turnonoffaction")]
    public class TurnOnOffAction : TuyaActionBase<TurnOnOffSettings> {
        private bool isOn = false;

        public TurnOnOffAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Connection.SetStateAsync(1).GetAwaiter().GetResult();
        }

        public override async void KeyPressed(KeyPayload payload) {
            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count == 0) {
                await Connection.ShowAlert();
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "TurnOnOffAction: no device slugs configured (per-action or global).");
                return;
            }

            switch (localSettings.TargetState) {
                case "on":
                    await TurnOnWithOverrides(slugs);
                    isOn = true;
                    break;
                case "off":
                    await TuyaApiClient.TurnOff(slugs);
                    isOn = false;
                    break;
                default:
                    if (isOn) { await TuyaApiClient.TurnOff(slugs); isOn = false; }
                    else { await TurnOnWithOverrides(slugs); isOn = true; }
                    break;
            }
            await Connection.SetStateAsync((uint)(isOn ? 0 : 1));
        }

        /// <summary>
        /// Turn the slugs ON, then apply per-light brightness/temperature overrides for any
        /// slug that has them. Plugs and slugs without overrides just stay on.
        /// </summary>
        private async Task TurnOnWithOverrides(List<string> slugs) {
            await TuyaApiClient.TurnOn(slugs);

            // Group by override values to minimize HTTP calls.
            var brightnessGroups = new Dictionary<int, List<string>>();
            var tempGroups = new Dictionary<int, List<string>>();
            foreach (var slug in slugs) {
                if (!TuyaApiClient.IsLight(slug)) continue;
                if (localSettings.TryGetBrightness(slug, out var b)) {
                    if (!brightnessGroups.ContainsKey(b)) brightnessGroups[b] = new List<string>();
                    brightnessGroups[b].Add(slug);
                }
                if (localSettings.TryGetWarmth(slug, out var t)) {
                    if (!tempGroups.ContainsKey(t)) tempGroups[t] = new List<string>();
                    tempGroups[t].Add(slug);
                }
            }

            if (brightnessGroups.Count == 0 && tempGroups.Count == 0) return;

            await Task.Delay(200);

            if (brightnessGroups.Count > 0) {
                var tasks = new List<Task>();
                foreach (var kv in brightnessGroups) {
                    tasks.Add(TuyaApiClient.SetBrightness(kv.Value, kv.Key));
                }
                await Task.WhenAll(tasks);
                if (tempGroups.Count > 0) await Task.Delay(150);
            }

            if (tempGroups.Count > 0) {
                var tasks = new List<Task>();
                foreach (var kv in tempGroups) {
                    tasks.Add(TuyaApiClient.SetTemperature(kv.Value, kv.Key));
                }
                await Task.WhenAll(tasks);
            }
        }
    }
}
