# KeepAwake 1.2 - feature spec

Builds on 1.1.0 (docs/DESIGN_SPEC.md still applies). Same constraints: net48, no runtime packages, single exe under 1 MB,
no installer, no admin, no network, keep the custom look, 0.00% idle CPU, no new timers that tick while nothing is pending.

## F1. "Until a time" duration
- The duration menu (main chevron) gets a last item "Until a time..." below Forever.
- It opens a compact popup anchored to the button, painted in the app style: a 24 h time entry (HH:mm, stock TextBox with
  BorderStyle None inside a painted rounded panel, theme colours, MaxLength 5, digits and ':' only), a hint line
  "Tomorrow if earlier than now", and OK / Cancel pill buttons. Enter = OK, Esc = Cancel. Default text = the last used time
  or now + 1 h rounded to the next quarter hour.
- OK activates until that local time; if the time is <= now it means tomorrow. Button label becomes "Keep awake until 17:30".
- Settings: "lastUntil": "17:30" (string). "lastDurationMinutes" is set to -1 while an until-time is the remembered choice.
- Tooltip/ring/sub-line reuse the existing remaining-time logic (the controller already works on EndUtc).
- Extend works as before (adds to EndUtc).

## F2. Expiry warning
- When an active timed period has 5 minutes left (not for Forever, not when the period itself is shorter than 10 minutes),
  show one balloon: title "KeepAwake ends in 5 minutes", text "Click to extend by 1 hour."
- Clicking the balloon extends by 1 hour. Dismissing it does nothing. Only one warning per activation (or per extension:
  extending re-arms the warning).
- The scheduling goes through Core/Schedule.NextWakeMs (add the warning instant as another candidate wake), so there is
  still no periodic timer.

## F3. Stop on battery
- Setting "stopOnBattery": false by default. Tray menu: Options > "Stop when on battery" (checkable).
- On WM_POWERBROADCAST / PBT_APMPOWERSTATUSCHANGE (or SystemEvents.PowerModeChanged), if active and the setting is on and
  SystemInformation.PowerStatus.PowerLineStatus == Offline, deactivate and show a balloon "KeepAwake stopped: running on battery".
- Also checked at activation time: activating while on battery with the setting on shows the same balloon and does not activate.

## F4. Stop when locked
- Setting "stopWhenLocked": false by default. Tray menu: Options > "Stop when Windows is locked".
- SystemEvents.SessionSwitch with SessionSwitchReason.SessionLock: if active and the setting is on, deactivate silently
  (the toast cannot be seen on the lock screen; the tray icon turns grey).
- Unsubscribe SystemEvents on shutdown.

## F5. Global hotkey
- Setting "hotkey": "Ctrl+Alt+K" by default; "" disables. Parsed from a string of modifiers Ctrl/Alt/Shift/Win plus one key
  name (letters, digits, F1-F24). Registered with RegisterHotKey on the main window handle (id 1), re-registered when the
  handle is recreated, unregistered on shutdown. If registration fails (already taken), show one balloon
  "Hotkey Ctrl+Alt+K is in use by another app" and continue.
- Pressing it toggles: inactive -> activate with the remembered choice (duration or until-time); active -> deactivate.
- Tray menu shows the hotkey as a hint on "Show KeepAwake"? No: show it as a disabled-looking right-aligned hint on the
  tray menu's first item is not possible with ToolStripMenuItem; instead add it to the main window footer:
  "v1.2.0 - by Karthik - Ctrl+Alt+K" (omit when disabled).

## F6. Middle-click on the tray icon
- Middle mouse button on the tray icon toggles exactly like the hotkey.

## Settings file (1.2)
{"lastDurationMinutes":480,"lastUntil":"","simulateActivity":false,"theme":"System","stopOnBattery":false,
 "stopWhenLocked":false,"hotkey":"Ctrl+Alt+K"}
Unknown keys are ignored, missing keys keep defaults (the hand-written parser already does this).

## Tray menu (1.2)
Show KeepAwake | --- | Extend > | Deactivate | Options > (Simulate activity, Stop when on battery, Stop when Windows is locked)
| Theme > | --- | Quit

## Tests to add (unit-testable, MSTest)
- Until-time resolution: "17:30" at 16:00 -> today 17:30; at 18:00 -> tomorrow 17:30; invalid strings rejected.
- Schedule.NextWakeMs includes the 5-minute warning instant; no warning for Forever or periods under 10 minutes.
- Warning fires once per activation and re-arms after Extend (controller-level logic with the fake clock).
- Hotkey parser: "Ctrl+Alt+K", "ctrl + shift + f5", "Win+1", "" (disabled), "Ctrl" (no key: invalid), "K" (no modifier: invalid).
- Settings round trip for all new keys; old 1.1 file still loads.
- Battery rule: pure function (active, settingOn, onBattery) -> stop.

## Version
Bump <Version> to 1.2.0. README: document the new features and the settings keys. ARCHITECTURE.md: add the new components.
