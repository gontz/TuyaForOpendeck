using System.Collections.Generic;
using System.Threading.Tasks;
using BarRaider.SdTools;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.sceneaction")]
    public class SceneAction : TuyaActionBase<SceneSettings> {
        // When a plug in the scene was off, downstream bulbs are unreachable on first probe.
        // Wait this long after powering the plug, then re-query and dispatch to the booted bulbs.
        private const int PlugBootDelayMs = 1500;

        public SceneAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Connection.SetStateAsync(1).GetAwaiter().GetResult();
        }

        public override async void KeyPressed(KeyPayload payload) {
            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count == 0) {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "SceneAction: no device slugs configured (per-action or global).");
                await Connection.ShowAlert();
                return;
            }

            var statuses = await TuyaApiClient.GetStatusesAsync(slugs);
            var reachable = ToggleDecision.ReachableSlugs(slugs, statuses);

            // If everything reachable is already on, the second press turns the scene OFF.
            if (reachable.Count > 0 && !ToggleDecision.ShouldTurnOn(reachable, statuses)) {
                await TuyaApiClient.TurnOff(reachable);
                await Connection.SetStateAsync(1);
                await Connection.ShowOk();
                return;
            }

            await ApplyScene(slugs, statuses);
            await Connection.SetStateAsync(0);
            await Connection.ShowOk();
        }

        private async Task ApplyScene(List<string> slugs, Dictionary<string, TuyaDeviceStatus> statuses) {
            var plan = SceneDispatchPlanner.Build(slugs, statuses, TuyaApiClient.IsLight);

            foreach (var slug in plan.OfflineSlugs) {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "SceneAction: device offline on first probe, slug=" + slug);
            }

            // Step 1 — power the plugs. Downstream bulbs (powered through them) are most
            // likely the ones in OfflineSlugs.
            if (plan.PlugOnSlugs.Count > 0) {
                await TuyaApiClient.TurnOn(plan.PlugOnSlugs);
            }

            // Step 2 — if any device was offline AND we just turned a plug on, give the
            // downstream bulbs time to boot, then re-query and fold them back into the plan.
            if (plan.PlugOnSlugs.Count > 0 && plan.OfflineSlugs.Count > 0) {
                await Task.Delay(PlugBootDelayMs);
                var rescan = await TuyaApiClient.GetStatusesAsync(plan.OfflineSlugs);
                var recovered = SceneDispatchPlanner.Build(plan.OfflineSlugs, rescan, TuyaApiClient.IsLight);
                plan.OffLightSlugs.AddRange(recovered.OffLightSlugs);
                plan.OnLightSlugs.AddRange(recovered.OnLightSlugs);
                foreach (var slug in recovered.OfflineSlugs) {
                    Logger.Instance.LogMessage(TracingLevel.WARN,
                        "SceneAction: still offline after plug-boot wait, slug=" + slug);
                }
            }

            // Step 3 — off-lights: send state+brightness+temp atomically per device.
            var offLightTasks = new List<Task>();
            foreach (var slug in plan.OffLightSlugs) {
                var b = localSettings.GetBrightness(slug);
                var t = localSettings.GetWarmth(slug);
                offLightTasks.Add(TuyaApiClient.ApplyLightState(slug, state: true, brightnessPct: b, tempPct: t));
            }
            if (offLightTasks.Count > 0) await Task.WhenAll(offLightTasks);

            // Step 4 — on-lights: just nudge mode + brightness + temp without re-issuing switch_led=true.
            if (plan.OnLightSlugs.Count > 0) {
                var brightnessGroups = new Dictionary<int, List<string>>();
                var tempGroups = new Dictionary<int, List<string>>();
                foreach (var slug in plan.OnLightSlugs) {
                    var b = localSettings.GetBrightness(slug);
                    if (!brightnessGroups.ContainsKey(b)) brightnessGroups[b] = new List<string>();
                    brightnessGroups[b].Add(slug);

                    var t = localSettings.GetWarmth(slug);
                    if (!tempGroups.ContainsKey(t)) tempGroups[t] = new List<string>();
                    tempGroups[t].Add(slug);
                }

                await TuyaApiClient.SetWhiteMode(plan.OnLightSlugs);

                var brightnessTasks = new List<Task>();
                foreach (var kv in brightnessGroups) brightnessTasks.Add(TuyaApiClient.SetBrightness(kv.Value, kv.Key));
                if (brightnessTasks.Count > 0) await Task.WhenAll(brightnessTasks);

                var tempTasks = new List<Task>();
                foreach (var kv in tempGroups) tempTasks.Add(TuyaApiClient.SetTemperature(kv.Value, kv.Key));
                if (tempTasks.Count > 0) await Task.WhenAll(tempTasks);
            }
        }
    }
}
