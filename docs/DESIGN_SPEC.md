# KeepAwake - design spec v1.0 (signed off 2026-11-02)

## Window
- 360 x 480 px at 100% DPI (DPI-aware, scales with system). Frameless, not resizable, centred on first show, position remembered per session only.
- Drag anywhere in the header strip (top 42 px). Header: 18 px eye icon, "KeepAwake" 13 px semibold, status pill (ACTIVE) when active, minimize and close glyph buttons 32x28.
- Content padding 18 px. Vertical layout: header 28 - gap 14 - ring area 196 - gap 14 - uptime card 56 - gap 14 - toggle row 24 - flex - action row 46 - gap 10 - footer 16.
- Ring: 188 px diameter, 10 px stroke, track colour; progress arc from 12 o'clock clockwise with a linear gradient accent -> cyan, round caps; glow when active.
  Centre text: label 11 px uppercase letter-spaced; value 38 px bold ("1h 23m", units 16 px muted); sub line 12 px.
  Inactive: value "Inactive" 26 px, sub "Windows may sleep as usual". Forever: ring fully lit, sub "until deactivated".
- Uptime card: rounded 12 px, 1 px border; "SYSTEM UP" 11 px uppercase, value 16 px semibold "3 d 4 h 12 min"; right: "since Thu 12:20" (inactive) or "screen stays on" (active).
- Toggle row: "Simulate activity (Teams, Slack)" 13 px, switch 40x22.
- Action row inactive: split pill button "Keep awake for {duration}" gradient + chevron part 46 px.
  Action row active: split button "Extend 1 h" + chevron (menu: 1 h, 2 h, 4 h, Forever) and "Deactivate" danger-outline button.
- Footer 11 px muted: "v{version} - by Karthik" left, "GitHub" link right.
- Corners 16 px (Win11 via DWM, Win10 via region). Shadow on Win11.

## Colours
Dark: bg #0F1115 with radial top glow #182238, card rgba(255,255,255,.04), border #262B37, text #F2F4F8, muted #8B93A7, faint #5F6780,
      track #222837, menu #171A21, menu border #2E3442, success #34D399, danger #F87171.
Light: bg #F7F8FC with glow #E8EEFB, card #FFFFFF, border #E2E5EE, text #111827, muted #6B7280, faint #9CA3AF, track #E3E7F0,
      menu #FFFFFF, menu border #D5D9E4, success #047857, danger #B91C1C.
Accent: Windows accent colour (HKCU\Software\Microsoft\Windows\DWM\AccentColor, ABGR) else #4F8CFF; gradient end = accent shifted toward cyan #22D3EE.
Theme: System (default) follows HKCU\...\Themes\Personalize\AppsUseLightTheme; Light / Dark override from tray menu; applies live.
Font: "Segoe UI Variable Display" if installed else "Segoe UI".

## Behaviour
- Durations: 1 h, 2 h, 4 h, 8 h (default), 24 h, Forever. Chosen duration is remembered (settings) and shown in the button label.
- Activate: SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED) on a dedicated thread; endUtc = now + duration (null = Forever).
- Extend(span): endUtc += span; Forever stays Forever. Extend via main button, main chevron menu, or tray menu.
- Deactivate: SetThreadExecutionState(ES_CONTINUOUS); state Inactive; ring resets.
- Expiry: controller ticks every 1 s; when now >= endUtc -> Deactivate + toast "KeepAwake finished" / "Kept awake for 8 h".
- Activity simulation: when toggle on and state Active, every 60 s SendInput MOUSEEVENTF_MOVE (+1,0) then (-1,0). Off by default.
- Uptime: GetTickCount64 -> "{d} d {h} h {m} min" (omit zero leading units), boot time = now - uptime, shown as "since {ddd HH:mm}" (or date if > 6 days).
- Window text refresh 1 s while visible; tray tooltip refresh 60 s and on every state change.
- Minimize and Close hide the window (no taskbar button while hidden). Left-click tray icon or "Show KeepAwake" restores it. Second instance signals the first to show and exits.
- Quit: Deactivate, dispose tray icon, exit.
- Tray tooltip (< 64 chars): "KeepAwake - Inactive" | "KeepAwake - Active - 2 h 37 min left" | "KeepAwake - Active - forever".
- Tray menu: Show KeepAwake (bold) | --- | Extend > (1 h, 2 h, 4 h, Forever; disabled when inactive) | Deactivate (disabled when inactive) | Theme > (System, Light, Dark; radio) | --- | Quit.
- Splash: 360 x 240, same look, 2000 ms total with 150 ms fade in/out, click to skip; main window is created meanwhile and shown when the splash closes.

## Settings file
keepawake.json beside the exe (fallback %LOCALAPPDATA%\KeepAwake\): {"lastDurationMinutes":480,"simulateActivity":false,"theme":"System"}
Missing or invalid file -> defaults; written on every change.

## Non-functional
Single exe < 1 MB, no files written except settings. Warm start to splash < 200 ms. Idle private bytes < 30 MB (working set about 40 MB including shared framework pages). No admin rights. No network.
