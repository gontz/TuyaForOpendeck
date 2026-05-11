# Changelog

All notable changes to TuyaForOpendeck.

## [1.5.0] — 2026-05-11

### Added
- **Status-aware Scene and Turn On/Off actions** — both actions query real device state via Tuya Cloud (`POST /status`) before deciding what to do. No more local `isActive`/`isOn` booleans drifting out of sync with reality.
- **Scene toggle restored, but smarter** — pressing a Scene tile when every reachable light is already on now turns the scene off. First press still applies the scene.
- **Plug-boot recovery** — when a Scene contains a plug whose downstream bulbs are unreachable on first probe, the action powers the plug, waits ~1.5 s, re-queries, and folds the now-booted bulbs back into the dispatch. Single press, no manual retry.
- **Already-on lights skip `switch_led`** — Scene applied to a light that's currently on just nudges mode/brightness/temp instead of re-issuing the switch command (avoids unnecessary brightness "flicker").
- **Schema-driven device capabilities** — discovery now reads `/v1.1/devices/{id}/specifications` and captures per-code `min`/`max` ranges + nested `h`/`s`/`v` schemas, baked into `TuyaLight.Capabilities`. `LightSpec.For()` prefers per-device capabilities over category defaults.
- **`TuyaPlug.SwitchCode`** — captures whether the plug uses `switch_1` or `switch`. Some Tuya plugs only respond to the unsuffixed variant.
- **`POST /status` endpoint** in `SmartRoomServer` — batched lookup (20 device IDs per cloud call) with per-device fallback. Returns reachability + on/off state.
- **CORS preflight + loopback auth bypass** — `OPTIONS` requests get a 204 with CORS headers; loopback requests skip the token check so the local PI works without configuring one.
- **`--self-test` flag** — `Program.Main --self-test` runs `SelfTestRunner` covering scaling, normalization, capability building, status parsing, dispatch planning, and toggle decisions. Exits 0/1 for CI.
- **More categories in `LightSpec`** — added `dc`, `fwd`, `fsd` alongside `dd`/`xdd`.
- **Bundled `sdpi-components.js`** — was loaded from CDN, now shipped with the plugin (works offline).

### Changed
- **`SmartRoomServer.HandleLight` auto-mode-switch** — sets `work_mode=white` before brightness/temp commands (or `work_mode=colour` before colour), so a bulb in the wrong mode actually responds to the values you send.
- **`/cloud/discover` accepts credentials in the body** — the PI can run discovery against not-yet-saved Tuya Cloud credentials.
- **`GlobalSettings.Normalize()` hardening** — empty `ApiUrl` defaults to `http://localhost:5000`, `ApiToken` is trimmed, and `SettingsCache` calls `Normalize()` on every Load and Save.

### Fixed
- **`deviceOverrides` per-key independence** — moving the brightness slider used to also write the warmth value (and vice-versa), which silently defeated the per-key "Use default" reset. Each slider now writes only its own key.
- **`DeviceSlugSettings._deviceSlugListString` double-serialization** — the backing field was `public`, and Newtonsoft.Json serializes public fields by default, so saved settings had both `deviceSlugListString` and `_deviceSlugListString` keys. Made the field private.

### Removed
- **Local `isActive`/`isOn` flags in Scene and Turn On/Off** — replaced by status queries. (The action no longer goes out of sync when bulbs are controlled by another app, by voice, or by a wall switch.)

## [1.4.0] — 2026-05-07

### Added
- **Per-device scene overrides** — every light in a Scene tile now has its own brightness and warmth sliders inline, side-by-side. Saved per-slug as `deviceOverrides`.
- **Per-device Turn On/Off overrides** — the Turn On/Off action's PI now shows the same per-light brightness/warmth controls. Each device row carries a capability tag (`[plug]` / `[CCT]` / `[RGB+CCT]`) so the visible controls match the device's actual capabilities.
- **`LightSpec`** — resolves the correct DPS code names and value ranges per Tuya category (`dj` V1, `dj` V2, `dd`, `xdd`). Picks `bright_value` vs `bright_value_v2` correctly per device, with the right min/max for each.
- **Atomic light commands** — Scene and Turn On/Off send `state + brightness + temp` to each device in a **single HTTP call**, so Tuya processes them as one command set (no race between separate posts).
- **Plug-first ordering in scenes** — when a Scene tile contains both plugs and lights, the plugs fire first and the action waits ~1.5 s before sending light commands. Lets bulbs powered through a smart plug boot before they're addressed.
- **Tuya `category` stored per light** — discovery captures `device["category"]` from Tuya Cloud and persists it on each `TuyaLight`, used by `LightSpec` to drive correct ranges.

### Changed
- **Scene PI rewrite** — replaces the old `tuyaDevicePicker` shared module with a custom picker that renders brightness/warmth sliders directly under each light. No gear icons, no toggle panel, no clipped-summary box.
- **Turn On/Off PI rewrite** — same per-device override controls and capability tagging.
- **Global Settings PI** — layout polish; `Fetch from Tuya Cloud` now sends a `{}` JSON body so HttpListener accepts the request (was failing with HTTP 411 "Length Required" → surfaced as Tuya error 1013 in the UI).
- **`SmartRoomServer` value scaling** — uses `LightSpec.For(light)` to scale 0–100 % to each product's accepted range. Previously hard-coded 0–1000 with a min of 0, which Tuya rejects for `bright_value*` (min is 10 for V2, 25 for V1).

### Fixed
- **`bright_value=0` rejection** — Tuya returns `params range invalid` (code 1101) for brightness values below the device minimum. The slider's 0 % now maps to the minimum the device accepts (10/1000 on V2, 25/255 on standard V1) instead of being silently dropped.
- **Existing scenes failing to load** — the C# `SceneSettings.deviceOverrides` was typed `string`, but older scenes stored it as a JSON array. Newtonsoft threw on the type mismatch, the action constructor failed, and pressing the tile did nothing. The field now accepts both the new string-of-JSON-object format and the legacy array format via `DeviceOverridesConverter`.
- **Plugin deployment target** — corrected install path to `%APPDATA%\opendeck\plugins\…` (OpenDeck reads from `Roaming`, not `Local`).

### Removed
- **Implicit `work_mode = white` in scenes** — the Scene action no longer forces white mode; it leaves the device's current mode alone and just applies the values you set.

## [1.3.1] — earlier

- Slider reset, sluggish device selection, and missing scene values fixed.

## [1.3.0] — earlier

- Initial 1.3 series.
