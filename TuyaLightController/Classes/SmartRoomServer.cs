using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TuyaLightController {
    /// <summary>
    /// Embedded smart-room HTTP server. Mirrors the Python api.py contract:
    /// GET /devices, POST /switch/{button}, POST /light/{slug}.
    /// </summary>
    public class SmartRoomServer {
        private readonly TuyaCloudClient _cloud = new TuyaCloudClient();
        private readonly object _stateLock = new object();

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _loopTask;

        private int _port;
        private string _expectedToken = "";
        private List<TuyaPlug> _plugs = new List<TuyaPlug>();
        private List<TuyaLight> _lights = new List<TuyaLight>();

        public bool IsRunning {
            get {
                lock (_stateLock) {
                    return _listener != null && _listener.IsListening;
                }
            }
        }

        public int Port {
            get {
                lock (_stateLock) {
                    return _port;
                }
            }
        }

        /// <summary>Apply settings; restart listener if port changed.</summary>
        public void ApplySettings(GlobalSettings s) {
            if (s == null) return;
            int newPort = s.ServerPort > 0 ? s.ServerPort : 5000;

            bool needRestart;
            lock (_stateLock) {
                needRestart = _listener != null && _listener.IsListening && newPort != _port;
                _port = newPort;
                _expectedToken = s.ApiToken ?? "";
                _plugs = s.Plugs ?? new List<TuyaPlug>();
                _lights = s.Lights ?? new List<TuyaLight>();
            }
            _cloud.Configure(s.TuyaRegion, s.TuyaClientId, s.TuyaClientSecret);

            if (needRestart) {
                Stop();
                Start();
            }
        }

        public void Start() {
            lock (_stateLock) {
                if (_listener != null && _listener.IsListening) return;

                var listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:" + _port + "/");
                listener.Prefixes.Add("http://127.0.0.1:" + _port + "/");
                try {
                    listener.Start();
                }
                catch (Exception ex) {
                    Logger.Instance.LogMessage(TracingLevel.ERROR,
                        "SmartRoomServer: failed to start on port " + _port + ": " + ex.Message);
                    return;
                }

                _listener = listener;
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _loopTask = Task.Run(() => AcceptLoop(listener, token));
                Logger.Instance.LogMessage(TracingLevel.INFO,
                    "SmartRoomServer: listening on http://localhost:" + _port + "/");
            }
        }

        public void Stop() {
            HttpListener listener;
            CancellationTokenSource cts;
            Task loop;
            lock (_stateLock) {
                listener = _listener;
                cts = _cts;
                loop = _loopTask;
                _listener = null;
                _cts = null;
                _loopTask = null;
            }

            try { cts?.Cancel(); } catch { }
            try { listener?.Stop(); } catch { }
            try { listener?.Close(); } catch { }
            try { loop?.Wait(2000); } catch { }
            try { cts?.Dispose(); } catch { }
        }

        private async Task AcceptLoop(HttpListener listener, CancellationToken token) {
            while (!token.IsCancellationRequested && listener.IsListening) {
                HttpListenerContext ctx;
                try {
                    ctx = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { return; }
                catch (HttpListenerException) { return; }
                catch (Exception ex) {
                    Logger.Instance.LogMessage(TracingLevel.WARN,
                        "SmartRoomServer: accept loop error: " + ex.Message);
                    return;
                }

                _ = Task.Run(() => HandleRequestSafe(ctx));
            }
        }

        private async Task HandleRequestSafe(HttpListenerContext ctx) {
            try {
                await HandleRequest(ctx).ConfigureAwait(false);
            }
            catch (Exception ex) {
                Logger.Instance.LogMessage(TracingLevel.ERROR,
                    "SmartRoomServer: handler crashed: " + ex.Message);
                try {
                    await WriteJson(ctx, 500, new { error = ex.Message }).ConfigureAwait(false);
                }
                catch { }
            }
        }

        private async Task HandleRequest(HttpListenerContext ctx) {
            var req = ctx.Request;
            var path = req.Url.AbsolutePath ?? "/";
            var method = req.HttpMethod ?? "GET";

            if (method == "OPTIONS") {
                WriteCorsHeaders(ctx.Response);
                ctx.Response.StatusCode = 204;
                ctx.Response.ContentLength64 = 0;
                try { ctx.Response.OutputStream.Close(); } catch { }
                try { ctx.Response.Close(); } catch { }
                return;
            }

            string expectedToken;
            lock (_stateLock) { expectedToken = _expectedToken; }
            if (IsProtectedRoute(method, path) && !IsLoopbackRequest(ctx.Request)) {
                if (string.IsNullOrWhiteSpace(expectedToken)) {
                    await WriteJson(ctx, 401, new { error = "API token required for non-local requests" }).ConfigureAwait(false);
                    return;
                }

                var providedToken = req.Headers["Authorization"] ?? "";
                if (providedToken != expectedToken) {
                    await WriteJson(ctx, 401, new { error = "Unauthorized" }).ConfigureAwait(false);
                    return;
                }
            }

            if (method == "GET" && path == "/devices") {
                await HandleListDevices(ctx).ConfigureAwait(false);
                return;
            }

            if (method == "POST" && path.StartsWith("/switch/", StringComparison.Ordinal)) {
                await HandleSwitch(ctx, path.Substring("/switch/".Length)).ConfigureAwait(false);
                return;
            }

            if (method == "POST" && path.StartsWith("/light/", StringComparison.Ordinal)) {
                await HandleLight(ctx, path.Substring("/light/".Length)).ConfigureAwait(false);
                return;
            }

            if (method == "POST" && path == "/cloud/discover") {
                await HandleCloudDiscover(ctx).ConfigureAwait(false);
                return;
            }

            if (method == "POST" && path == "/status") {
                await HandleStatus(ctx).ConfigureAwait(false);
                return;
            }

            await WriteJson(ctx, 404, new { error = "Not found", path }).ConfigureAwait(false);
        }

        private async Task HandleListDevices(HttpListenerContext ctx) {
            List<TuyaPlug> plugs;
            List<TuyaLight> lights;
            lock (_stateLock) {
                plugs = new List<TuyaPlug>(_plugs);
                lights = new List<TuyaLight>(_lights);
            }

            var switches = new Dictionary<string, object>();
            foreach (var p in plugs) {
                switches[p.Button.ToString()] = new {
                    name = p.Name ?? "",
                    id = p.Id ?? "",
                    slug = "plug-" + p.Button
                };
            }
            var lightDict = new Dictionary<string, object>();
            foreach (var l in lights) {
                if (string.IsNullOrWhiteSpace(l.Slug)) continue;
                lightDict[l.Slug] = new { name = l.Name ?? "", rgb = l.Rgb };
            }

            await WriteJson(ctx, 200, new { switches, lights = lightDict }).ConfigureAwait(false);
        }

        private async Task HandleSwitch(HttpListenerContext ctx, string buttonStr) {
            if (!int.TryParse(buttonStr, out int button)) {
                await WriteJson(ctx, 400, new { error = "Invalid button number" }).ConfigureAwait(false);
                return;
            }
            TuyaPlug plug = null;
            lock (_stateLock) {
                foreach (var p in _plugs) {
                    if (p.Button == button) { plug = p; break; }
                }
            }
            if (plug == null || string.IsNullOrWhiteSpace(plug.Id)) {
                await WriteJson(ctx, 400, new { error = "Invalid button number" }).ConfigureAwait(false);
                return;
            }

            var body = await ReadJsonBody(ctx.Request).ConfigureAwait(false);
            bool state = (bool?)body["state"] ?? false;

            var commands = new List<Dictionary<string, object>> {
                new Dictionary<string, object> {
                    ["code"] = string.IsNullOrWhiteSpace(plug.SwitchCode) ? "switch_1" : plug.SwitchCode,
                    ["value"] = state
                }
            };

            try {
                var result = await _cloud.SendCommandsAsync(plug.Id, commands).ConfigureAwait(false);
                await WriteJson(ctx, 200, new { result }).ConfigureAwait(false);
            }
            catch (Exception ex) {
                await WriteJson(ctx, 500, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleLight(HttpListenerContext ctx, string slug) {
            TuyaLight light = null;
            lock (_stateLock) {
                foreach (var l in _lights) {
                    if (string.Equals(l.Slug, slug, StringComparison.OrdinalIgnoreCase)) { light = l; break; }
                }
            }
            if (light == null) {
                await WriteJson(ctx, 404, new { error = "unknown light: " + slug }).ConfigureAwait(false);
                return;
            }

            var body = await ReadJsonBody(ctx.Request).ConfigureAwait(false);
            var commands = new List<Dictionary<string, object>>();
            var spec = LightSpec.For(light);

            if (body["state"] != null) {
                commands.Add(new Dictionary<string, object> {
                    ["code"] = spec.SwitchCode,
                    ["value"] = (bool?)body["state"] ?? false
                });
            }
            if (body["mode"] != null) {
                var mode = (string)body["mode"];
                if (mode != "white" && mode != "colour" && mode != "scene" && mode != "music") {
                    await WriteJson(ctx, 400, new { error = "invalid mode: " + mode }).ConfigureAwait(false);
                    return;
                }
                commands.Add(new Dictionary<string, object> {
                    ["code"] = spec.WorkModeCode, ["value"] = mode
                });
            }
            else if (body["color"] != null && spec.SupportsColorMode) {
                commands.Add(new Dictionary<string, object> {
                    ["code"] = spec.WorkModeCode,
                    ["value"] = "colour"
                });
            }
            else if ((body["brightness"] != null || body["temp"] != null) && spec.SupportsWhiteMode) {
                commands.Add(new Dictionary<string, object> {
                    ["code"] = spec.WorkModeCode,
                    ["value"] = "white"
                });
            }
            if (body["brightness"] != null) {
                int pct = (int?)body["brightness"] ?? 0;
                commands.Add(new Dictionary<string, object> {
                    ["code"] = spec.BrightnessCode,
                    ["value"] = ScaleUtil.ScalePercent(pct, spec.BrightnessMin, spec.BrightnessMax)
                });
            }
            if (body["temp"] != null) {
                int pct = (int?)body["temp"] ?? 0;
                commands.Add(new Dictionary<string, object> {
                    ["code"] = spec.TempCode,
                    ["value"] = ScaleUtil.ScalePercent(pct, spec.TempMin, spec.TempMax)
                });
            }
            if (body["color"] != null) {
                if (!light.Rgb) {
                    await WriteJson(ctx, 400, new { error = "device does not support color" }).ConfigureAwait(false);
                    return;
                }
                var color = body["color"] as JArray;
                if (color == null || color.Count < 3) {
                    await WriteJson(ctx, 400, new { error = "color must be [h,s,v]" }).ConfigureAwait(false);
                    return;
                }
                int h = (int?)color[0] ?? 0;
                int s = (int?)color[1] ?? 0;
                int v = (int?)color[2] ?? 0;
                commands.Add(new Dictionary<string, object> {
                    ["code"] = spec.ColorCode,
                    ["value"] = new {
                        h = Math.Max(0, Math.Min(spec.ColorHueMax, h)),
                        s = ScaleUtil.ScalePercent(s, 0, spec.ColorSatMax),
                        v = ScaleUtil.ScalePercent(v, 0, spec.ColorValMax)
                    }
                });
            }

            if (commands.Count == 0) {
                await WriteJson(ctx, 400, new { error = "no fields provided" }).ConfigureAwait(false);
                return;
            }

            try {
                var result = await _cloud.SendCommandsAsync(light.Id, commands).ConfigureAwait(false);
                await WriteJson(ctx, 200, new { result }).ConfigureAwait(false);
            }
            catch (Exception ex) {
                await WriteJson(ctx, 500, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleCloudDiscover(HttpListenerContext ctx) {
            var body = await ReadJsonBody(ctx.Request).ConfigureAwait(false);
            List<TuyaPlug> plugs;
            List<TuyaLight> lights;
            lock (_stateLock) {
                plugs = new List<TuyaPlug>(_plugs);
                lights = new List<TuyaLight>(_lights);
            }

            try {
                var cloud = _cloud;
                var region = ((string)body["tuyaRegion"] ?? "").Trim();
                var clientId = ((string)body["tuyaClientId"] ?? "").Trim();
                var clientSecret = ((string)body["tuyaClientSecret"] ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret)) {
                    cloud = new TuyaCloudClient();
                    cloud.Configure(region, clientId, clientSecret);
                }

                Logger.Instance.LogMessage(TracingLevel.INFO,
                    "SmartRoomServer: Tuya cloud discovery started for region=" + (string.IsNullOrWhiteSpace(region) ? "(saved)" : region));

                var discovered = await cloud.DiscoverDevicesAsync(plugs, lights).ConfigureAwait(false);
                Logger.Instance.LogMessage(TracingLevel.INFO,
                    "SmartRoomServer: Tuya cloud discovery complete, plugs=" + discovered.Plugs.Count +
                    ", lights=" + discovered.Lights.Count + ", ignored=" + discovered.IgnoredDevices);
                await WriteJson(ctx, 200, discovered).ConfigureAwait(false);
            }
            catch (Exception ex) {
                Logger.Instance.LogMessage(TracingLevel.ERROR,
                    "SmartRoomServer: Tuya cloud discovery failed: " + ex.Message);
                await WriteJson(ctx, 500, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private async Task HandleStatus(HttpListenerContext ctx) {
            var body = await ReadJsonBody(ctx.Request).ConfigureAwait(false);
            var slugArray = body["slugs"] as JArray;
            var slugs = (slugArray ?? new JArray())
                .Select(x => (string)x)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var responseDevices = new Dictionary<string, TuyaDeviceStatus>(StringComparer.OrdinalIgnoreCase);
            if (slugs.Count == 0) {
                await WriteJson(ctx, 200, new { devices = responseDevices }).ConfigureAwait(false);
                return;
            }

            List<TuyaPlug> plugs;
            List<TuyaLight> lights;
            lock (_stateLock) {
                plugs = new List<TuyaPlug>(_plugs);
                lights = new List<TuyaLight>(_lights);
            }

            var deviceIds = new List<string>();
            var slugToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slug in slugs) {
                if (slug.StartsWith("plug-", StringComparison.OrdinalIgnoreCase)) {
                    if (int.TryParse(slug.Substring("plug-".Length), out int button)) {
                        var plug = plugs.FirstOrDefault(p => p.Button == button);
                        if (plug != null && !string.IsNullOrWhiteSpace(plug.Id)) {
                            slugToId[slug] = plug.Id;
                            deviceIds.Add(plug.Id);
                        }
                    }
                    continue;
                }

                var light = lights.FirstOrDefault(l => string.Equals(l.Slug, slug, StringComparison.OrdinalIgnoreCase));
                if (light != null && !string.IsNullOrWhiteSpace(light.Id)) {
                    slugToId[slug] = light.Id;
                    deviceIds.Add(light.Id);
                }
            }

            var statusById = await _cloud.GetLatestStatusesAsync(deviceIds).ConfigureAwait(false);
            foreach (var slug in slugs) {
                var item = new TuyaDeviceStatus();
                responseDevices[slug] = item;

                if (!slugToId.TryGetValue(slug, out var deviceId)) {
                    item.Reachable = false;
                    item.Error = "device not configured";
                    continue;
                }

                item.Id = deviceId;
                if (slug.StartsWith("plug-", StringComparison.OrdinalIgnoreCase)) {
                    var plug = plugs.FirstOrDefault(p => string.Equals(p.Id, deviceId, StringComparison.OrdinalIgnoreCase));
                    item.Name = plug?.Name ?? "";
                    item.IsLight = false;
                    var switchCode = string.IsNullOrWhiteSpace(plug?.SwitchCode) ? "switch_1" : plug.SwitchCode;
                    if (statusById.TryGetValue(deviceId, out var status)) {
                        item.Reachable = true;
                        item.State = StatusUtil.ReadSwitchState(status, switchCode);
                    }
                    else {
                        item.Reachable = false;
                        item.Error = "offline";
                    }
                    continue;
                }

                var light = lights.FirstOrDefault(l => string.Equals(l.Id, deviceId, StringComparison.OrdinalIgnoreCase));
                item.Name = light?.Name ?? "";
                item.IsLight = true;
                if (statusById.TryGetValue(deviceId, out var lightStatus)) {
                    item.Reachable = true;
                    var spec = LightSpec.For(light);
                    item.State = StatusUtil.ReadSwitchState(lightStatus, spec.SwitchCode);
                }
                else {
                    item.Reachable = false;
                    item.Error = "offline";
                }
            }

            await WriteJson(ctx, 200, new { devices = responseDevices }).ConfigureAwait(false);
        }

        private static bool IsProtectedRoute(string method, string path) {
            return
                (method == "GET" && path == "/devices") ||
                (method == "POST" && path == "/status") ||
                (method == "POST" && path == "/cloud/discover") ||
                (method == "POST" && path.StartsWith("/switch/", StringComparison.Ordinal)) ||
                (method == "POST" && path.StartsWith("/light/", StringComparison.Ordinal));
        }

        private static bool IsLoopbackRequest(HttpListenerRequest req) {
            var address = req?.RemoteEndPoint?.Address;
            return address != null && IPAddress.IsLoopback(address);
        }

        private static async Task<JObject> ReadJsonBody(HttpListenerRequest req) {
            if (req.ContentLength64 <= 0) return new JObject();
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8)) {
                var text = await reader.ReadToEndAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text)) return new JObject();
                try { return JObject.Parse(text); }
                catch { return new JObject(); }
            }
        }

        private static async Task WriteJson(HttpListenerContext ctx, int status, object payload) {
            var json = JsonConvert.SerializeObject(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            WriteCorsHeaders(ctx.Response);
            ctx.Response.ContentLength64 = bytes.Length;
            try {
                await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            finally {
                try { ctx.Response.OutputStream.Close(); } catch { }
                try { ctx.Response.Close(); } catch { }
            }
        }

        private static void WriteCorsHeaders(HttpListenerResponse response) {
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Headers"] = "Authorization,Content-Type";
            response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
            response.Headers["Access-Control-Max-Age"] = "600";
        }
    }
}

