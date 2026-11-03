# KeepAwake 1.2 - independent code review (Opus)

Scope: the uncommitted 1.2 working tree on top of v1.1.0 (`git diff` plus the new files `Core/UntilTime.cs`, `Core/Hotkey.cs`,
`UI/UntilPopup.cs`, `tests/KeepAwake.Tests/Spec12Tests.cs`), read in full together with `MainForm.cs`, `TrayIcon.cs`,
`AwakeController.cs`, `Schedule.cs`, `Settings.cs`, `Program.cs` and `NativeMethods.cs`, checked against `docs/SPEC_1.2.md`,
`docs/DESIGN_SPEC.md`, `docs/ARCHITECTURE.md` and the timer design in `docs/REVIEW_OPUS.md` section A. Static review only; no
source file was changed.

Severity: Critical, High, Medium, Low. "Unconfirmed" means the conclusion depends on OS or framework behaviour I could not run
here; each such item says how to confirm it.

Counts: Critical 0, High 0, Medium 2, Low 13 (findings 1-15). Spec deviations are judged in section B, the verified-OK list is
section C.

---

## A. Findings

### 1. Medium - "Until a time" is computed in naive local time, so it is off by one hour across a DST change
`MainForm.StartUntil`, `UntilTime.Resolve`, and (display) `MainForm.SubText`.

`StartUntil` takes `DateTime.Now`, resolves the target as a local wall-clock `DateTime`, and passes `target - now` to the
controller. Subtracting two local `DateTime`s ignores the UTC offset change, so a session that spans a DST transition is one
hour wrong:
- Fall back (for example EU, 25 Oct 2026, 03:00 CEST -> 02:00 CET): at 23:00 the user picks "07:00". Naive span 8 h, real span
  9 h. KeepAwake stops at 06:00 local, one hour early. This is the night-shift and overnight-download case the feature exists for.
- Spring forward (28 Mar 2027): "07:00" picked at 23:00 runs until 08:00 local, one hour late.

The sub-line hides the bug: `SubText` shows `DateTime.Now + rem`, which is the same naive arithmetic, so the window says
"ends 07:00" while the real end is 06:00.

Reproduce: in a unit test, put `nowUtc = 2026-10-24 21:00Z`, `tz = W. Europe Standard Time`, `"07:00"` through the current
code. The span is 8 h; the correct span is 9 h (end `2026-10-25 06:00Z`).

Fix: resolve in UTC with an injectable time zone, and handle the non-existent hour in the spring-forward gap. Without that
handling, `ConvertTimeToUtc` throws `ArgumentException` for a time inside the gap.
```csharp
// UntilTime
public static DateTime ResolveUtc(DateTime nowUtc, TimeSpan timeOfDay, TimeZoneInfo tz)
{
    var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
    for (int day = 0; day < 3; day++)
    {
        var local = DateTime.SpecifyKind(localNow.Date.AddDays(day) + timeOfDay, DateTimeKind.Unspecified);
        while (tz.IsInvalidTime(local)) local = local.AddMinutes(15);   // inside the spring-forward gap: first valid instant after it
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);              // ambiguous fall-back hour resolves to standard time
        if (utc > nowUtc) return utc;
    }
    throw new InvalidOperationException("unreachable");
}

// MainForm.StartUntil
var nowUtc = DateTime.UtcNow;
Begin(UntilTime.ResolveUtc(nowUtc, timeOfDay, TimeZoneInfo.Local) - nowUtc);

// MainForm.SubText
... + TimeFormat.Clock((DateTime.UtcNow + rem).ToLocalTime());
```
Keep `Resolve` for its tests or re-express them through `ResolveUtc` with `TimeZoneInfo.Utc`. Add the two DST tests from
finding 15.

### 2. Medium - The warning balloon's click action is not tied to the period that raised it (partly Unconfirmed)
`TrayIcon._balloonAction` / `TrayIcon` constructor (`BalloonTipClicked`), `MainForm.OnControllerWarning`, `MainForm.ExtendFromWarning`.

`Toast` stores `onClick` in one field. The field is cleared only when the balloon is clicked or when another `Toast` call
replaces it. `ExtendFromWarning` only checks `IsActive && !IsForever`, not that the session is still the one that was warned
about. Stale clicks therefore extend an unrelated session:
- A warning is shown, the user deactivates (hotkey, middle-click or button: none of these shows a toast, so the action
  survives), starts a new 8 h session, and then clicks the still-visible balloon. The new session is extended by 1 h.
- Unconfirmed: on Windows 10/11 a legacy balloon is kept in the notification centre after its 6 s timeout. If clicking it there
  delivers `NIN_BALLOONUSERCLICK` (I believe it does for `NOTIFYICON_VERSION` 0 icons), a warning can be clicked hours later and
  extend whatever session is running then.
- The same applies after an Extend: the warning re-arms, the old balloon is still clickable, and a click adds yet another hour.

Clearing the action on `BalloonTipClosed` is not a safe fix. When a new balloon replaces an old one, the old balloon's
`NIN_BALLOONTIMEOUT`/`HIDE` notification can arrive after `Toast` has stored the new action, and it would wipe the new action.

Fix: stamp each period and check the stamp on click.
```csharp
// AwakeController
public int PeriodId { get; private set; }           // ++ in Activate, in Extend (both branches) and in Deactivate

// MainForm
private void OnControllerWarning(object s, EventArgs e)
{
    int period = _controller.PeriodId;
    _tray.Toast("KeepAwake ends in 5 minutes", "Click to extend by 1 hour.", () =>
    {
        if (_controller.IsActive && !_controller.IsForever && _controller.PeriodId == period) Extend(Durations.Extend[0]);
    });
}
```
`PeriodId` is unit-testable with the fake clock (finding 15).

To confirm the notification-centre part: show the warning, let it time out, open the notification centre, click the entry,
and check whether `BalloonTipClicked` fires (Trace output or a breakpoint).

### 3. Low - Clicking the main window does not cancel the until-time popup, contrary to ARCHITECTURE.md
`UntilPopup.Prompt` (`ShowDialog(owner)`), `UntilPopup.OnDeactivate`, ARCHITECTURE "click-away cancels".

`ShowDialog` disables the owner. A click on a disabled owner does not move activation: Windows beeps and flashes the popup, so
`OnDeactivate` never runs. The most likely click-away target, the KeepAwake window around the popup, therefore does nothing.
Click-away only works on other apps, the desktop or the taskbar. Esc, Cancel and Enter work as coded (`ProcessCmdKey` runs
before the TextBox for both keys, and returning true suppresses the WM_CHAR beep).

A side effect of the modal loop: a hotkey press while the popup is open still reaches `MainForm.WndProc` and activates. A later
OK then calls `Activate` again and resets the elapsed time. This is harmless but surprising.

Fix (pick one):
- Make the popup modeless: `f.Show(owner)`, return the result through a callback from `FormClosed`, and have `Close(result)`
  call `base.Close()` (on a modeless form, setting `DialogResult` does not close it). Then a click on the owner activates the
  owner, `OnDeactivate` cancels, and the hotkey case goes away if `ToggleActive` returns while a popup is open.
- Or keep it modal and, in `OnShown`, call `EnableWindow(Owner.Handle, true)`. Also make `MainForm` answer `WM_MOUSEACTIVATE`
  with `MA_ACTIVATEANDEAT` while the popup is open, so the click that cancels does not also press a button underneath.

Otherwise, drop "click-away cancels" from ARCHITECTURE.md.

### 4. Low - Middle double-click on the tray icon also shows the window
`TrayIcon` constructor, `_icon.DoubleClick`.

For `WM_MBUTTONDBLCLK`, `NotifyIcon.WndProc` calls `WmMouseDown(..., clicks: 2)`, which raises `DoubleClick` regardless of the
button. So a quick double middle-click toggles once (the first click) and then raises `ShowRequested`. Right double-click does
the same (pre-existing).

Fix:
```csharp
_icon.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowRequested?.Invoke(this, EventArgs.Empty); };
// remove the _icon.DoubleClick subscription
```

### 5. Low - The battery rule is not re-checked after resume (Unconfirmed)
`MainForm.WndProc`, `WM_POWERBROADCAST` branch.

On `PBT_APMRESUMEAUTOMATIC`/`PBT_APMRESUMESUSPEND` only `CoreTick` runs. If the charger is unplugged while the PC is asleep
(lid closed, then unplugged and carried away), the code relies on Windows sending a separate `PBT_APMPOWERSTATUSCHANGE` after
resume. That message is usually sent but is not documented as guaranteed. Without it, the session keeps the laptop awake on
battery despite the option.

Fix: `if (resume) { CoreTick(); CheckBattery(); }`. The cost is one `GetSystemPowerStatus` per resume.
To confirm: on a laptop with the option on and a session active, sleep, unplug, wake, and check the tray icon.

### 6. Low - The hotkey parser accepts Shift-only combinations and system shortcuts
`Hotkey.TryParse`.

`"Shift+A"`, `"Shift+1"` and `"Alt+F4"` parse as valid. `RegisterHotKey` succeeds for them and they are then swallowed
system-wide: typing a capital A, typing "!", or closing any window with Alt+F4 toggles KeepAwake instead. The value only comes
from a hand-edited file, but a typo has a large blast radius.

Fix: after the loop, `if ((mods & ~ModShift) == 0) return false;`. Optionally also reject `Alt+F4`, `Alt+Tab`, `Alt+Space`
and `Alt+Esc` (key with `mods == ModAlt`). Add test cases to `Parse_NoKeyOrNoModifier_IsInvalid`.

### 7. Low - The hotkey failure balloon repeats on every handle creation, and the first one lands on the splash
`MainForm.RegisterHotkey` (called from `OnHandleCreated`).

The spec asks for "one balloon". `OnHandleCreated` runs again on any `RecreateHandle`, and each run toasts again for "in use"
or "not valid". The first run happens in `Prewarm()`, behind the 2 s splash, and that balloon also replaces any earlier one.

Fix: `private bool _hotkeyWarned;` and toast only `if (!_hotkeyWarned) { _hotkeyWarned = true; ... }`. Optionally move the
first registration attempt from `OnHandleCreated` to `PrepareMenus` (after the splash) and keep `OnHandleCreated` for
re-registration only.

### 8. Low - Tray option check marks and the lock watch are wrong or absent for the first ~400 ms
`TrayIcon.EnsureMenu`/`ApplyOptionChecks`, `MainForm.PrepareMenus`, `MainForm.SyncLockWatch`.

`SetOptions` is first called from `PrepareMenus`, which `DeferMenuBuild` runs 400 ms after start. If the user right-clicks the
tray icon before then, `EnsureMenu` builds the Options submenu from the default `false` fields, so "Simulate activity" shows
unchecked although it is on. The lock subscription also starts only then. This has no real impact on the lock (no session can
be active yet), but the state is split across two places.

Fix: call `UpdateTrayOptions()` (it only stores three bools until the menu exists) at the end of the `MainForm` constructor.
Leave `SyncLockWatch()` in `PrepareMenus` so the SystemEvents cost stays off the start-up path.

### 9. Low - An until-time does not follow wall-clock or time-zone changes made after activation
`MainForm.StartUntil` + `SystemClock` (monotonic by design).

After activation, the end is a monotonic instant. If the user changes time zone (travel), or the clock is corrected after a long
wrong-clock period, KeepAwake still stops at the original instant. Meanwhile `SubText` recomputes "ends HH:mm" from the new
`DateTime.Now`, and the button label (when inactive) still says "until 17:30". This is consistent with the 1.1 rule "clock
changes do not move the expiry", but for an until-time that rule is arguably wrong.

Fix: decide and document. Either (a) keep it and add a sentence to README/ARCHITECTURE ("an until-time is converted to a
duration when you start"), or (b) on `WM_TIMECHANGE` and on `SystemEvents.TimeChanged` (time-zone change), if the current
period came from an until-time and has not been extended, re-resolve with `ResolveUtc` and adjust the end through a new
controller method `SetEnd(DateTime endUtc)`. That method must also re-arm the warning.

### 10. Low (performance) - CheckBattery queries power status on every power-status message even when the option is off
`MainForm.CheckBattery`.

`PowerRules.ShouldStopOnBattery(_controller.IsActive, _settings.StopOnBattery, OnBattery())` evaluates `OnBattery()` eagerly:
one `GetSystemPowerStatus` call plus a `PowerStatus` allocation per `PBT_APMPOWERSTATUSCHANGE`. On many laptops that message
arrives on every 1 % battery change. The cost is microseconds, and the thread is woken by the message anyway, but the work is
pointless.

Fix: `if (_shutDown || !_controller.IsActive || !_settings.StopOnBattery) return; if (!OnBattery()) return; ...`.
The same short-circuit applies in `Begin`.

### 11. Low - A long hotkey text is clipped in the footer
`MainForm.RenderStatic`, footer.

The footer text is drawn into a fixed `L(240)` wide box. "v1.2.0  ·  by Karthik  ·  Ctrl+Alt+Shift+Win+F12" is about 260 px
at 96 DPI in 8 pt Segoe UI, so it is trimmed. Widening the box would collide with the GitHub link. The default "Ctrl+Alt+K"
fits.

Fix: measure the text and, if it does not fit in `linkRect.Left - L(Pad) - L(8)`, drop "by Karthik" first. Alternatively, show
the hint only as "Hotkey" plus a tooltip.

### 12. Low - `Settings.ToJson` writes `LastUntil` unescaped
`Settings.ToJson`.

`Hotkey` goes through `Escape`, but `LastUntil` is concatenated raw. Today this is safe only because `MainForm` always assigns
`UntilTime.Format(...)` and `Parse` normalises the value. The property is public, so any other caller that assigns a string
containing `"` produces an invalid file, which then loads as all defaults.

Fix: `",\"lastUntil\":\"" + Escape(LastUntil) + "\""`, or normalise in the setter.

### 13. Low (docs) - The SystemEvents threading notes are inaccurate (Unconfirmed in detail)
`MainForm.OnSessionSwitch` summary comment; ARCHITECTURE ("it starts the SystemEvents thread").

`SessionSwitch` is subscribed on the UI thread (from the `DeferMenuBuild` timer, or from a tray click). In .NET Framework,
`SystemEvents` records `AsyncOperationManager.SynchronizationContext` per handler, which is the WinForms context here, and
delivers the event through `Send`. So `OnSessionSwitch` already runs on the UI thread. In addition, when `SystemEvents` is first
initialised from an STA thread, it creates its hidden window on that thread rather than on a new one. The `BeginInvoke` hop is
harmless, and the "subscribe only while on" choice is still right, because it avoids `WTSRegisterSessionNotification`. But the
"non-UI thread" and "starts a thread" statements are wrong.

To confirm: check `Thread.CurrentThread.ManagedThreadId` inside `OnSessionSwitch`, and count the threads in Process Explorer
before and after turning the option on.

Fix: reword the comment ("may run on another thread if subscribed off the UI thread; hop anyway") and the ARCHITECTURE sentence
("subscribed only while the option is on, so no session-notification registration exists otherwise").

### 14. Low (docs) - ARCHITECTURE.md has stale lines for 1.2
- The `tests/` line still lists only the 1.1 areas. Add: until-time parsing and resolution, warning scheduling and re-arm,
  hotkey parser, battery rule, 1.2 settings keys.
- The `Native/*.cs` line lists the user32 imports without `RegisterHotKey`/`UnregisterHotKey`.
- The behaviour line "while timed it wakes at most once a minute (tooltip) plus once at expiry" should add "plus once at the
  5-minute warning". The `Schedule.cs` line already says so, so the two lines contradict each other.
- "click-away cancels" (UntilPopup): see finding 3.

README is accurate for 1.2. One addition worth making: state that the hotkey hint is omitted when registration fails. README
already says "while it is registered", so this is optional.

### 15. Low - Test gaps for unit-testable logic
`tests/KeepAwake.Tests/Spec12Tests.cs`. Add:
- DST resolution (after finding 1): `W. Europe Standard Time`. Fall back: `ResolveUtc(2026-10-24 21:00Z, 07:00)` ==
  `2026-10-25 06:00Z`. Spring forward: `ResolveUtc(2027-03-27 22:00Z, 07:00)` == `2027-03-28 05:00Z`. Gap time:
  `ResolveUtc(2027-03-28 00:30Z, 02:30)` returns 03:00 local (`01:00Z`) and does not throw.
- Midnight: `ResolveUtc(23:59:30 local, 00:00)` is 30 s ahead.
- `Extend(null)` after the warning was armed: `WarningUtc` is null, and a `Tick` past the old instant does not warn.
- A period under 10 min extended by 1 h arms a warning at the new end minus 5 min.
- `Schedule.NextWakeMs` with `warnUtc` after `endUtc` (end wins), and with `warnUtc` past while `endUtc` is in the future
  (returns `MinWakeMs` once; after `Tick`, `WarningUtc` is null, so the next schedule is no longer 50 ms). This pins down the
  "no wake storm" rule.
- `PeriodId` (finding 2): changes on Activate, Extend and Deactivate. A captured id no longer matches after
  Deactivate + Activate.
- Hotkey: `Parse(ToString())` round trip for every modifier combination, including `Win`; `"Shift+A"` invalid (finding 6);
  `"Ctrl+Alt+k"` canonicalises to `"Ctrl+Alt+K"`.
- Settings: a hotkey containing `"` and `\` round-trips; `{"lastUntil":"17:30","lastDurationMinutes":-1}` (keys in reverse
  order) loads as until-mode; `{"lastDurationMinutes":-1,"lastUntil":""}` falls back to 480.

---

## B. Spec compliance and the implementer's deviations

Every SPEC_1.2 rule is implemented, except where findings 1, 3 and 7 qualify it (DST correctness of "until that local time",
click-away, "one balloon"). Items that differ from the spec's literal text:

| Spec item | As implemented | Verdict |
|---|---|---|
| F5 footer "v1.2.0 - by Karthik - Ctrl+Alt+K" | uses the existing " · " separator | Acceptable (matches the 1.1 footer) |
| F5 footer "omit when disabled" | also omitted when registration failed | Acceptable, better |
| F5 hotkey text | extra balloon for an invalid hotkey string | Acceptable (one balloon; see finding 7) |
| F5 changes to `hotkey` | read at start-up only; README says "at the next start" | Acceptable, documented |
| F3 activation-time refusal | balloon says "stopped" although nothing started | Spec-mandated wording; "KeepAwake not started: running on battery" would be clearer |
| F3 toggling the option on while on battery and active | stops immediately (`CheckBattery` after toggle) | Not in spec; reasonable |
| F1 menu check mark | "Until a time..." shows checked while until-mode is remembered | Not in spec; consistent with the other items |
| F1 default text | `_untilTod` (this run) before `lastUntil` before suggestion | Matches "last used time" |
| F2 "shorter than 10 minutes" | uses the total period (`EndUtc - StartedUtc`, grows with Extend) | Correct reading |
| Tray menu (1.2) | order matches exactly; "Simulate activity" is now in the tray and the window | Matches spec |

---

## C. Verified OK

- `Schedule.NextWakeMs`: still returns -1 when inactive or Forever. The warning is just one more `Math.Min` candidate.
  A past instant gives one 50 ms wake, and `Tick` then sets `_warned`, so `WarningUtc` becomes null and no wake storm follows.
  `ScheduleCore` passes `_controller.UtcNow` (the monotonic clock), so the warning, the end and "now" are in the same time
  domain, and a wall-clock jump cannot cause repeated 50 ms wakes.
- `AwakeController` warning logic: fires once (`_warned` is set before the event, so a throwing handler cannot re-fire it).
  Re-armed by `Activate` and by a timed `Extend`. `Extend(null)` makes `WarningUtc` null via `EndUtc == null`. An expiry that
  is detected late (sleep) skips the warning and expires. Extend spans are at least 1 h, so a re-armed warning is never in the
  past.
- No new idle work: no new timers. The popup is created per use and disposed by `using`. The footer hint is baked into the
  static background cache and rebuilt only when it changes. `SystemEvents` is touched only while "Stop when locked" is on.
  `ToggleSwitch._anim` after a tray toggle while hidden converges on its own within about 0.2 s.
- Hotkey: `RegisterHotKey(Handle, 1, mods | MOD_NOREPEAT, vk)` in `OnHandleCreated`. `UnregisterHotKey` runs in
  `OnHandleDestroyed`, while the HWND is still valid there, and in `Shutdown`. `MOD_NOREPEAT` is valid on Windows 7 and later.
  A failure is non-fatal. `WM_HOTKEY` is matched on id 1 and ignored after shutdown. A second instance exits before creating a
  window, so it never competes for the hotkey.
- `WM_POWERBROADCAST` reaches the hidden top-level main window. `PBT_APMPOWERSTATUSCHANGE` (0x000A) is correct.
  `PowerLineStatus.Offline` is the right test (Unknown and Online do not stop).
- SessionSwitch: subscribed and unsubscribed symmetrically through `_lockWatch`, removed in `Shutdown` (which runs from Quit,
  `OnFormClosed` and `Dispose`), and the static event no longer roots the form. `BeginInvoke` is guarded and its
  `InvalidOperationException` is caught.
- Balloon click: the single `BalloonTipClicked` subscription is made once in the constructor (no accumulation). The action is
  swapped on each `Toast` and cleared after it runs. `ExtendFromWarning` does nothing when the session is inactive or Forever.
  The remaining gap is finding 2.
- Settings: `lastUntil` is parsed before `lastDurationMinutes` regardless of key order. `-1` is accepted only with a valid
  `lastUntil`. A 1.1 file loads with the new defaults. `hotkey: ""` stays disabled. Unknown keys are ignored. `ToJson` key
  order matches the spec.
- `UntilTime.TryParse` rejects every malformed case in the spec, plus seconds, single-digit minutes and 24:00. `Suggest` wraps
  past midnight. `Resolve` treats equal-to-now as tomorrow.
- `UntilPopup`: Enter and Esc are handled in `ProcessCmdKey` (no beep). Input is filtered on `KeyPress` and pasted text is
  cleaned in `TextChanged`. Invalid input shows the error hint and reselects the text. Fonts come from the shared DPI-keyed
  `Theme.Font` cache and are not disposed by the TextBox. The window region is owned by the system once `SetWindowRgn`
  succeeds. The popup is sized and placed with `Theme.Scale` and clamped to the anchor screen's working area.
- Options submenu: `CheckOnClick` is off, so the window toggle and the tray item cannot double-toggle. Each change goes through
  `UpdateTrayOptions` → `SetOptions`. The submenu inherits the themed renderer through `ApplyThemeToMenu`.
- Middle-click: `NotifyIcon.WndProc` maps `WM_MBUTTONUP` to `MouseClick` with `MouseButtons.Middle`. Whether the Windows 11
  shell forwards middle-button messages for a version-0 icon is not verified here (manual check: middle-click the tray icon).
- Version bumped to 1.2.0. The CI tag check reads `<Version>`, so tag `v1.2.0` will match.
