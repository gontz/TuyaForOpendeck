using BarRaider.SdTools;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.setbrightnessaction")]
    public class SetBrightnessAction : TuyaActionBase<SetBrightnessSettings> {
        public SetBrightnessAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload) { }

        public override async void KeyPressed(KeyPayload payload) {
            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count == 0) {
                await Connection.ShowAlert();
                return;
            }
            await TuyaApiClient.SetBrightness(slugs, localSettings.Brightness);
        }
    }
}
