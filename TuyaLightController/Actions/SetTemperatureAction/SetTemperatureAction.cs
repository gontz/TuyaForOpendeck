using BarRaider.SdTools;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.settemperatureaction")]
    public class SetTemperatureAction : TuyaActionBase<SetTemperatureSettings> {
        public SetTemperatureAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload) { }

        public override async void KeyPressed(KeyPayload payload) {
            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count == 0) {
                await Connection.ShowAlert();
                return;
            }
            await TuyaApiClient.SetTemperature(slugs, localSettings.Warmth);
        }
    }
}
