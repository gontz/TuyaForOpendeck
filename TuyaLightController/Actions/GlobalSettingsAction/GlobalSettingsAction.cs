using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TuyaLightController {
    [PluginActionId("com.gontz.tuyalightcontroller.globalsettingsaction")]
    public class GlobalSettingsAction : KeypadBase {
        private GlobalSettings settings;

        public GlobalSettingsAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            settings = SettingsCache.Load();
            settings.Normalize();
            SettingsCache.Save(settings);
            TuyaApiClient.CurrentSettings = settings;
            ApplyToServer();
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
            if (settings == null) {
                settings = new GlobalSettings();
            }
            JsonConvert.PopulateObject(payload.Settings.ToString(), settings);
            settings.Normalize();
            TuyaApiClient.CurrentSettings = settings;
            SettingsCache.Save(settings);
            ApplyToServer();
            GlobalSettingsManager.Instance.SetGlobalSettings(JObject.FromObject(settings));
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) {
            settings = SettingsCache.Load();
            if (payload.Settings != null && payload.Settings.HasValues) {
                JsonConvert.PopulateObject(payload.Settings.ToString(), settings);
                SettingsCache.Save(settings);
            }
            settings.Normalize();
            TuyaApiClient.CurrentSettings = settings;
            ApplyToServer();
            Connection.SetSettingsAsync(JObject.FromObject(settings)).GetAwaiter().GetResult();
        }

        private void OnPropertyInspectorOpened(object sender,
            SDEventReceivedEventArgs<PropertyInspectorDidAppear> e)
        {
            Connection.SetSettingsAsync(JObject.FromObject(settings)).GetAwaiter().GetResult();
        }

        private void ApplyToServer() {
            try {
                Program.Server.ApplySettings(settings);
                if (settings.AutoStartServer && !Program.Server.IsRunning) {
                    Program.Server.Start();
                }
                else if (!settings.AutoStartServer && Program.Server.IsRunning) {
                    Program.Server.Stop();
                }
            }
            catch (System.Exception ex) {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "GlobalSettingsAction: ApplyToServer failed: " + ex.Message);
            }
        }
    }
}
