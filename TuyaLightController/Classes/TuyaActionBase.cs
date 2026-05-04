using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TuyaLightController {
    /// <summary>
    /// Base for keypad actions that need access to GlobalSettings.
    /// </summary>
    public abstract class TuyaActionBase<TSettings> : KeypadBase
        where TSettings : class, new()
    {
        protected readonly TSettings localSettings;
        protected GlobalSettings globalSettings = new GlobalSettings();

        protected TuyaActionBase(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0) {
                this.localSettings = new TSettings();
                SaveLocalSettings().GetAwaiter().GetResult();
            }
            else {
                this.localSettings = payload.Settings.ToObject<TSettings>();
            }
            GlobalSettingsManager.Instance.RequestGlobalSettings();
            Connection.OnPropertyInspectorDidAppear += OnPropertyInspectorOpened;
        }

        public override void Dispose() {
            Connection.OnPropertyInspectorDidAppear -= OnPropertyInspectorOpened;
        }

        public override void OnTick() { }

        public override void KeyReleased(KeyPayload payload) { }

        public override void ReceivedSettings(ReceivedSettingsPayload payload) {
            Tools.AutoPopulateSettings(localSettings, payload.Settings);
            SaveLocalSettings();
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) {
            globalSettings = payload.Settings.ToObject<GlobalSettings>() ?? new GlobalSettings();
            TuyaApiClient.CurrentSettings = globalSettings;
        }

        protected List<string> ResolveSlugs(DeviceSlugSettings perAction) {
            if (perAction == null) return globalSettings?.DefaultDevices?.DeviceSlugList ?? new List<string>();
            return perAction.UseGlobalSettings
                ? globalSettings?.DefaultDevices?.DeviceSlugList ?? new List<string>()
                : perAction.DeviceSlugList;
        }

        protected Task SaveLocalSettings() =>
            Connection.SetSettingsAsync(JObject.FromObject(localSettings));

        private void OnPropertyInspectorOpened(object sender,
            SDEventReceivedEventArgs<PropertyInspectorDidAppear> e)
        {
            Connection.SetSettingsAsync(JObject.FromObject(localSettings));
        }
    }
}
