# TuyaForOpendeck

OpenDeck plugin for controlling local Tuya lights and plugs through the `smart-room` HTTP API.

## What It Does

- Turn selected lights and plugs on or off
- Change light color
- Change light brightness
- Change light white temperature
- Apply toggleable scenes to mixed lights and plugs
- Use per-action device lists or shared global defaults

## How It Works

This plugin does not talk to Tuya Cloud directly.

It sends requests to a local API, expected by default at:

`http://localhost:5000`

That API is provided by the `smart-room` project and exposes:

- `GET /devices`
- `POST /switch/<button>`
- `POST /light/<slug>`

The plugin sends the configured `API_TOKEN` in the `Authorization` header.

## OpenDeck Setup

1. Install the plugin into OpenDeck.
2. Add the `Global Settings` action to any tile.
3. Set:
   - `API URL`
   - `API Token`
4. Confirm `Ping /devices` reports your lights and plugs.
5. Add normal actions like:
   - `Turn On/Off`
   - `Change Color`
   - `Set Temperature`
   - `Change Brightness`
   - `Apply Scene`

## Device Selection

Each action supports two modes:

- `Use Global Default Devices`
- `Use Devices Below`

For reliable OpenDeck behavior, per-action `Use Devices Below` is the safer default.

## Scene Action

`Apply Scene` is a toggle action.

- First press: applies the configured scene
- Second press: turns those same devices off

Scene behavior:

- plugs: power only
- lights: on -> white mode -> brightness -> temperature

This action is intended for presets like:

- bedroom evening lights
- desk work lights
- mixed light + plug groups

## Build

Requirements:

- Windows
- Visual Studio / MSBuild
- .NET Framework 4.8 targeting pack
- restored NuGet packages in `D:\ai\TuyaLightController\packages`

Build command:

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "D:\ai\TuyaLightController\TuyaLightController.sln" `
  /t:Build /p:Configuration=Debug
```

Build output:

`D:\ai\TuyaLightController\TuyaLightController\bin\Debug\com.gontz.tuyalightcontroller.sdPlugin`

## Install Into OpenDeck

Copy the built plugin folder to:

`%APPDATA%\opendeck\plugins\com.gontz.tuyalightcontroller.sdPlugin`

If OpenDeck is running, restart it after replacing the plugin files.

## Project Layout

- `TuyaLightController/manifest.json` - plugin manifest
- `TuyaLightController/Actions/` - action handlers, icons, PI HTML
- `TuyaLightController/Classes/TuyaApiClient.cs` - API transport
- `TuyaLightController/Classes/SettingsCache.cs` - OpenDeck fallback settings cache

## Notes About OpenDeck

This port needed some OpenDeck-specific workarounds:

- fallback cache for global settings
- PI-side fallback for device/API settings
- direct handling of nested settings payloads
- custom device picker behavior for clearer selection state

## Credits

This project was ported and adapted from:

- David Golunski's `GoveeLightController`

Base project:

- https://github.com/DavidGolunski/GoveeLightController

Also built on:

- BarRaider StreamDeck Tools
  - https://github.com/BarRaider/streamdeck-tools

## Current Focus Of This Fork

This fork removes the original Govee, script, League of Legends, and Counter-Strike-specific behavior and replaces it with:

- Tuya device control through a local API
- OpenDeck-focused property inspector behavior
- mixed plug + light scene actions
