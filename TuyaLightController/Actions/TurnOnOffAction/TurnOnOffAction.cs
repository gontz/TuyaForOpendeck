using BarRaider.SdTools;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.turnonoffaction")]
    public class TurnOnOffAction : TuyaActionBase<TurnOnOffSettings> {
        private bool isOn = false;

        public TurnOnOffAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Connection.SetStateAsync(0).GetAwaiter().GetResult();
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
                    await TuyaApiClient.TurnOn(slugs);
                    isOn = true;
                    break;
                case "off":
                    await TuyaApiClient.TurnOff(slugs);
                    isOn = false;
                    break;
                default:
                    if (isOn) { await TuyaApiClient.TurnOff(slugs); isOn = false; }
                    else { await TuyaApiClient.TurnOn(slugs); isOn = true; }
                    break;
            }
            await Connection.SetStateAsync((uint)(isOn ? 1 : 0));
        }
    }
}
