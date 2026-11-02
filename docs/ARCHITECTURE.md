# KeepAwake - architecture (v0.2, for sign-off)

## Goal
Portable single-file Windows app (Win10 22H2 / Win11) that keeps the PC and screen awake for a chosen
period, lives in the system tray, and optionally simulates user activity so Teams/Slack stay "Available".
Hard targets: exe < 1 MB, cold start < 200 ms, working set < 30 MB idle, zero installation, zero runtime download.

## Stack decision
C# 7.3, **.NET Framework 4.8**, WinForms host with **fully custom GDI+ painting** (no stock controls on screen).
- 4.8 ships inside Windows 10 1903+ and all Windows 11, so one exe runs anywhere with no install.
- WinForms + GDI+ starts in ~100 ms and idles around 18-25 MB. WPF would look similar but costs ~500 ms start
  and ~50 MB; Tauri/WebView2 ~80 MB; Rust/egui ~8 MB exe with GPU memory; C++/Direct2D is tiniest but 3x the effort.
- Modern look comes from owner drawing: anti-aliased rounded panels, gradient pill buttons, progress ring
  (DrawArc), double-buffered form, timer animations (~60 fps, only while the window is visible).
  Win11: DWM rounded corners (DwmSetWindowAttribute 33) + DWM shadow via DwmExtendFrameIntoClientArea.
  Win10: window region fallback (square-ish corners, no shadow) - cosmetic only.
- Build: .NET 8 SDK on the dev box with Microsoft.NETFramework.ReferenceAssemblies; output is one KeepAwake.exe
  with the icon and both tray icons embedded. No NuGet runtime dependencies.

## Components
```
KeepAwake/
  src/KeepAwake/            (net48, WinExe)
    Program.cs              single instance (Mutex + named Event to raise the window), splash, run
    Core/AwakeController.cs state machine Inactive | Active(endUtc | Forever); Activate(span), Extend(span), Deactivate()
                            SetThreadExecutionState(ES_CONTINUOUS|ES_SYSTEM_REQUIRED|ES_DISPLAY_REQUIRED) once on a dedicated
                            background thread (independent of the UI thread, same approach as PowerToys Awake); cleared with
                            ES_CONTINUOUS on deactivate/expiry/quit; the OS clears it anyway if the process dies
    Core/ActivitySimulator.cs every 60 s SendInput: MOUSEEVENTF_MOVE +1 px then -1 px (0-delta moves are filtered on some builds)
    Core/Uptime.cs          GetTickCount64 -> "3 d 4 h 12 min" + boot time
    Core/Settings.cs        keepawake.json beside the exe (lastDuration, simulateActivity, theme); DataContractJsonSerializer; missing file = defaults;
                            if the exe folder is read-only, falls back to %LOCALAPPDATA%\KeepAwake\keepawake.json
    Core/IClock.cs          injectable clock so the controller is unit-testable
    Native/*.cs             P/Invoke: kernel32 (SetThreadExecutionState, GetTickCount64), user32 (SendInput, GetLastInputInfo), dwmapi
    UI/Theme.cs             Dark/Light palettes; follows HKCU ...\Themes\Personalize AppsUseLightTheme + accent from HKCU\...\DWM\AccentColor;
                            live on WM_SETTINGCHANGE; override System/Light/Dark; font Segoe UI Variable with Segoe UI fallback on Win10
    UI/MainForm.cs          360x480 frameless, drag by header, min/close -> tray; owner-drawn: header, ring, uptime card, toggle, split button
    UI/Controls/*.cs        RingGauge, PillButton, SplitButton (custom popup), ToggleSwitch, FlatIconButton
    UI/SplashForm.cs        360x240, 2 s, fade in/out, click to skip
    UI/TrayIcon.cs          NotifyIcon; icon by state; tooltip "KeepAwake - Active - 2 h 37 min left" (<64 chars); menu: Show, Extend >, Deactivate, Theme >, Quit
    UI/Toast.cs             NotifyIcon.ShowBalloonTip (renders as a Windows toast on 10/11, no extra deps) on expiry
    Resources/              app.ico (256/48/32/16), tray-active.ico, tray-inactive.ico
  tests/KeepAwake.Tests/    MSTest on net48: controller timing (fake clock), extend/forever/expiry, settings round trip, uptime formatting, tooltip length
  tools/                    ollama_ask.py (LLM helper + usage ledger), make_icons.py (SVG -> ICO)
  .github/workflows/build.yml  windows-latest: build, test, on tag v* attach KeepAwake.exe to a GitHub Release
```

## Behaviour rules
- Durations 1/2/4/8/24 h/Forever; default 8 h; last choice remembered. Extend adds to remaining time (Forever stays Forever).
- Activate always keeps display on. "Simulate activity" toggle persists; runs only while Active.
- Minimize and X hide to tray. Quit (tray only) deactivates, clears execution state, exits. Second launch raises the existing window.
- Expiry: deactivate, tray icon turns grey, toast "KeepAwake finished - 8 h kept awake".
- Tray tooltip refreshes every 60 s; window text every 1 s while visible only.
- No start-with-Windows, no installer, no registry writes.

## Risks / open points
- SetThreadExecutionState is per-thread and needs a live message loop: asserted from the UI thread timer. Verified by `powercfg /requests` (admin) in tests.
- Corporate policy may block SendInput or treat it as automation; feature stays opt-in and off by default.
- Balloon tips are suppressed if the user disabled notifications for the app or Focus Assist is on.
- Unsigned exe triggers SmartScreen once; documented in README.
- Start target is a warm start (second launch) to the splash in < 200 ms; first-run Defender scan is excluded. The 2 s splash is a
  requirement; the main window is built behind it so it appears instantly when the splash ends.
- Balloon tips work without an AppUserModelID but cannot be re-enabled per app if the user mutes them; accepted for a portable app.

## Review log
- 2026-11-02 glm-5.3:cloud review (8 points): adopted +/-1 px activity nudge, dedicated thread for execution state, AccentColor
  registry, Segoe UI fallback, settings fallback folder, DWM shadow via ExtendFrame, start-time definition; kept the splash and
  balloon-tip toasts as accepted trade-offs.
