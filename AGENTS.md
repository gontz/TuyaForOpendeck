# Agent Instructions

Instructions for AI agents (Claude Code, Copilot, etc.) working on TuyaForOpendeck. The user-facing README is `README.md`; this file is the WHY behind decisions and the things you can't tell from reading the code.

## What this is

An OpenDeck plugin (the open-source Stream Deck alternative). Runs as a .NET Framework 4.8 console app spawned by OpenDeck. The plugin embeds an HTTP server (`SmartRoomServer`) that the Property Inspector HTML talks to, and that server in turn talks to Tuya Cloud directly via signed v2 (HMAC-SHA256) requests.

```
OpenDeck ──► TuyaLightController.exe ──► SmartRoomServer (HttpListener on :5000)
                                          │
   PI HTML ◄─────────── localhost:5000 ◄──┘
   (fetches /devices, POSTs /switch /light /status /cloud/discover)
                                          │
                                          ▼
                                    TuyaCloudClient ──► openapi.tuya{us,eu,cn,in}.com
```

The plugin's actions (`Turn On/Off`, `Apply Scene`, `Set Brightness`, etc.) call into `TuyaApiClient`, which POSTs to the local `SmartRoomServer`, which then dispatches to Tuya Cloud. The "double hop" exists so the PI HTML and the plugin code share one path to the cloud.

## Build / test / install

Build:
```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "D:\ai\TuyaLightController\TuyaLightController.sln" `
  /p:Configuration=Release /t:Build
```

The csproj references `..\packages\` — restored NuGet packages must live at `D:\ai\TuyaLightController\packages\`. If working in a worktree at `.claude\worktrees\<name>\`, create a directory junction: `New-Item -ItemType Junction -Path .\packages -Target D:\ai\TuyaLightController\packages`.

Self-test (covers ScaleUtil, GlobalSettings, LightSpec, capability building, status parsing, dispatch planner, toggle decisions):
```powershell
.\TuyaLightController\bin\Release\com.gontz.tuyalightcontroller.sdPlugin\TuyaLightController.exe --self-test
```
Exits 0 on pass, 1 on fail. Run this before claiming a change is done.

Install path: `%APPDATA%\opendeck\plugins\com.gontz.tuyalightcontroller.sdPlugin\` (Roaming, NOT Local — OpenDeck on Windows loads from Roaming). Stale copies may exist in `%LOCALAPPDATA%\opendeck\plugins\` and `%APPDATA%\Elgato\StreamDeck\Plugins\`; ignore those.

Before installing, kill the running plugin process (its `.exe` is locked):
```powershell
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like "*TuyaLight*" } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

Use robocopy with `/XF global-settings-cache.json pluginlog.log scene-debug.log` to preserve user state during reinstall.

## Architecture: the pieces

| File | Role |
|---|---|
| `Classes/Program.cs` | Entry point. Starts `SmartRoomServer`, hands off to `SDWrapper.Run`. Also handles `--self-test`. |
| `Classes/SmartRoomServer.cs` | HttpListener exposing `/devices`, `/switch/{n}`, `/light/{slug}`, `/status`, `/cloud/discover`. |
| `Classes/TuyaCloudClient.cs` | Signed v2 HMAC-SHA256 transport to Tuya Cloud. Token caching, device discovery, capability schema reading, status batching. |
| `Classes/TuyaApiClient.cs` | Static helpers the actions call. Fans out HTTP POSTs to the local `SmartRoomServer`. |
| `Classes/LightSpec.cs` | Maps a `TuyaLight` to its DPS codes + value ranges. Prefers per-device `Capabilities`; falls back to category defaults. |
| `Classes/SceneDispatchPlanner.cs` | Buckets selected slugs into Offline / PlugOn / OffLightSlugs / OnLightSlugs based on status. |
| `Classes/ToggleDecision.cs` | "Should I turn on?" — `true` unless every reachable device is already on. |
| `Classes/SettingsCache.cs` | OpenDeck doesn't reliably surface global settings on plugin start; this caches them to disk and reads back. |
| `Classes/TuyaActionBase.cs` | Base class for keypad actions; settings plumbing + slug resolution. |
| `Actions/*/`*.html | Property Inspector HTML (raw JS, no framework). Talks to the SDPI bridge + the local API. |
| `Actions/_shared/tuyaDevicePicker.js` | Reusable device-list picker for the simple actions (brightness, color, etc). |
| `Actions/_shared/settingsBinding.js` | Two-way settings ↔ DOM binder used by all PIs. |

## Intent behind the design

### Status-aware actions (1.5.0)
Earlier versions used action-local `isActive`/`isOn` booleans to decide what a press should do. They drift: voice control, wall switches, other apps, and even other tiles toggling the same device leave the action's idea of state stale. The 1.5.0 refactor queries `POST /status` first and decides from real state. **Don't add new local-boolean state machines** — query status and pass through `ToggleDecision` or `SceneDispatchPlanner`.

### Plug-boot recovery in Scene
A common setup: bulb is plugged into a smart plug. When the plug is off, the bulb is unreachable. First press of a scene that includes both plug and bulb would historically miss the bulb. `SceneAction.ApplyScene` now: powers plugs → if any device was offline → waits `PlugBootDelayMs` (1500ms) → re-queries → merges newly-online devices into the dispatch. Single press works. If you change the delay or remove the recovery, downstream bulbs need two presses again.

### Schema-driven capabilities
`TuyaCloudClient` reads `/v1.1/devices/{id}/specifications` during discovery and stores per-code `min`/`max` ranges + h/s/v nested schemas in `TuyaLight.Capabilities`. `LightSpec.For()` uses these when present. This replaces hard-coded category defaults (which were brittle — `bright_value_v2=0` got rejected with Tuya error 1101 because the device's actual minimum was 10). **Always re-run "Fetch from Tuya Cloud" after upgrading**: lights discovered before this build have `Capabilities=null` and fall back to category defaults.

### Atomic per-device commands
A Scene applied to a bulb sends `switch_led + work_mode + bright_value + temp_value` as one Tuya `commands` array in a single HTTP call. Splitting them into separate posts races (Tuya processes them out of order at the bulb). `TuyaApiClient.ApplyLightState` is the single-call entry point.

### Auto mode-switch in `HandleLight`
Tuya bulbs have a `work_mode` (`white`, `colour`, `scene`, `music`). Brightness/temp values only take effect in `white` mode; color values only in `colour`. `SmartRoomServer.HandleLight` infers and prepends the correct `work_mode` command if the body didn't include one. **Don't remove this** — it's why scenes work after a colour change.

### Loopback auth bypass
`SmartRoomServer` only enforces the API token for non-loopback requests. The PI HTML runs in OpenDeck and connects to `localhost:5000`; requiring a token there for new users is friction. Network access from another machine still requires the token. The check is in `IsLoopbackRequest`.

### Self-test
`SelfTestRunner` exists because there's no test framework in this repo (target is .NET Framework 4.8 console exe). Tests run in-process via `--self-test` and exit 0/1. Add to it whenever you write or change a pure-function helper (`ScaleUtil`, `StatusUtil`, `ToggleDecision`, `SceneDispatchPlanner`, capability building).

## Gotchas

- **Newtonsoft serializes public fields by default.** `[JsonProperty]` on a property does not prevent a public backing field from also being emitted. Backing fields must be `private`. ([DeviceSlugSettings.cs](TuyaLightController/Classes/DeviceSlugSettings.cs) was bitten by this.)
- **`deviceOverrides` is a string-of-JSON, not JSON.** Stored as `"{\"slug\":{\"brightness\":80}}"`. `DeviceOverridesConverter` reads both that format AND the legacy array-of-objects format. Don't switch storage formats without updating the converter and a self-test case.
- **Slug regex is `^[a-z0-9-]+$`** ([DeviceSlugSettings.cs](TuyaLightController/Classes/DeviceSlugSettings.cs)). Invalid slugs are silently dropped on load. If you add a slug source, normalize before checking.
- **`PreserveNewest` in csproj.** Static assets (HTML, JS, CSS, images) use `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`. New assets need an entry in [TuyaLightController.csproj](TuyaLightController/TuyaLightController.csproj) or they won't ship.
- **OpenDeck plugin process holds its own `.exe` open.** Robocopy will fail to overwrite it. Stop the process first.
- **The PI uses raw JS, not a framework.** Don't introduce React/Vue/etc. PI files are flat HTML loaded by OpenDeck's embedded webview.
- **CRLF warnings on commit are expected** on Windows. `.gitattributes` isn't configured; Git's `core.autocrlf` handles it.

## Don't touch (without a migration story)

- **Settings JSON shape** (`apiUrl`, `apiToken`, `defaultDevices.deviceSlugListString`, `tuyaClientId`, `tuyaClientSecret`, `plugs[]`, `lights[]`). Existing users have these on disk. Adding fields is fine; renaming or restructuring breaks them.
- **Action UUIDs** in `manifest.json` (`com.gontz.tuyalightcontroller.*`). Changing them orphans every existing tile.
- **The `plug-N` slug convention.** `TuyaApiClient.IsLight` and several PI scripts assume plug slugs start with `plug-`. Lights are everything else.

## Versioning

Bump version in both `TuyaLightController/Properties/AssemblyInfo.cs` (`AssemblyVersion` + `AssemblyFileVersion`) AND `TuyaLightController/manifest.json` (`Version`). Mismatched versions confuse OpenDeck. Add a CHANGELOG entry, tag `vX.Y.Z`, push tag, create a GitHub release.
