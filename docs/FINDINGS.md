# KeepAwake - analysis and findings (1.0.0 to 1.2.0)

Written 2026-11-03 after three releases in one day. This is the record of what was decided, what was measured, what the
reviews found, and what is still unverified. The roadmap is in [ROADMAP.md](ROADMAP.md).

## 1. What was built

A portable single-file Windows app that keeps the PC and screen awake for a chosen period, lives in the tray, and
optionally simulates user activity. Repository: https://github.com/karthikbhaskar-1/KeepAwake (public, MIT).

| Release | Date | Scope |
|---|---|---|
| 1.0.0 | 2026-11-02 | Splash, main window with progress ring, tray icon with tooltip and menu, durations 1/2/4/8/24 h and Forever, extend, deactivate, toast on expiry, light/dark theme following Windows, settings beside the exe, single instance, GitHub Actions release |
| 1.1.0 | 2026-11-02 | Full review by Opus, fixes by Sonnet: startup ordering, event-driven timer, cached painting, shutdown on sign-out, DPI, hand-written JSON, CI hardening; tests 43 to 98 |
| 1.2.0 | 2026-11-02 | "Until a time", 5-minute expiry warning with click-to-extend, stop on battery, stop when locked, global hotkey, tray middle-click; DST-safe end times; tests to 145 |

## 2. Key decisions and why

| Decision | Alternatives considered | Reason |
|---|---|---|
| C# on .NET Framework 4.8, WinForms host, every pixel painted with GDI+ | WPF (500 ms start, 50 MB), .NET 8 self-contained (60 MB exe), Tauri/WebView2 (80 MB), Rust/egui (8 MB, GPU memory), C++/Direct2D (smallest, 3x effort) | Only option that is tiny, instant, zero-install on Windows 10/11 and still modern-looking. The look comes from owner drawing, not the framework |
| Execution state held from a dedicated thread | Re-asserting from the UI thread every second | The hold must not depend on the UI thread; Windows clears it if the process dies |
| Event-driven core timer (Core/Schedule.NextWakeMs) | A 1 s tick | Nothing ticks while nothing is pending, which is what gives 0.000% CPU in the tray |
| Hand-written flat JSON reader/writer | DataContractJsonSerializer | Avoids loading a serializer assembly at start; atomic save via temp file and replace |
| Mouse nudge of +1/-1 px | 0-delta move, F15 key | 0-delta moves are filtered on some builds; the 1 px pair is invisible and resets idle timers |
| Settings beside the exe with LocalAppData fallback | Registry, AppData only | Portable first; read-only media still works |
| Balloon tips for toasts | Registered toast notifications | Registration needs a Start Menu shortcut, which breaks the no-install rule |
| Unsigned exe | Code signing | Signing needs a certificate the owner must buy; deferred to the roadmap |

## 3. Measurements

All on a Windows 11 laptop at 100% DPI, release build, measured with a scripted driver (clicks by window-relative
coordinates, screenshots, process counters sampled over 30 to 90 s). "Hidden" means minimized to the tray.

| Metric | 1.0.0 | 1.1.0 | 1.2.0 |
|---|---|---|---|
| Exe size | 124 KB | 139 KB | 155 KB |
| Launch to splash on screen (warm) | not measurable | 238 ms | about 240 ms |
| CPU, window visible and active | 0.76% of a core | 0.00% | 0.05% (one 15 ms tick) |
| CPU, hidden in tray, active | 0.59% | 0.000% | 0.000% |
| CPU, hidden in tray, inactive | not measured | 0.039% | 0.035% (one tick) |
| Private bytes over 90 s | 24.9 to 27.0 MB, rising | 25.0 MB flat | 25.3 MB flat |
| Working set | about 40 MB | about 40 MB | about 41 MB |
| GDI objects | 40 | 40 | 41 |
| Unit tests | 43 | 98 | 145 |

The 1.0.0 CPU figures included a timer that ticked every second and a background gradient redrawn on every paint.
Both are gone. The working set is mostly shared .NET Framework pages; private bytes are the number that matters.

## 4. What the reviews found

Three review passes were run: a cloud model (glm-5.3, kimi-k2.7-code) during 1.0 development, Opus for 1.1, Opus for 1.2.
Full texts: [REVIEW_OPUS.md](REVIEW_OPUS.md) and [REVIEW_OPUS_1.2.md](REVIEW_OPUS_1.2.md).

### Found and fixed before 1.0.0 (by testing and the cloud review)
- A cached font was disposed after the first paint, crashing the window ("Parameter is not valid").
- The split button fired Click twice because WinForms raises Click after MouseUp.
- The ring showed "0" instead of "00" because measured text was tighter than drawn text.
- The expiry timer stopped while the window was hidden, so a tray-only session never expired.
- Zero-delta mouse moves may not reset idle timers; changed to a 1 px pair.

### 1.1.0 (Opus review: 5 High, 15 Medium, 24 Low; 23 spec deviations; 16 test gaps)
Top items, all fixed:
- Startup did all heavy work before the splash and built the main window only after it closed; a second launch during the splash broke "show window" for the session.
- The core timer ran every second forever, even hidden and inactive.
- Closing by sign-out or End task raised events into a disposed tray icon.
- The chevron check assumed MouseUp runs before Click.
- The background gradient and brushes were recreated on every paint and every hover.
- A partial settings file silently turned the default into Forever.
- Theme switch left the window menus unreadable.
Accepted trade-offs: 2 s splash kept (a requirement); balloon tips kept instead of registered toasts.

### 1.2.0 (Opus review: 0 Critical, 0 High, 2 Medium, 13 Low)
- Medium: "until a time" was an hour off across a daylight-saving change. Fixed by resolving in UTC with explicit handling of the skipped spring hour.
- Medium: clicking a stale warning balloon could extend a newer session. Fixed with a period id checked on click.
- Low items fixed: click-away closes the popup, double middle-click no longer opens the window, parser refuses Shift-only and Alt+F4 hotkeys, battery re-checked after resume, no repeated hotkey-in-use balloon, Options checkmarks correct at startup, footer clipping, JSON escaping of lastUntil, power status not read when the option is off, doc corrections.
- Deliberately not done: an until-time does not follow a time-zone change made after starting; the end is fixed when you start.

## 5. How it was verified

- Unit tests (MSTest, net48): controller timing with a fake clock, extend and forever rules, expiry and warning scheduling, settings round trips including 1.1-era files, DST cases with explicit time zones, hotkey parser, battery rule, and a live check that Windows reports both ES_SYSTEM_REQUIRED and ES_DISPLAY_REQUIRED while the hold is active.
- Scripted UI runs on the release exe: duration menu, chevron versus body click, activate, extend menu, deactivate, close to tray, single-instance relaunch, light theme from settings, until-time popup (Enter, invalid input, click-away), hotkey toggle while hidden, warning balloon at the scheduled instant.
- Performance script sampling CPU time, private bytes, working set, GDI and USER objects, handles and threads.
- CI: build, tests, 1 MB size gate, tag-matches-version check, SHA256 beside the exe, release job on tags.

### Lessons about the verification itself
- Scripted clicks fail silently when the session is locked or another window covers the app. Pin the window topmost and drive menus by keyboard where possible.
- When a menu is open, the process's main window handle can resolve to the menu; compute click coordinates from the main window rect captured beforehand.
- powercfg /requests needs an elevated prompt, so the execution-state check was moved into a unit test that reads the state Windows returns on release.

## 6. Still unverified (manual checks recommended)

- Tray right-click menu entries, including the Options submenu, since the tray icon position cannot be scripted reliably.
- Stop on battery (unplug while active), stop when locked (Win+L with the option on), and the hotkey-in-use balloon.
- Clicking the warning balloon to extend, and the expiry toast at the end of a real session.
- Sign-out and shutdown while active (the code path is covered, the OS behaviour is not).
- Multi-monitor and mixed-DPI layouts; only 100% DPI on one monitor was tested.
- Windows 10 (region-based corners, no DWM shadow).

## 7. Model usage

| Stage | Model | Tokens (in / out) | Notes |
|---|---|---|---|
| Architecture risk review | glm-5.3 cloud | 1.3k / 12.4k | 8 points, 6 adopted |
| Unit tests for core | glm-5.3 cloud | 3.6k / 30.0k | 41 tests, all passed first run |
| README draft | glm-5.3-flash cloud | 1.5k / 1.9k | cleaned by hand |
| Code review of UI | kimi-k2.7-code cloud | 16.4k / 40.7k | 10 points, 5 adopted |
| 1.1 review | Claude Opus | 217k total | 44 findings plus spec and test gaps |
| 1.1 fixes | Claude Sonnet | 288k total | tests 43 to 98 |
| 1.2 implementation | Claude Sonnet | 187k total | 29 new tests |
| 1.2 review | Claude Opus | 169k total | 15 findings |
| 1.2 fixes | Claude Sonnet | 125k total | 18 new tests |
| Planning, mockups, all 1.0 application code, verification, releases | Claude (main session) | not itemised | |

Cloud model output tokens are dominated by reasoning text; the usable output was a fraction of the count.

## 8. Open risks

- Unsigned exe: SmartScreen warns on first run; some corporate policies block it outright.
- SendInput can be refused by policy or treated as automation by endpoint security; the feature is opt-in and off by default.
- Balloon tips are suppressed by Focus Assist or if the user mutes the app; there is no per-app re-enable without a shortcut.
- The hotkey is read once at start; editing the settings file needs a restart.
