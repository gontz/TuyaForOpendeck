using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
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

            string expectedToken;
            lock (_stateLock) { expectedToken = _expectedToken; }
            var providedToken = req.Headers["Authorization"] ?? "";
            if (!string.IsNullOrEmpty(expectedToken) && providedToken != expectedToken) {
                await WriteJson(ctx, 401, new { error = "Unauthorized" }).ConfigureAwait(false);
                return;
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
                new Dictionary<string, object> { ["code"] = "switch_1", ["value"] = state }
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
            if (body["brightness"] != null) {
                int pct = (int?)body["brightness"] ?? 0;
                commands.Add(new Dictionary<string, object> {
                    ["code"] = spec.BrightnessCode,
                    ["value"] = Scale(pct, 100, spec.BrightnessMin, spec.BrightnessMax)
                });
            }
            if (body["temp"] != null) {
                int pct = (int?)body["temp"] ?? 0;
                commands.Add(new Dictionary<string, object> {
                    ["code"] = spec.TempCode,
                    ["value"] = Scale(pct, 100, spec.TempMin, spec.TempMax)
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
                        s = Scale(s, 100, 0, spec.ColorSatMax),
                        v = Scale(v, 100, 0, spec.ColorValMax)
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
            List<TuyaPlug> plugs;
            List<TuyaLight> lights;
            lock (_stateLock) {
                plugs = new List<TuyaPlug>(_plugs);
                lights = new List<TuyaLight>(_lights);
            }

            try {
                var discovered = await _cloud.DiscoverDevicesAsync(plugs, lights).ConfigureAwait(false);
                await WriteJson(ctx, 200, discovered).ConfigureAwait(false);
            }
            catch (Exception ex) {
                await WriteJson(ctx, 500, new { error = ex.Message }).ConfigureAwait(false);
            }
        }

        private static int Scale(int val, int inMax, int outMin, int outMax) {
            if (inMax <= 0) return outMin;
            int scaled = (int)Math.Round((double)val * outMax / inMax);
            return Math.Max(outMin, Math.Min(outMax, scaled));
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
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "Authorization,Content-Type";
            ctx.Response.ContentLength64 = bytes.Length;
            try {
                await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            finally {
                try { ctx.Response.OutputStream.Close(); } catch { }
                try { ctx.Response.Close(); } catch { }
            }
        }
    }
}

