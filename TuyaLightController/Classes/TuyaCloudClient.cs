using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TuyaLightController {
    /// <summary>
    /// Direct Tuya Cloud client using the v2 (HMAC-SHA256) signing scheme.
    /// Handles access token caching + auto-refresh and command dispatch.
    /// </summary>
    public class TuyaCloudClient {
        private const string EmptyBodyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        private static readonly Regex PlugSwitchCodePattern = new Regex(@"^switch(_\d+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NonSlugChars = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled);

        private static readonly HttpClient Http = new HttpClient {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
        private readonly object _settingsLock = new object();

        private string _clientId;
        private string _clientSecret;
        private string _baseUrl;

        private string _accessToken;
        private DateTime _accessTokenExpiresUtc = DateTime.MinValue;

        public void Configure(string region, string clientId, string clientSecret) {
            lock (_settingsLock) {
                var newBase = ResolveBaseUrl(region);
                if (clientId != _clientId || clientSecret != _clientSecret || newBase != _baseUrl) {
                    _clientId = clientId;
                    _clientSecret = clientSecret;
                    _baseUrl = newBase;
                    _accessToken = null;
                    _accessTokenExpiresUtc = DateTime.MinValue;
                }
            }
        }

        public bool IsConfigured {
            get {
                lock (_settingsLock) {
                    return !string.IsNullOrWhiteSpace(_clientId)
                        && !string.IsNullOrWhiteSpace(_clientSecret)
                        && !string.IsNullOrWhiteSpace(_baseUrl);
                }
            }
        }

        public async Task<JObject> SendCommandsAsync(string deviceId, List<Dictionary<string, object>> commands) {
            if (!IsConfigured) {
                throw new InvalidOperationException("Tuya Cloud client not configured (region/clientId/clientSecret).");
            }
            if (string.IsNullOrWhiteSpace(deviceId)) {
                throw new ArgumentException("deviceId is required", nameof(deviceId));
            }

            var token = await GetAccessTokenAsync().ConfigureAwait(false);
            var path = "/v1.0/iot-03/devices/" + deviceId + "/commands";
            var bodyObj = new Dictionary<string, object> { ["commands"] = commands };
            var bodyJson = JsonConvert.SerializeObject(bodyObj);

            var resJson = await SendSignedAsync(HttpMethod.Post, path, bodyJson, token).ConfigureAwait(false);
            return resJson;
        }

        public async Task<TuyaDiscoveryResult> DiscoverDevicesAsync(
            IReadOnlyList<TuyaPlug> existingPlugs = null,
            IReadOnlyList<TuyaLight> existingLights = null)
        {
            if (!IsConfigured) {
                throw new InvalidOperationException("Tuya Cloud client not configured (region/clientId/clientSecret).");
            }

            var devices = await GetAllDevicesAsync().ConfigureAwait(false);
            await EnrichDevicesWithDetailsAsync(devices).ConfigureAwait(false);
            var result = new TuyaDiscoveryResult {
                TotalDevices = devices.Count
            };

            var existingPlugsById = (existingPlugs ?? Array.Empty<TuyaPlug>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id))
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var existingLightsById = (existingLights ?? Array.Empty<TuyaLight>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Id))
                .GroupBy(l => l.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var usedSlugs = new HashSet<string>(
                (existingLights ?? Array.Empty<TuyaLight>())
                    .Select(l => l?.Slug)
                    .Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);

            int nextButton = 1;
            foreach (var plug in existingPlugsById.Values) {
                if (plug.Button >= nextButton) {
                    nextButton = plug.Button + 1;
                }
            }

            foreach (var device in devices) {
                var id = (string)device["id"];
                if (string.IsNullOrWhiteSpace(id)) {
                    result.IgnoredDevices++;
                    continue;
                }

                var name = ResolveDisplayName(device, id);
                var codes = await GetCapabilityCodesAsync(device).ConfigureAwait(false);

                if (LooksLikeLight(codes)) {
                    var light = existingLightsById.TryGetValue(id, out var existingLight)
                        ? new TuyaLight {
                            Id = id,
                            Name = name,
                            Slug = string.IsNullOrWhiteSpace(existingLight.Slug) ? "" : existingLight.Slug,
                            Rgb = existingLight.Rgb,
                            V2 = existingLight.V2
                        }
                        : new TuyaLight {
                            Id = id,
                            Name = name
                        };

                    light.Name = name;
                    light.Rgb = codes.Any(c => c.StartsWith("colour_data", StringComparison.OrdinalIgnoreCase));
                    light.V2 = codes.Any(c => c.EndsWith("_v2", StringComparison.OrdinalIgnoreCase));
                    light.Slug = BuildUniqueSlug(name, id, usedSlugs, light.Slug);
                    result.Lights.Add(light);
                    continue;
                }

                if (LooksLikePlug(codes)) {
                    int button = existingPlugsById.TryGetValue(id, out var existingPlug) && existingPlug.Button > 0
                        ? existingPlug.Button
                        : nextButton++;
                    result.Plugs.Add(new TuyaPlug {
                        Button = button,
                        Id = id,
                        Name = name
                    });
                    continue;
                }

                result.IgnoredDevices++;
            }

            result.Plugs = result.Plugs.OrderBy(p => p.Button).ThenBy(p => p.Name).ToList();
            result.Lights = result.Lights.OrderBy(l => l.Name).ToList();
            return result;
        }

        private async Task<string> GetAccessTokenAsync() {
            lock (_settingsLock) {
                if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _accessTokenExpiresUtc.AddMinutes(-2)) {
                    return _accessToken;
                }
            }

            await _tokenLock.WaitAsync().ConfigureAwait(false);
            try {
                lock (_settingsLock) {
                    if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _accessTokenExpiresUtc.AddMinutes(-2)) {
                        return _accessToken;
                    }
                }

                var path = "/v1.0/token?grant_type=1";
                var resJson = await SendSignedAsync(HttpMethod.Get, path, "", null).ConfigureAwait(false);

                var success = (bool?)resJson["success"] ?? false;
                if (!success) {
                    var msg = (string)resJson["msg"] ?? "unknown";
                    var code = (int?)resJson["code"] ?? 0;
                    throw new InvalidOperationException("Tuya token request failed: " + msg + " (code " + code + ")");
                }
                var result = resJson["result"] as JObject;
                if (result == null) throw new InvalidOperationException("Tuya token response missing 'result'.");
                var token = (string)result["access_token"];
                var expireSec = (int?)result["expire_time"] ?? 7200;
                if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Tuya token response missing access_token.");

                lock (_settingsLock) {
                    _accessToken = token;
                    _accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(expireSec);
                }
                return token;
            }
            finally {
                _tokenLock.Release();
            }
        }

        private async Task<List<JObject>> GetAllDevicesAsync() {
            var token = await GetAccessTokenAsync().ConfigureAwait(false);
            try {
                return await GetProjectDevicesAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "TuyaCloudClient: project device query failed, falling back to legacy list: " + ex.Message);
                return await GetLegacyDevicesAsync(token).ConfigureAwait(false);
            }
        }

        private async Task<List<JObject>> GetProjectDevicesAsync(string token) {
            var devices = new List<JObject>();
            const int pageSize = 20;
            string lastId = null;

            for (int pageNo = 1; pageNo <= 20; pageNo++) {
                var path = "/v2.0/cloud/thing/device?page_size=" + pageSize;
                if (!string.IsNullOrWhiteSpace(lastId)) {
                    path += "&last_id=" + Uri.EscapeDataString(lastId);
                }

                var resJson = await SendSignedAsync(HttpMethod.Get, path, "", token).ConfigureAwait(false);

                var success = (bool?)resJson["success"] ?? false;
                if (!success) {
                    var msg = (string)resJson["msg"] ?? "unknown";
                    var code = (int?)resJson["code"] ?? 0;
                    throw new InvalidOperationException("Tuya project device query failed: " + msg + " (code " + code + ")");
                }

                var pageDevices = resJson["result"] as JArray;
                if (pageDevices != null) {
                    foreach (var tokenDevice in pageDevices.OfType<JObject>()) {
                        devices.Add(tokenDevice);
                    }
                }

                if (pageDevices == null || pageDevices.Count == 0 || pageDevices.Count < pageSize) {
                    break;
                }

                var newLastId = (string)pageDevices.Last?["id"];
                if (string.IsNullOrWhiteSpace(newLastId) || string.Equals(newLastId, lastId, StringComparison.OrdinalIgnoreCase)) {
                    break;
                }
                lastId = newLastId;
            }

            return devices;
        }

        private async Task EnrichDevicesWithDetailsAsync(List<JObject> devices) {
            if (devices == null || devices.Count == 0) {
                return;
            }

            var token = await GetAccessTokenAsync().ConfigureAwait(false);
            const int batchSize = 20;
            for (int i = 0; i < devices.Count; i += batchSize) {
                var batch = devices.Skip(i).Take(batchSize)
                    .Where(d => d != null)
                    .ToList();
                if (batch.Count == 0) {
                    continue;
                }

                var ids = batch
                    .Select(d => ((string)d["id"] ?? "").Trim())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (ids.Count == 0) {
                    continue;
                }

                try {
                    var path = "/v2.0/cloud/thing/batch?device_ids=" + Uri.EscapeDataString(string.Join(",", ids));
                    var resJson = await SendSignedAsync(HttpMethod.Get, path, "", token).ConfigureAwait(false);
                    var success = (bool?)resJson["success"] ?? false;
                    if (!success) {
                        continue;
                    }

                    var detailMap = (resJson["result"] as JArray)?
                        .OfType<JObject>()
                        .Select(detail => new {
                            Id = ((string)detail["id"] ?? "").Trim(),
                            Detail = detail
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                        .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().Detail, StringComparer.OrdinalIgnoreCase);

                    if (detailMap == null || detailMap.Count == 0) {
                        continue;
                    }

                    foreach (var device in batch) {
                        var id = ((string)device["id"] ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(id) || !detailMap.TryGetValue(id, out var detail)) {
                            continue;
                        }

                        MergeMissingString(device, detail, "customName");
                        MergeMissingString(device, detail, "custom_name");
                        MergeMissingString(device, detail, "name");
                        MergeMissingString(device, detail, "productName");
                        MergeMissingString(device, detail, "product_name");
                    }
                }
                catch (Exception ex) {
                    Logger.Instance.LogMessage(TracingLevel.WARN,
                        "TuyaCloudClient: device detail enrichment failed: " + ex.Message);
                }
            }
        }

        private async Task<List<JObject>> GetLegacyDevicesAsync(string token) {
            var devices = new List<JObject>();
            const int pageSize = 100;

            for (int pageNo = 1; pageNo <= 20; pageNo++) {
                var path = "/v1.0/devices?page_no=" + pageNo + "&page_size=" + pageSize;
                var resJson = await SendSignedAsync(HttpMethod.Get, path, "", token).ConfigureAwait(false);

                var success = (bool?)resJson["success"] ?? false;
                if (!success) {
                    var msg = (string)resJson["msg"] ?? "unknown";
                    var code = (int?)resJson["code"] ?? 0;
                    throw new InvalidOperationException("Tuya legacy device list request failed: " + msg + " (code " + code + ")");
                }

                var result = resJson["result"] as JObject;
                var pageDevices = result?["devices"] as JArray;
                if (pageDevices != null) {
                    foreach (var tokenDevice in pageDevices.OfType<JObject>()) {
                        devices.Add(tokenDevice);
                    }
                }

                var total = (int?)result?["total"] ?? devices.Count;
                if (pageDevices == null || pageDevices.Count == 0 || devices.Count >= total) {
                    break;
                }
            }

            return devices;
        }

        private async Task<HashSet<string>> GetCapabilityCodesAsync(JObject device) {
            var codes = ExtractCodes(device?["status"] as JArray);
            if (LooksLikeLight(codes) || LooksLikePlug(codes)) {
                return codes;
            }

            var deviceId = (string)device?["id"];
            if (string.IsNullOrWhiteSpace(deviceId)) {
                return codes;
            }

            try {
                var token = await GetAccessTokenAsync().ConfigureAwait(false);
                var specJson = await SendSignedAsync(HttpMethod.Get, "/v1.1/devices/" + deviceId + "/specifications", "", token)
                    .ConfigureAwait(false);
                var success = (bool?)specJson["success"] ?? false;
                if (!success) {
                    return codes;
                }

                var result = specJson["result"] as JObject;
                MergeCodes(codes, result?["functions"] as JArray);
                MergeCodes(codes, result?["status"] as JArray);
            }
            catch (Exception ex) {
                Logger.Instance.LogMessage(TracingLevel.WARN,
                    "TuyaCloudClient: specification lookup failed for " + deviceId + ": " + ex.Message);
            }

            return codes;
        }

        private static HashSet<string> ExtractCodes(JArray entries) {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MergeCodes(codes, entries);
            return codes;
        }

        private static void MergeCodes(HashSet<string> codes, JArray entries) {
            if (entries == null) return;
            foreach (var entry in entries.OfType<JObject>()) {
                var code = (string)entry["code"];
                if (!string.IsNullOrWhiteSpace(code)) {
                    codes.Add(code);
                }
            }
        }

        private static bool LooksLikeLight(HashSet<string> codes) {
            return codes.Any(code =>
                code.Equals("switch_led", StringComparison.OrdinalIgnoreCase)
                || code.Equals("work_mode", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("bright_value", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("temp_value", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("colour_data", StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksLikePlug(HashSet<string> codes) {
            return codes.Any(code =>
                PlugSwitchCodePattern.IsMatch(code)
                || code.StartsWith("countdown_", StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveDisplayName(JObject device, string fallbackId) {
            var customName = GetString(device, "customName", "custom_name");
            if (!string.IsNullOrWhiteSpace(customName)) {
                return customName;
            }

            var name = GetString(device, "name");
            if (!string.IsNullOrWhiteSpace(name)) {
                return name;
            }

            var productName = GetString(device, "productName", "product_name");
            if (!string.IsNullOrWhiteSpace(productName)) {
                return productName;
            }

            return (fallbackId ?? "").Trim();
        }

        private static string GetString(JObject source, params string[] keys) {
            if (source == null || keys == null) {
                return "";
            }

            foreach (var key in keys) {
                var value = ((string)source[key] ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(value)) {
                    return value;
                }
            }

            return "";
        }

        private static void MergeMissingString(JObject target, JObject source, string key) {
            if (target == null || source == null || string.IsNullOrWhiteSpace(key)) {
                return;
            }

            var sourceValue = ((string)source[key] ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sourceValue)) {
                return;
            }

            var targetValue = ((string)target[key] ?? "").Trim();
            if (string.IsNullOrWhiteSpace(targetValue)) {
                target[key] = sourceValue;
            }
        }

        private static string BuildUniqueSlug(string name, string deviceId, HashSet<string> usedSlugs, string preferredSlug = null) {
            string baseSlug = NormalizeSlug(preferredSlug);
            if (string.IsNullOrWhiteSpace(baseSlug)) {
                baseSlug = NormalizeSlug(name);
            }
            if (string.IsNullOrWhiteSpace(baseSlug)) {
                baseSlug = "device-" + (deviceId ?? "").Trim().ToLowerInvariant();
            }
            if (string.IsNullOrWhiteSpace(baseSlug)) {
                baseSlug = "device";
            }

            string slug = baseSlug;
            int suffix = 2;
            while (usedSlugs.Contains(slug)) {
                slug = baseSlug + "-" + suffix++;
            }
            usedSlugs.Add(slug);
            return slug;
        }

        private static string NormalizeSlug(string value) {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var slug = NonSlugChars.Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
            return slug;
        }

        private async Task<JObject> SendSignedAsync(HttpMethod method, string path, string body, string accessToken) {
            string clientId, clientSecret, baseUrl;
            lock (_settingsLock) {
                clientId = _clientId;
                clientSecret = _clientSecret;
                baseUrl = _baseUrl;
            }

            var t = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds).ToString();
            var nonce = Guid.NewGuid().ToString("N");
            var contentHash = string.IsNullOrEmpty(body) ? EmptyBodyHash : Sha256Hex(body);
            var stringToSign = method.Method.ToUpperInvariant() + "\n" + contentHash + "\n" + "" + "\n" + path;
            var signSource = string.IsNullOrEmpty(accessToken)
                ? clientId + t + nonce + stringToSign
                : clientId + accessToken + t + nonce + stringToSign;
            var sign = HmacSha256Hex(clientSecret, signSource).ToUpperInvariant();

            using (var req = new HttpRequestMessage(method, baseUrl + path)) {
                req.Headers.Add("client_id", clientId);
                req.Headers.Add("t", t);
                req.Headers.Add("nonce", nonce);
                req.Headers.Add("sign_method", "HMAC-SHA256");
                req.Headers.Add("sign", sign);
                if (!string.IsNullOrEmpty(accessToken)) {
                    req.Headers.Add("access_token", accessToken);
                }
                if (!string.IsNullOrEmpty(body)) {
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                var res = await Http.SendAsync(req).ConfigureAwait(false);
                var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) {
                    Logger.Instance.LogMessage(TracingLevel.WARN,
                        "TuyaCloudClient: " + method.Method + " " + path + " HTTP " + (int)res.StatusCode + " body=" + Truncate(text, 200));
                    throw new HttpRequestException("Tuya Cloud HTTP " + (int)res.StatusCode);
                }
                try {
                    return JObject.Parse(text);
                }
                catch (Exception ex) {
                    Logger.Instance.LogMessage(TracingLevel.ERROR,
                        "TuyaCloudClient: invalid JSON from " + path + ": " + ex.Message);
                    throw;
                }
            }
        }

        private static string ResolveBaseUrl(string region) {
            switch ((region ?? "us").Trim().ToLowerInvariant()) {
                case "eu": return "https://openapi.tuyaeu.com";
                case "cn": return "https://openapi.tuyacn.com";
                case "in": return "https://openapi.tuyain.com";
                default: return "https://openapi.tuyaus.com";
            }
        }

        private static string Sha256Hex(string s) {
            using (var sha = SHA256.Create()) {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                return ToHex(bytes);
            }
        }

        private static string HmacSha256Hex(string key, string message) {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key))) {
                var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                return ToHex(bytes);
            }
        }

        private static string ToHex(byte[] bytes) {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        private static string Truncate(string s, int max) {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }
    }
}
