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
                await TuyaApiClient.ApplyScene(slugs, false, localSettings.Brightness, localSettings.Warmth);
                isActive = false;
            }
            else {
                await TuyaApiClient.ApplyScene(slugs, true, localSettings.Brightness, localSettings.Warmth);
                isActive = true;
            }

            await Connection.SetStateAsync((uint)(isActive ? 0 : 1));
            await Connection.ShowOk();
        }
    }
}
