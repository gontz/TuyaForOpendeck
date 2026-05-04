using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json.Linq;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.globalsettingsaction")]
    public class GlobalSettingsAction : KeypadBase {
        private GlobalSettings settings = new GlobalSettings();

        public GlobalSettingsAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            GlobalSettingsManager.Instance.RequestGlobalSettings();
            Connection.OnPropertyInspectorDidAppear += OnPropertyInspectorOpened;
        }

        public override void Dispose() {
            Connection.OnPropertyInspectorDidAppear -= OnPropertyInspectorOpened;
        }

        public override async void KeyPressed(KeyPayload payload) {
            TuyaApiClient.CurrentSettings = settings;
            var devices = await TuyaApiClient.GetDevicesAsync();
            if (devices.Count == 0) {
                await Connection.ShowAlert();
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "GlobalSettingsAction test ping returned 0 devices - check API URL and token.");
            }
            else {
                await Connection.ShowOk();
                Logger.Instance.LogMessage(TracingLevel.INFO,
                    $"GlobalSettingsAction test ping OK - {devices.Count} device(s) reported.");
            }
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override void OnTick() { }

        public override void ReceivedSettings(ReceivedSettingsPayload payload) {
            settings = payload.Settings.ToObject<GlobalSettings>() ?? new GlobalSettings();
            TuyaApiClient.CurrentSettings = settings;
            GlobalSettingsManager.Instance.SetGlobalSettings(JObject.FromObject(settings));
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) {
            settings = payload.Settings.ToObject<GlobalSettings>() ?? new GlobalSettings();
            TuyaApiClient.CurrentSettings = settings;
            Connection.SetSettingsAsync(JObject.FromObject(settings)).GetAwaiter().GetResult();
        }

        private void OnPropertyInspectorOpened(object sender,
            SDEventReceivedEventArgs<PropertyInspectorDidAppear> e)
        {
            Connection.SetSettingsAsync(JObject.FromObject(settings)).GetAwaiter().GetResult();
        }
    }
}
