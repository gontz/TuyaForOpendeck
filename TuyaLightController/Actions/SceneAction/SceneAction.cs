using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarRaider.SdTools;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.sceneaction")]
    public class SceneAction : TuyaActionBase<SceneSettings> {
        private bool isActive = false;

        public SceneAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Connection.SetStateAsync(1).GetAwaiter().GetResult();
        }

        public override async void KeyPressed(KeyPayload payload) {
            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count == 0) {
                await Connection.ShowAlert();
                return;
            }

            if (isActive) {
                await TuyaApiClient.TurnOff(slugs);
                isActive = false;
            }
            else {
                await ApplyWithOverrides(slugs);
                isActive = true;
            }

            await Connection.SetStateAsync((uint)(isActive ? 0 : 1));
            await Connection.ShowOk();
        }

        // Smart bulbs can be powered through a smart plug. When the plug is off, the bulb has
        // no power and isn't reachable. So: switch plugs ON first, wait for the downstream
        // bulbs to come online, then send the atomic per-light state+brightness+temp commands.
        private const int PlugBootDelayMs = 1500;

        private async Task ApplyWithOverrides(List<string> slugs) {
            var plugSlugs = slugs.Where(s => !TuyaApiClient.IsLight(s)).ToList();
            var lightSlugs = slugs.Where(s => TuyaApiClient.IsLight(s)).ToList();

            // Step 1 — turn the plugs on so any downstream bulb gets mains power.
            if (plugSlugs.Count > 0) {
                await TuyaApiClient.TurnOn(plugSlugs);
                if (lightSlugs.Count > 0) {
                    await Task.Delay(PlugBootDelayMs);
                }
            }

            // Step 2 — apply atomic state+brightness+temp to each bulb in parallel.
            if (lightSlugs.Count > 0) {
                var tasks = new List<Task>();
                foreach (var slug in lightSlugs) {
                    var b = localSettings.GetBrightness(slug);
                    var t = localSettings.GetWarmth(slug);
                    tasks.Add(TuyaApiClient.ApplyLightState(slug, state: true, brightnessPct: b, tempPct: t));
                }
                await Task.WhenAll(tasks);
            }
        }
    }
}
