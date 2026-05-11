# Changelog

All notable changes to TuyaForOpendeck.

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
