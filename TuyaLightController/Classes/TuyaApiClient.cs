using BarRaider.SdTools;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TuyaLightController {
    /// <summary>
    /// Stateless HTTP transport for the smart-room API. All public methods accept
    /// a slug list (mixed lights + plug-N) and fan out POSTs in parallel. Per-action
    /// classes do not keep a TuyaApiClient instance; they call static methods directly.
    /// </summary>
    public static class TuyaApiClient {
        private static readonly HttpClient Http = new HttpClient {
            Timeout = TimeSpan.FromSeconds(5)
        };

        public static GlobalSettings CurrentSettings { get; set; } = new GlobalSettings();

        public static Task TurnOn(IEnumerable<string> slugs) => Dispatch(slugs, state: true);
        public static Task TurnOff(IEnumerable<string> slugs) => Dispatch(slugs, state: false);

        public static Task SetBrightness(IEnumerable<string> slugs, int pct) =>
            Task.WhenAll(slugs.Where(IsLight).Select(s => SendForLight(s, brightness: Clamp(pct, 0, 100))));

        public static Task SetTemperature(IEnumerable<string> slugs, int pct) =>
            Task.WhenAll(slugs.Where(IsLight).Select(s => SendForLight(s, temp: Clamp(pct, 0, 100))));

        public static Task SetColor(IEnumerable<string> slugs, int h, int s, int v) =>
            Task.WhenAll(slugs.Where(IsLight).Select(slug =>
                SendForLight(slug, color: new[] { ((h % 360) + 360) % 360, Clamp(s, 0, 100), Clamp(v, 0, 100) })));

        public static Task SetWhiteMode(IEnumerable<string> slugs) =>
            Task.WhenAll(slugs.Where(IsLight).Select(s => SendForLight(s, mode: "white")));

        public static async Task ApplyScene(IEnumerable<string> slugs, bool powerOn, int brightness, int temp) {
            var sceneSlugs = slugs?.Distinct().ToList() ?? new List<string>();
            if (sceneSlugs.Count == 0) {
                return;
            }

            if (!powerOn) {
                await TurnOff(sceneSlugs).ConfigureAwait(false);
                return;
            }

            await TurnOn(sceneSlugs).ConfigureAwait(false);
            await Task.Delay(200).ConfigureAwait(false);
            await SetWhiteMode(sceneSlugs).ConfigureAwait(false);
            await Task.Delay(350).ConfigureAwait(false);
            await SetBrightness(sceneSlugs, brightness).ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);
            await SetTemperature(sceneSlugs, temp).ConfigureAwait(false);
        }

        public static async Task<List<TuyaDeviceInfo>> GetDevicesAsync() {
            if (!HasConfig()) return new List<TuyaDeviceInfo>();
            var url = BaseUrl() + "/devices";
            using (var req = new HttpRequestMessage(HttpMethod.Get, url)) {
                AddAuthorizationHeader(req);
                try {
                    var res = await Http.SendAsync(req).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) {
                        Logger.Instance.LogMessage(TracingLevel.WARN,
                            $"TuyaApiClient: GET /devices returned {(int)res.StatusCode}");
                        return new List<TuyaDeviceInfo>();
                    }
                    var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseDevices(json);
                }
                catch (Exception ex) {
                    Logger.Instance.LogMessage(TracingLevel.ERROR,
                        $"TuyaApiClient: GET /devices failed: {ex.Message}");
                    return new List<TuyaDeviceInfo>();
                }
            }
        }

        public static bool IsLight(string slug) =>
            !string.IsNullOrEmpty(slug) && !slug.StartsWith("plug-", StringComparison.Ordinal);

        private static Task Dispatch(IEnumerable<string> slugs, bool state) =>
            Task.WhenAll(slugs.Select(slug =>
                IsLight(slug) ? SendForLight(slug, state: state) : SendForPlug(slug, state)));

        private static async Task SendForLight(
            string slug,
            bool? state = null,
            int? brightness = null,
            int? temp = null,
            int[] color = null,
            string mode = null)
        {
            if (!HasConfig()) return;
            var body = new Dictionary<string, object>();
            if (state.HasValue) body["state"] = state.Value;
            if (!string.IsNullOrWhiteSpace(mode)) body["mode"] = mode;
            if (brightness.HasValue) body["brightness"] = brightness.Value;
            if (temp.HasValue) body["temp"] = temp.Value;
            if (color != null) body["color"] = color;
            await PostJson(BaseUrl() + "/light/" + slug, body, slug).ConfigureAwait(false);
        }

        private static async Task SendForPlug(string slug, bool state) {
            if (!HasConfig()) return;
            var n = slug.Substring("plug-".Length);
            await PostJson(BaseUrl() + "/switch/" + n, new Dictionary<string, object> { ["state"] = state }, slug)
                .ConfigureAwait(false);
        }

        private static async Task PostJson(string url, object body, string slugForLog) {
            using (var req = new HttpRequestMessage(HttpMethod.Post, url)) {
                req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                AddAuthorizationHeader(req);
                try {
                    var res = await Http.SendAsync(req).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) {
                        Logger.Instance.LogMessage(TracingLevel.WARN,
                            $"TuyaApiClient: POST {url} for {slugForLog} returned {(int)res.StatusCode}");
                    }
                }
                catch (Exception ex) {
                    Logger.Instance.LogMessage(TracingLevel.ERROR,
                        $"TuyaApiClient: POST {url} for {slugForLog} failed: {ex.Message}");
                }
            }
        }

        private static bool HasConfig() {
            if (CurrentSettings == null || string.IsNullOrWhiteSpace(CurrentSettings.ApiUrl)) {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "TuyaApiClient: API URL not configured; request skipped. Open Global Settings to configure.");
                return false;
            }
            return true;
        }

        private static void AddAuthorizationHeader(HttpRequestMessage req) {
            if (!string.IsNullOrWhiteSpace(CurrentSettings?.ApiToken)) {
                req.Headers.Add("Authorization", CurrentSettings.ApiToken);
            }
        }

        private static string BaseUrl() => CurrentSettings.ApiUrl.TrimEnd('/');

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private static List<TuyaDeviceInfo> ParseDevices(string json) {
            var result = new List<TuyaDeviceInfo>();
            var root = Newtonsoft.Json.Linq.JObject.Parse(json);
            if (root["switches"] is Newtonsoft.Json.Linq.JObject switches) {
                foreach (var kv in switches) {
                    var entry = (Newtonsoft.Json.Linq.JObject)kv.Value;
                    result.Add(new TuyaDeviceInfo {
                        Slug = (string)entry["slug"] ?? ("plug-" + kv.Key),
                        Name = (string)entry["name"] ?? kv.Key,
                        Rgb = false,
                        IsPlug = true
                    });
                }
            }
            if (root["lights"] is Newtonsoft.Json.Linq.JObject lights) {
                foreach (var kv in lights) {
                    var entry = (Newtonsoft.Json.Linq.JObject)kv.Value;
                    result.Add(new TuyaDeviceInfo {
                        Slug = kv.Key,
                        Name = (string)entry["name"] ?? kv.Key,
                        Rgb = (bool?)entry["rgb"] ?? false,
                        IsPlug = false
                    });
                }
            }
            return result;
        }
    }
}



