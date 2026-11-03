# KeepAwake

A tiny, portable Windows utility that keeps your PC awake - with a live countdown ring, tray controls, and optional activity simulation for apps like Teams and Slack.

![KeepAwake](docs/screenshot.png)

## Features

- Keep awake for 1 h, 2 h, 4 h, 8 h (default), 24 h, Forever, or **until a time** you type (HH:mm, tomorrow if already past)
- Global hotkey (default **Ctrl+Alt+K**) and middle-click on the tray icon toggle keep-awake on and off
- A balloon warns 5 minutes before a timed session ends; click it to extend by 1 hour
- Optional: stop automatically when the PC runs on battery, or when Windows is locked
- Live countdown ring with gradient progress and elapsed time
- Extend the session anytime from the window or the tray menu
- Optional activity simulation - a subtle mouse nudge every 60 s so meeting apps and webpages never see you idle
- System uptime card with boot time and session status
- Light, Dark, and System themes (follows Windows accent colour)
- Single portable exe, no install, no admin rights, < 1 MB

## Download and run

Grab the latest **KeepAwake.exe** from [GitHub Releases](../../releases) and run it - no installation needed.

**Note:** Windows SmartScreen may warn about unsigned apps. Click **More info > Run anyway** to continue.

**Requirements:** Windows 10 (1903+) or Windows 11, 32- or 64-bit.

**Limits:** keeping the PC awake works through `SetThreadExecutionState`, which does not stop a sleep you start yourself (Start > Sleep, the power button, closing the lid when the lid action is Sleep). Some corporate policies and Modern Standby setups can still turn the display off.

## Usage

### Window
- Drag the header strip to move the window; minimize and close hide it to the tray (left-click the tray icon to bring it back). **Quit** is only in the tray menu
- The main split button starts a session for the selected duration - pick it from the chevron menu. **Until a time...** opens a small popup: type a 24 h time (Enter = OK, Esc = Cancel); a time that is already past means tomorrow. The button then reads "Keep awake until 17:30"
- While active: **Extend 1 h** (chevron offers 1 h, 2 h, 4 h, Forever) and **Deactivate**
- Toggle **Simulate activity (Teams, Slack)** to enable idle-app nudges while awake
- When a timer expires, a toast tells you the session is finished. Five minutes before, a toast says "KeepAwake ends in 5 minutes" - click it to extend by 1 hour (once per activation or extension; not for Forever or sessions shorter than 10 minutes)
- The footer shows the hotkey while it is registered. If another app already owns it, a toast says so and the hotkey is skipped

### Tray menu
- Double/single-click the tray icon to show the window
- Tooltip shows the remaining time, e.g. `KeepAwake - Active - 2 h 37 min left`
- Middle-click the tray icon to toggle, like the hotkey: inactive starts with the remembered choice (duration or until-time), active deactivates
- **Extend >** (1 h, 2 h, 4 h, Forever), **Deactivate**, **Options >** (Simulate activity, Stop when on battery, Stop when Windows is locked), **Theme >** (System, Light, Dark), **Quit**
- **Stop when on battery**: deactivates when the charger is unplugged (toast "KeepAwake stopped: running on battery"), and refuses to activate on battery while it is on
- **Stop when Windows is locked**: deactivates silently when the session is locked
- Exiting KeepAwake properly deactivates the awake session first

### Theme
Choose **System** (default), **Light**, or **Dark** from the tray menu; changes apply instantly.

## Settings file

`keepawake.json` is stored beside the exe (fallback: `%LOCALAPPDATA%\KeepAwake\`):

```json
{"lastDurationMinutes":480,"lastUntil":"","simulateActivity":false,"theme":"System",
 "stopOnBattery":false,"stopWhenLocked":false,"hotkey":"Ctrl+Alt+K"}
```

| Key | Meaning |
|---|---|
| `lastDurationMinutes` | last duration in minutes, `0` = Forever, `-1` = the until-time in `lastUntil` |
| `lastUntil` | last until-time as `"HH:mm"`, or empty |
| `simulateActivity` | the activity-simulation toggle |
| `theme` | `System`, `Light` or `Dark` |
| `stopOnBattery` | deactivate (and refuse to activate) on battery |
| `stopWhenLocked` | deactivate when Windows is locked |
| `hotkey` | global hotkey: modifiers `Ctrl`, `Alt`, `Shift`, `Win` plus one letter, digit or `F1`-`F24`, e.g. `Ctrl+Shift+F5` (Shift alone and Alt+F4 are refused); `""` disables it. Changes apply at the next start |

Your last choice, toggles, and theme are remembered. Files from 1.1 still load; unknown keys are ignored and missing keys keep their defaults. Delete the file to reset to defaults. A missing or invalid file means defaults; it is rewritten on the next change. If the file beside the exe is read-only, the settings move to the `%LOCALAPPDATA%\KeepAwake\` folder.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The .NET Framework 4.8 reference assemblies come from the build-only `Microsoft.NETFramework.ReferenceAssemblies` package, so no targeting pack needs to be installed. Nothing from that package ships in the exe.

```bash
dotnet build -c Release src/KeepAwake
```

The build produces a single **KeepAwake.exe** (targeting .NET Framework 4.8, which ships with Windows 10 1903+) in `src/KeepAwake/bin/Release/net48/`.

## How it works

- While active, KeepAwake calls `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)` so Windows won't sleep or turn off the display; deactivating restores the default state
- With simulation enabled, it sends a 1-pixel `SendInput` mouse move back-and-forth every 60 s - invisible to you, but enough to reset idle timers in Teams, Slack, and web pages

## Privacy

No network access, no telemetry, no registry writes - the only file it creates is its own settings JSON.

## License

Released under the [MIT License](LICENSE).

## Author

Made by **Karthik**. Feedback and issues welcome on GitHub.
