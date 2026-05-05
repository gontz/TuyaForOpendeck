using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
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
        protected GlobalSettings globalSettings;

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
            NormalizeLocalSettings();
            globalSettings = SettingsCache.Load();
            globalSettings?.Normalize();
            TuyaApiClient.CurrentSettings = globalSettings;
            GlobalSettingsManager.Instance.RequestGlobalSettings();
            Connection.OnPropertyInspectorDidAppear += OnPropertyInspectorOpened;
        }

        public override void Dispose() {
            Connection.OnPropertyInspectorDidAppear -= OnPropertyInspectorOpened;
        }

        public override void OnTick() { }

        public override void KeyReleased(KeyPayload payload) { }

        public override void ReceivedSettings(ReceivedSettingsPayload payload) {
            JsonConvert.PopulateObject(payload.Settings.ToString(), localSettings);
            NormalizeLocalSettings();
            // Don't echo settings back: the PI initiated this update.
            // Echoing while the user is mid-drag races with sdpi-range and snaps the slider back.
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) {
            globalSettings = SettingsCache.Load();
            if (payload.Settings != null && payload.Settings.HasValues) {
                JsonConvert.PopulateObject(payload.Settings.ToString(), globalSettings);
            }
            globalSettings?.Normalize();
            TuyaApiClient.CurrentSettings = globalSettings;
            SettingsCache.Save(globalSettings);
        }

        protected List<string> ResolveSlugs(DeviceSlugSettings perAction) {
            var globalSlugs = globalSettings?.DefaultDevices?.DeviceSlugList ?? new List<string>();
            if (perAction == null) return globalSlugs;
            if (!perAction.UseGlobalSettings) return perAction.DeviceSlugList;
            return globalSlugs.Count > 0 ? globalSlugs : perAction.DeviceSlugList;
        }

        protected Task SaveLocalSettings() =>
            Connection.SetSettingsAsync(JObject.FromObject(localSettings));

        private void OnPropertyInspectorOpened(object sender,
            SDEventReceivedEventArgs<PropertyInspectorDidAppear> e)
        {
            Connection.SetSettingsAsync(JObject.FromObject(localSettings));
        }

        private void NormalizeLocalSettings() {
            var prop = typeof(TSettings).GetProperty("Devices");
            if (prop?.PropertyType == typeof(DeviceSlugSettings)) {
                ((DeviceSlugSettings)prop.GetValue(localSettings))?.Normalize();
            }
        }
    }
}
