using BarRaider.SdTools;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.setcoloraction")]
    public class SetColorAction : TuyaActionBase<SetColorActionSettings> {
        public SetColorAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload) { }

        public override async void KeyPressed(KeyPayload payload) {
            var slugs = ResolveSlugs(localSettings.Devices);
            if (slugs.Count == 0) {
                await Connection.ShowAlert();
                return;
            }
            await TuyaApiClient.SetColor(slugs,
                localSettings.Hue, localSettings.Saturation, localSettings.Value);
        }
    }
}
