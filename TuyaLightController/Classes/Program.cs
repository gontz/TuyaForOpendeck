using BarRaider.SdTools;
using System;

namespace TuyaLightController {
    internal class Program {
        public static SmartRoomServer Server { get; } = new SmartRoomServer();

        static void Main(string[] args) {
            // Uncomment this line of code to allow for debugging
            //while (!System.Diagnostics.Debugger.IsAttached) { System.Threading.Thread.Sleep(100); }

            try {
                var settings = SettingsCache.Load();
                settings.Normalize();
                TuyaApiClient.CurrentSettings = settings;
                Server.ApplySettings(settings);
                if (settings.AutoStartServer) {
                    Server.Start();
                }
            }
            catch (Exception ex) {
                try {
                    Logger.Instance.LogMessage(TracingLevel.ERROR,
                        "TuyaLightController: server bootstrap failed: " + ex.Message);
                }
                catch { }
            }

            AppDomain.CurrentDomain.ProcessExit += (s, e) => {
                try { Server.Stop(); } catch { }
            };

            try {
                SDWrapper.Run(args);
            }
            finally {
                try { Server.Stop(); } catch { }
            }
        }
    }
}
