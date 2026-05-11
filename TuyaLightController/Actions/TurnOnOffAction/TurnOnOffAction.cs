using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarRaider.SdTools;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.turnonoffaction")]
    public class TurnOnOffAction : TuyaActionBase<TurnOnOffSettings> {
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

            var statuses = await TuyaApiClient.GetStatusesAsync(slugs);
            var reachable = ToggleDecision.ReachableSlugs(slugs, statuses);
            foreach (var slug in slugs) {
                if (!reachable.Contains(slug)) {
                    Logger.Instance.LogMessage(TracingLevel.WARN,
                        "TurnOnOffAction: device offline/unreachable, skipping slug=" + slug);
                }
            }

            if (reachable.Count == 0) {
                await Connection.ShowAlert();
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "TurnOnOffAction: no reachable devices in selected set.");
                return;
            }

            bool finalOn;
            switch ((localSettings.TargetState ?? "").ToLowerInvariant()) {
                case "on":
                    await TurnOnWithOverrides(reachable);
                    finalOn = true;
                    break;
                case "off":
                    await TuyaApiClient.TurnOff(reachable);
                    finalOn = false;
                    break;
                default:
                    if (!ToggleDecision.ShouldTurnOn(reachable, statuses)) {
                        await TuyaApiClient.TurnOff(reachable);
                        finalOn = false;
                    }
                    else {
                        await TurnOnWithOverrides(reachable);
                        finalOn = true;
                    }
                    break;
            }

            await Connection.SetStateAsync((uint)(finalOn ? 0 : 1));
        }

        private async Task TurnOnWithOverrides(List<string> slugs) {
            await TuyaApiClient.TurnOn(slugs);

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
                var tasks = brightnessGroups.Select(kv => TuyaApiClient.SetBrightness(kv.Value, kv.Key));
                await Task.WhenAll(tasks);
            }
            if (tempGroups.Count > 0) {
                await Task.Delay(150);
                var tasks = tempGroups.Select(kv => TuyaApiClient.SetTemperature(kv.Value, kv.Key));
                await Task.WhenAll(tasks);
            }
        }
    }
}
