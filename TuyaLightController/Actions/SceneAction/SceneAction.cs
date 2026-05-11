using System.Collections.Generic;
using System.Threading.Tasks;
using BarRaider.SdTools;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.sceneaction")]
    public class SceneAction : TuyaActionBase<SceneSettings> {
        public SceneAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Connection.SetStateAsync(0).GetAwaiter().GetResult();
        }

        public override async void KeyPressed(KeyPayload payload) {
            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count == 0) {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "SceneAction: no device slugs configured (per-action or global).");
                return;
            }

            var statuses = await TuyaApiClient.GetStatusesAsync(slugs);
            var plan = SceneDispatchPlanner.Build(slugs, statuses, TuyaApiClient.IsLight);
            foreach (var slug in plan.OfflineSlugs) {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "SceneAction: device offline/unreachable, skipping slug=" + slug);
            }

            var offLightTasks = new List<Task>();
            foreach (var slug in plan.OffLightSlugs) {
                var b = localSettings.GetBrightness(slug);
                var t = localSettings.GetWarmth(slug);
                offLightTasks.Add(TuyaApiClient.ApplyLightState(slug, state: true, brightnessPct: b, tempPct: t));
            }

            if (plan.PlugOnSlugs.Count > 0) {
                await TuyaApiClient.TurnOn(plan.PlugOnSlugs);
            }

            if (offLightTasks.Count > 0) {
                await Task.WhenAll(offLightTasks);
            }

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

            if (plan.PlugOnSlugs.Count > 0 || offLightTasks.Count > 0 || plan.OnLightSlugs.Count > 0) {
                await Connection.ShowOk();
            }
        }
    }
}
