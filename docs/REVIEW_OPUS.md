# KeepAwake 1.0.0 - independent code review (Opus)

Scope: every file under `src/KeepAwake`, `tests/KeepAwake.Tests`, `.github/workflows/build.yml`, `README.md`, checked against
`docs/DESIGN_SPEC.md` (signed off) and `docs/ARCHITECTURE.md`. This was a static review; no source files were changed.
Items already fixed after the first review (cached font disposal, double click events, expiry timer while hidden, DPI double
scaling, tray/exit lifetime) were re-checked and are not reported again, except where a related path is still wrong.

Severity levels are Critical, High, Medium and Low. "Unconfirmed" means the conclusion depends on framework or OS behaviour I
could not run here; each such item says how to confirm it.

Sections: A. Performance (top priority) - B. Correctness, Win32, threading - C. Spec compliance - D. Test gaps -
E. CI, README, docs - F. Checked and found OK - G. Next version candidates.

Counts: Performance P1-P15 (High 2, Medium 4, Low 9). Functional 1-24 (High 3, Medium 10, Low 11). Spec table C1-C23 (Medium 2,
the rest Low, many pointing at the findings above). CI/docs E1-E5 (Medium 1, Low 4). No Critical findings.

---

## A. Performance

### Measurement protocol (use this for every expectation below)
- CPU: use Process Explorer, Threads tab, with the "CPU", "Cycles Delta" and "CSwitch Delta" columns. Or run
  `wpr -start CPU -filemode`, wait 5 min, run `wpr -stop cpu.etl` and open it in WPA (CPU Usage (Precise) by thread). Task
  Manager and `Get-Process ... TotalProcessorTime` are tick-sampled and too coarse to tell 0.01 % from 0.5 %.
- Make sure no UI Automation client is attached while measuring: close Inspect, FlaUI, the scripted harness and Narrator. A
  connected UIA client makes WinForms answer cross-process accessibility calls, and that alone can cost about 0.5 %.
- Memory and handles: run `Get-Counter '\Process(KeepAwake)\Private Bytes'` once a minute for 60 min, and watch the Task Manager
  "GDI objects" and "USER objects" columns. Ignore the first 5 minutes (JIT, first GC, lazy menu creation).
- Start-up: give the splash a distinct window title (P11). Then time from `Start-Process` until
  `FindWindow(null, "KeepAwake Splash")` returns a visible window. Warm start means a second launch after a previous run exited.

### Where the measured hidden-in-tray CPU (0.59 % of one core) comes from - Unconfirmed
I traced every code path that can run while the window is hidden:
1. `_coreTimer` (MainForm, 1000 ms) is always running. Each tick: WM_TIMER -> `CoreTick` -> `AwakeController.Tick` (two property
   reads and a `DateTime.UtcNow`) -> `Stopwatch.ElapsedMilliseconds`. Including message dispatch that is roughly 20-100 us per
   tick, about 0.002-0.01 % of a core.
2. Every 60 s `TrayIcon.Update` runs (tooltip, icon, menu item states). Negligible.
3. If "Simulate activity" is on and the state is Active, one `SendInput` per 60 s on a thread-pool thread. Negligible.
4. `_uiTimer` and `_animTimer`: `OnVisibleChanged` does fire for `Hide()` (Form.SetVisibleCore -> WM_SHOWWINDOW -> state change
   -> OnVisibleChanged), and both timers are stopped there. `OnStateChanged` only restarts `_animTimer` when `Visible` is true.
   `UiTick` is only called from `_uiTimer` and from `OnVisibleChanged(true)`. I found no path that restarts either timer while
   the window is hidden.
5. `Invalidate()` calls from `OnStateChanged` and `OnThemeChanged` while hidden only accumulate an update region. A hidden
   window gets no WM_PAINT, so they cost nothing.
6. The power-hold thread and the show-event waiter thread sit in kernel waits. `ToggleSwitch._anim` stops once it converges.

Together, paths 1-6 cannot reach 0.59 % (about 265 ms of CPU per 45 s). That is 50-100x more than they can cost, so the app's
own code does not explain the number. The most likely causes, in order:
- (a) a UIA client from the scripted run was still attached;
- (b) the 45 s window began right after start-up or right after a hide, so it caught JIT, the first gen-0 GC and finalizer work;
- (c) measurement granularity.

Action: re-measure for 5 minutes using the protocol above. If the UI thread still shows more than 1 context switch per second,
capture a WPR trace and read the stacks. Either way, apply P2 so the UI thread does not wake at all while hidden and inactive.

Expected after P2:
- hidden and inactive: 0 context switches on the UI thread over 5 min;
- hidden and active with a timed session: 1 per 60 s;
- the Process Explorer CPU column stays blank (0.00) over 5 min.

### P1. High - Everything is built before the splash paints, and the main window's HWND is not built behind it
`Program.Run`, lines 36-63. Before `splash.Show()` the app runs all of this, in order:
- the settings path probe, which creates and deletes a file on disk;
- loading `DataContractJsonSerializer` (P6);
- two registry reads;
- `TrayIcon`: a ContextMenuStrip with 10 items, two icon decodes, font family probing, creating the NotifyIcon window and a
  `Shell_NotifyIcon` round trip to Explorer;
- the `MainForm` constructor: 6 custom controls, two more ContextMenuStrips with 10 items, two icon decodes plus resampling,
  `Icon.ExtractAssociatedIcon` (a shell call) and 3 timers;
- the splash constructor: another icon decode and 3 fonts.

ToolStrip is one of the heaviest WinForms JIT paths, and all of it runs before the first splash pixel.

On top of that, the main window's handle is NOT created during the 2 s splash. Nothing touches `main.Handle`, and
`Invalidate()` and `SetBounds` don't create it. So when the splash closes, `main.Show()` pays for handle creation, the first
`OnPaint` JIT and font realisation while the user is watching. That is the opposite of the spec ("main window is created
meanwhile and shown when the splash closes"), and it also causes finding 1.

Expectations:
- warm start to the first splash pixel <= 120 ms (spec: 200 ms);
- the main window is visible within 16 ms of the splash closing;
- its first `OnPaint` takes <= 5 ms.

Fix (Program.Run):
```csharp
var settings = Settings.Load(settingsPath);           // ~1 ms after P6
Theme.Apply(settings.ThemeMode);
using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))   // created first (finding 18)
using (var splash = new SplashForm { Version = AppVersion })
{
    splash.Show();
    splash.Update();                                   // synchronous first paint before any heavy work
    using (var tray = new TrayIcon())
    using (var controller = new AwakeController(SystemClock.Instance, new ThreadPowerHold()))   // disposed before tray (finding 2)
    using (var simulator = new ActivitySimulator(new MouseNudger(), TimeSpan.FromSeconds(60)))
    {
        var main = new MainForm(controller, simulator, settings, settingsPath, tray);
        main.Prewarm();                                // creates the HWND and JITs every paint path off-screen
        StartWaiter(showEvent, main);                  // only after the handle exists (finding 1)
        splash.FormClosed += (s, e) => { if (!main.IsDisposed) main.Show(); };
        Application.Run(new TrayApplicationContext(main));
    }
}
```
`MainForm.Prewarm()`:
```csharp
public void Prewarm()
{
    var h = Handle;                                    // creates the HWND without showing it
    foreach (Control c in Controls) { var ch = c.Handle; }
    using (var bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
    using (var g = Graphics.FromImage(bmp))
    {
        var r = ClientRectangle;
        OnPaintBackground(new PaintEventArgs(g, r));
        OnPaint(new PaintEventArgs(g, r));             // JIT and font realisation happen here, behind the splash
    }
}
```
The splash fade is driven by a clock, so heavy work right after `splash.Update()` can make it skip the 150 ms fade-in. To keep
the fade smooth, start the construction from `splash.Shown` via `BeginInvoke` after the first timer step, or accept a skipped
fade-in on cold start. Also apply P8 (drop `ExtractAssociatedIcon`) and P10 (create menus lazily).

### P2. High - The UI thread wakes every second forever, even when hidden and inactive
`MainForm._coreTimer` (line 28) starts in the constructor and only stops in `Quit`. Yet:
- while inactive, nothing can expire and the tooltip never changes;
- while Forever, nothing can expire either;
- while timed, the tooltip only needs one update per 60 s (spec) and expiry needs one wake at `EndUtc`.

The 60 s tray refresh also runs while inactive and re-sends the same tooltip.

Expectations:
- hidden and inactive, or hidden and Forever: 0 UI-thread context switches over 5 min;
- hidden and timed: at most 1 wake per 60 s, plus 1 at expiry;
- idle CPU shows 0.00 % in Process Explorer over 5 min.

Fix: schedule the core timer instead of polling it.
```csharp
private void ScheduleCore()
{
    _coreTimer.Stop();
    if (!_controller.IsActive || _controller.IsForever) return;
    double toEnd  = (_controller.EndUtc.Value - DateTime.UtcNow).TotalMilliseconds;
    double toTray = 60000 - _trayClock.ElapsedMilliseconds;
    double next   = Math.Min(toEnd, toTray);
    _coreTimer.Interval = (int)Math.Max(50, Math.Min(60000, Math.Ceiling(next) + 20));
    _coreTimer.Start();
}
private void CoreTick()
{
    _controller.Tick();                                // may raise StateChanged -> OnStateChanged -> ScheduleCore
    if (_trayClock.ElapsedMilliseconds >= 59900) { UpdateTrayIfChanged(); _trayClock.Restart(); }
    ScheduleCore();
}
```
Supporting changes:
- Call `ScheduleCore()` at the end of `OnStateChanged`.
- In `WndProc`, handle `WM_POWERBROADCAST` (0x0218) with `PBT_APMRESUMEAUTOMATIC` (0x12), and `WM_TIMECHANGE` (0x001E), by
  calling `CoreTick()`. That keeps expiry and the tooltip correct right after a resume or a clock change.
- In `TrayIcon.Update`, keep the last tooltip string, icon and menu states, and return early when nothing changed.
- Extract `NextWakeMs(now, endUtc, msSinceTray)` as a pure static function so it can be unit tested (section D).

### P3. Medium - While visible, about 70 % of the window repaints every second, although its content changes once a minute
`MainForm.UiTick` invalidates `(0, 0, Width, L(ToggleY))` every second: header, ring and uptime card, each time redrawing the
full `TopGlow` path gradient. Yet the displayed values change at most once a minute:
- ring "1h 23m" (minutes);
- sub line "x min left" and "ends HH:mm";
- uptime "3 d 4 h 12 min";
- "since ..." (constant).

The progress arc only moves 1 px every `duration / (pi * 178 px)`: about 51 s for 8 h, about 6 s for 1 h.

Expectations:
- visible and inactive: at most 2 paints per minute;
- visible and active (8 h): at most 3 paints per minute outside animations;
- visible idle CPU at most 0.05 % of a core (measured today: 0.76 %).

Fix: keep the 1 s timer (spec: "refresh 1 s while visible"), but invalidate only what changed.
```csharp
private string _ringKey, _cardKey; private int _arcPx = -1;
private void UiTick()
{
    var ringKey = _controller.IsActive ? RingText() + "|" + SubText() : "inactive";
    var cardKey = UptimeText() + "|" + RightCardText();
    int arcPx = (int)(_controller.Progress * Math.PI * L(RingD - RingStroke));
    if (ringKey != _ringKey || arcPx != _arcPx) { _ringKey = ringKey; _arcPx = arcPx; StartAnimOrInvalidate(RingRect); }
    if (cardKey != _cardKey) { _cardKey = cardKey; Invalidate(CardRect); }
}
```
`Uptime.BootTimeLocal` is constant, so compute it once at start-up and cache the formatted "since" string.

### P4. Medium - Each paint redraws the gradient background, allocates heavily, and draws everything regardless of the clip
`MainForm.OnPaintBackground` / `OnPaint`, `Painter.TopGlow`, `Painter.Text`, `Painter.Measure`.
- `TopGlow` builds a `GraphicsPath` and a `PathGradientBrush` over an ellipse 1.2 x 1.3 times the window, and fills it with
  AntiAlias and HighQuality pixel offset. It does that on every paint, every animation frame (60 fps) and every child repaint
  (P5). It is the most expensive single operation in the app.
- `TopGlow` does `var old = g.Clip; ... g.Clip = old;`. The `Graphics.Clip` getter returns a new GDI+ `Region` that is never
  disposed. That leaves one native region per paint for the finalizer, which churns the native heap and probably contributes
  to the private-bytes rise (P12).
- Allocations per paint:
  - `Painter.Text` creates a `StringFormat` and a `SolidBrush` per call, about 12 calls per paint;
  - `Painter.Measure` clones `StringFormat.GenericTypographic` per call, 6 calls per active paint;
  - `PaintRing` creates a `Pen`, a `LinearGradientBrush` and a glow `Pen`;
  - `PaintUptimeCard` and the border create `GraphicsPath`s.
- `OnPaint` draws every section even when the clip is only the ring (animation) or only a child's area (P5). GDI+ still lays
  out every string.
- `InterpolationMode.HighQualityBicubic` makes `DrawImage(logo, ..., L(18), L(18))` resample on every paint, although the bitmap
  is already pre-scaled.

Expectations:
- a full-window paint takes at most 2 ms at 100 % and 5 ms at 200 % on a 4-core laptop;
- an animation frame takes at most 1.5 ms;
- no native Regions are created per paint;
- at most 10 gen-0 GCs per hour while visible.

Fix:
1. Cache a static background layer in `Bitmap _bgCache` (PixelFormat.Format32bppPArgb). Render it once per (ClientSize, DPI,
   Palette instance) with the background colour, glow, card fill and border, window border, ring track, "SYSTEM UP",
   "Simulate activity (Teams, Slack)" and the footer. Discard it in `OnThemeChanged` and on DPI change. `OnPaintBackground`
   becomes `g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(_bgCache, 0, 0);`.
2. In `TopGlow` (still used for the cache and by the splash), replace the clip juggling with
   `var st = g.Save(); g.IntersectClip(surface); g.FillPath(pgb, path); g.Restore(st);`.
3. In `OnPaint`, guard each section with `if (e.ClipRectangle.IntersectsWith(sectionRect))`.
4. Cut the allocations:
   - make the `StringFormat`s static readonly fields (near/center/far, with and without trimming, plus one typographic format
     for measuring);
   - cache `SolidBrush`es per Color in a small dictionary that is cleared on theme change;
   - cache measured widths keyed by (string, font), because the hour and minute strings change once a minute.
5. Draw the logo with `g.DrawImageUnscaled`, or set `InterpolationMode.NearestNeighbor` just for that call.

### P5. Medium - Transparent child controls repaint the whole form twice on every hover and toggle frame
`PillButton`, `GlyphButton` and `ToggleSwitch` set `BackColor = Color.Transparent` with `SupportsTransparentBackColor`. To paint
a transparent child, WinForms (`Control.PaintTransparentBackground`) calls the parent's `OnPaintBackground` and `OnPaint` with a
translated Graphics. So all of these run the full `MainForm` render, `TopGlow` included:
- every mouse enter or leave on a button;
- every change of chevron hover;
- every press;
- each of the roughly 13 `ToggleSwitch` animation frames at 15 ms.

On a slow laptop that makes hover and the toggle feel laggy.

Expectations: a hover state change costs at most 0.5 ms of painting; a whole toggle animation costs at most 15 ms of CPU.

Fix: once P4.1 is in place, let each child copy its slice of the parent's cached background instead of being transparent.
```csharp
// MainForm
internal void PaintParentBackground(Control child, Graphics g)
{ EnsureBgCache(); g.DrawImage(_bgCache, new Rectangle(Point.Empty, child.Size), child.Bounds, GraphicsUnit.Pixel); }
// In each child: BackColor = Theme.Current.Bg (opaque), remove SupportsTransparentBackColor, and
protected override void OnPaintBackground(PaintEventArgs e)
{ if (Parent is MainForm f) f.PaintParentBackground(this, e.Graphics); else e.Graphics.Clear(Theme.Current.Bg); }
```

### P6. Medium - DataContractJsonSerializer runs on the start-up path
`Settings.Load` / `Settings.Save`. Loading `DataContractJsonSerializer` pulls in `System.Runtime.Serialization.dll`,
`System.Xml.dll` and their dependencies, and builds a serializer through reflection and IL generation. For a 3-field file that
typically costs 40-100 ms cold, 10-20 ms warm, and about 1.5-3 MB of private bytes. It is also the cause of finding 6.

Expectations: `Settings.Load` takes at most 2 ms warm, and `System.Runtime.Serialization` does not appear in the loaded module
list (Process Explorer, lower pane, DLL view).

Fix: a hand-written reader and writer for the three known keys, with no new dependency.
```csharp
static readonly Regex Num  = new Regex("\"lastDurationMinutes\"\\s*:\\s*(-?\\d+)", RegexOptions.CultureInvariant);
static readonly Regex Bool = new Regex("\"simulateActivity\"\\s*:\\s*(true|false)", RegexOptions.CultureInvariant);
static readonly Regex Str  = new Regex("\"theme\"\\s*:\\s*\"([A-Za-z]+)\"", RegexOptions.CultureInvariant);
public static Settings Parse(string json) { var s = new Settings(); /* apply each match if present, then validate */ return s; }
public string ToJson() => "{\"lastDurationMinutes\":" + LastDurationMinutes.ToString(CultureInfo.InvariantCulture)
    + ",\"simulateActivity\":" + (SimulateActivity ? "true" : "false") + ",\"theme\":\"" + ThemeMode + "\"}";
```
`System.Text.RegularExpressions` lives in System.dll, which is already loaded; a 30-line manual scanner would avoid even the
Regex JIT. Remove the `[DataContract]` attributes so the serializer assembly is never touched.

### P7. Low - Font family probing throws on Windows 10 during start-up
`Theme.FontFamily`. On Windows 10 `new FontFamily("Segoe UI Variable Display")` and `new FontFamily("Segoe UI Variable")` throw
`ArgumentException` because the font is not installed. Each first-chance exception costs about 1-5 ms cold (stack trace
capture plus loading the resource string) and adds noise to any debugger or ETW trace.

On Windows 11 it is also Unconfirmed whether GDI+ resolves the variable-font named instance "Segoe UI Variable Display" or
quietly substitutes another font. If it substitutes, `f.Name` differs and the code falls through to "Segoe UI Variable" or
"Segoe UI".

Expectation: no first-chance exceptions during start-up (check with DebugView or the PerfView exceptions view).

Fix: check the registry instead of constructing font families. If
`Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts")?.GetValue("Segoe UI Variable (TrueType)") != null`,
use the Variable family; otherwise use "Segoe UI". Log `Theme.FontFamily` once on a Windows 11 machine to confirm which family
GDI+ actually uses.

### P8. Low - `Icon.ExtractAssociatedIcon` is on the start-up path and yields a blurry 32 px icon
`MainForm` constructor, line 67. It is a shell call (about 5-20 ms cold) and returns only the 32x32 image, so the Alt+Tab and
taskbar icons are upscaled at 150-200 %.
Fix: `using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")) if (s != null) Icon = new Icon(s);`
The embedded icon already contains 16-256 px images.

### P9. Low - Icons are decoded twice, and the header logo is upscaled at 200 %
`TrayIcon.LoadIcon` and `MainForm.LoadLogos` both decode `tray_active.ico` and `tray_inactive.ico`. `LoadResourceImage` always
asks for the 32 px image and resamples it to `L(18)` (36 px at 200 %), so the logo is blurry there.
Fix: add 48 px images to the tray .ico files via `tools/make_icons.py`, then use `new Icon(s, L(18), L(18))`, which picks the
nearest stored size, and call `ToBitmap()` without a second resample when the size matches. Share the decoded Icons between
TrayIcon and MainForm.

### P10. Low - Three ContextMenuStrips (24 items) are built eagerly, and the first open has a one-off cost
`TrayIcon` constructor and `MainForm` constructor. The menus are built before the splash (P1), and their dropdown windows are
created on first open. That explains the measured USER objects going from 29 to 31 after the first re-show and menu use.

Expectations: the first menu open takes at most 30 ms, and USER objects stay at the post-first-use value after 50 show/hide
cycles and 20 menu opens.

Fix: build `_durationMenu` and `_extendMenu` lazily on the first `ShowMenu` (a `??=` factory), or create them behind the splash
(P1). Do not dispose and re-create menus on each open.

### P11. Low - Splash: the timer runs 1.7 s with nothing to do, the layered style flips, and the window has no title
`SplashForm.Step` / constructor.
- The 15 ms timer keeps ticking through the 1700 ms hold: about 113 ticks, each setting `Opacity = 1`.
- Setting `Opacity` to exactly 1.0 makes WinForms drop `WS_EX_LAYERED`, and the fade-out puts it back. That forces two extra
  full repaints (with `TopGlow`) and can flicker.
- The `Stopwatch` starts in the constructor, not at first show, so any delay before the first tick eats into the 2000 ms and can
  skip the fade-in.
- The form never sets `Text`, so external start-up measurement (FindWindow, UIA) cannot identify it.

Fix:
- set `Text = "KeepAwake Splash";` in the constructor (ShowInTaskbar is false, so users never see it);
- start `_clock` in `OnShown`;
- clamp opacity at `0.99` instead of `1`;
- during the hold, set `_timer.Interval` to the remaining hold time (`TotalMs - FadeMs - t`), and back to 15 for the fade-out.

### P12. Low - Private bytes rising 24.9 -> 27.0 MB over 90 s: probably the gen-0 budget filling up, not yet a proven leak
The visible window creates managed garbage on every paint (StringFormat, Brush, Pen and GraphicsPath wrappers, strings,
closures) and leaves one native GDI+ Region per paint for the finalizer (P4). At such a low allocation rate the first gen-0 GC
can take minutes, so private bytes climb and then level off. GDI objects flat at 40 and handles flat at about 304 support
"no handle leak".

Expectations (acceptance test):
- after the first 5 minutes, private bytes stay within +/-0.5 MB for 60 minutes, both hidden and visible;
- GDI and USER objects stay flat over 50 show/hide cycles.

Any steady growth after 5 minutes is a bug. In that case take two `!dumpheap -stat` snapshots (WinDbg/SOS), or two PerfView heap
snapshots, 30 min apart and compare them.

Fix: P4 (dispose or avoid the clip Region, cache StringFormats and brushes) removes nearly all per-paint garbage.

### P13. Low - Every WM_SETTINGCHANGE re-reads the theme and repaints everything
`MainForm.WndProc`, line 221. Windows broadcasts WM_SETTINGCHANGE for many unrelated changes: environment variables, policy
refresh, taskbar settings, VPN and proxy changes. During sign-in there are often several per minute. Each one does 2 registry
reads, builds a new Palette, raises `Theme.Changed`, invalidates the whole window plus all children (`Invalidate(true)`) and
updates the tray.

Expectation: `setx FOO 1`, which broadcasts WM_SETTINGCHANGE "Environment", causes no repaint.

Fix:
- refresh only when `m.LParam` is non-zero and `Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet"`, or on
  WM_DWMCOLORIZATIONCOLORCHANGED;
- in `Theme.Refresh`, raise `Changed` only if `dark` or `accent` actually changed.

### P14. Low - Settings are written synchronously on the UI thread on every activation, even when unchanged
`MainForm.StartKeepAwake` and the toggle handler call `SaveSettings()` every time. On a USB stick or a network share the write
can block the UI for 50-200 ms.
Fix: keep the last written JSON string and skip the write when it hasn't changed. Write atomically (finding 21).

### P15. Low - Thread stacks
`ThreadPowerHold` and the show-event waiter each reserve the default 1 MB stack. That is address space only; little of it is
committed.
Fix: use `new Thread(body, 64 * 1024)` for both. This shrinks virtual size slightly and changes no behaviour.

---

## B. Correctness, Win32 / P/Invoke, threading and lifetime

### 1. High - A second instance launched during the splash breaks "show window" for the rest of the session
`Program.Run`, waiter thread, line 53. The main form's HWND does not exist until `main.Show()` runs after the 2 s splash (P1).
If a second instance starts during the splash:
1. `main.BeginInvoke` throws `InvalidOperationException` ("Invoke or BeginInvoke cannot be called on a control until the window
   handle has been created").
2. `catch { return; }` ends the waiter thread.
3. Every later second launch is silently ignored.

Repro: start KeepAwake.exe, start it again within 2 s, wait, hide to tray, start it a third time. The window does not appear.

Fix:
- Create the handle before starting the waiter (`main.Prewarm()` or `var h = main.Handle;`, see P1).
- Make the loop resilient: exit only on `ObjectDisposedException`, and `continue` on `InvalidOperationException`.
- Alternatively, capture `SynchronizationContext.Current` (a WindowsFormsSynchronizationContext exists once the first control
  has been created) and use `ctx.Post(_ => { if (!main.IsDisposed) main.ShowFromTray(); }, null)`.

### 2. High - Closing by any route other than Quit while Active fires events into disposed objects (crash at sign-out) - Unconfirmed
`Program.Run` `using` order, plus `AwakeController.Dispose`, plus the `MainForm` event subscriptions. On sign-out, shutdown or
Task Manager "End task", this happens:
1. The form closes with `CloseReason.WindowsShutDown` or `TaskManagerClosing`, which is not cancelled.
2. `FormClosed` -> `ExitThread`.
3. The `using` blocks dispose in reverse order: `tray` first, then `simulator`, then `controller`.
4. `controller.Dispose()` -> `Deactivate()`, which runs because the state is still Active (`Quit()` was never called).
5. `StateChanged` -> the lambda in `MainForm` -> `OnStateChanged` -> `_tray.Update`.
6. `_tray.Update` sets `NotifyIcon.Icon` and `Text` on a disposed NotifyIcon.

In .NET Framework, `NotifyIcon.Dispose` sets its internal window to null and `UpdateIcon` dereferences it. So this is very
likely a `NullReferenceException` that escapes `Main`, showing a WER dialog or logging an event-log crash during sign-out. Even
if it does not throw, it touches disposed controls.

Repro: activate, then sign out (or `shutdown /l`). Or activate, show the window, and use Task Manager -> End task. Then check
Event Viewer > Application for a .NET Runtime error from KeepAwake.exe.

Fix:
- In `MainForm.OnFormClosed` (any reason), run the same teardown as `Quit`: stop `_coreTimer`, call `_simulator.Stop()` and
  `_controller.Deactivate()`.
- In `MainForm.Dispose(true)`, unsubscribe everything it subscribed. Keep the handlers as named methods or fields and do
  `_controller.StateChanged -= ...; _controller.Expired -= ...; _tray.ShowRequested -= ...`, plus the other tray events.
- In `Program.Run`, declare `controller` and `simulator` inside the `tray` using block so they are disposed before the tray icon.
- Optionally handle `SystemEvents.SessionEnding` to release the hold early.

### 3. High - Clicking the chevron of a split button may run the main action instead of opening the menu - Unconfirmed
`PillButton.OnMouseUp` / `OnClick`. The chevron decision is stored in `_chevronPending` inside `OnMouseUp`, which assumes
`OnMouseUp` runs before `OnClick`. In .NET Framework, `Control.WmMouseUp` raises `OnClick` and `OnMouseClick` first and calls
`OnMouseUp` last; the reference source comments this as "call the MouseUp Finally". If that holds, `_chevronPending` is always
false inside `OnClick`, and a mouse click on the chevron starts "Keep awake for 8 hours" or "Extend 1 h" instead of opening the
menu.

If the scripted UI run opened these menus with a real `SendInput` click on the chevron, this is a false alarm. If it used UIA
Invoke, the keyboard or the tray menus, this path was never exercised.

Repro: click the chevron of "Keep awake for 8 hours" with the physical mouse.

Fix: this works whatever the event order, because WinForms passes a MouseEventArgs to OnClick for mouse clicks.
```csharp
protected override void OnClick(EventArgs e)
{
    if (SplitChevron && e is MouseEventArgs me && ChevronRect.Contains(me.Location))
    { ChevronClick?.Invoke(this, EventArgs.Empty); return; }
    base.OnClick(e);
}
```
Then delete `_chevronPending`.

### 4. Medium - Double-clicking an action button triggers a second, different action
`MainForm.OnStateChanged` swaps `_activate` for `_extend`/`_deactivate` in the same place. The second click of a double-click
lands on the newly visible control. That is a different HWND, so it arrives as a normal click:
- double-click "Keep awake for 8 hours": the first click activates, the second hits "Extend 1 h", giving 9 h;
- double-click "Deactivate": the first click deactivates, the second hits the right part of the wide activate button, which
  re-activates (or, once finding 3 is fixed, opens the duration menu).

Repro: double-click "Deactivate" quickly.

Fix: ignore clicks for `SystemInformation.DoubleClickTime` ms after a state change.
```csharp
private int _ignoreClicksUntil;
// in OnStateChanged: _ignoreClicksUntil = Environment.TickCount + SystemInformation.DoubleClickTime;
private bool ClickAllowed() => unchecked(Environment.TickCount - _ignoreClicksUntil) >= 0;
// guard _activate.Click, _extend.Click, _deactivate.Click and both ChevronClick handlers with ClickAllowed()
```

### 5. Medium - Quitting from the tray during the splash leaves a zombie process, then throws
`MainForm.Quit` calls `Close()` while the main form has no handle (during the splash). This happens:
1. `Form.Close()` on a form without a handle just calls `Dispose()`. It raises neither `FormClosing` nor `FormClosed`.
2. So `TrayApplicationContext` never calls `ExitThread`.
3. When the splash closes, `splash.FormClosed` calls `main.Show()` on the disposed form.
4. That throws `ObjectDisposedException` and shows the unhandled-exception dialog, and the message loop keeps running with no
   window.

Repro: launch, right-click the tray icon within 2 s, and choose Quit.

Fix:
- Let the application context own the exit. Give `MainForm` an `ExitRequested` event that `Quit()` raises after teardown; in
  `TrayApplicationContext`, subscribe to it and call `ExitThread()`.
- Guard the splash handler with `if (!main.IsDisposed)`.
- P1's pre-created handle also removes the no-handle path.

### 6. Medium - A partial settings file turns the default duration into Forever
`Settings.Load`. `DataContractJsonSerializer` does not run constructors or property initializers; it uses
`FormatterServices.GetUninitializedObject`. So a file without `lastDurationMinutes` (hand-edited, or written by a future version
that renames the key) yields `LastDurationMinutes = 0`. Zero is `Durations.ForeverMinutes`, so it passes the "known" check and
the main button reads "Keep awake forever". A missing `simulateActivity` comes out false only by luck; a missing `theme` is
patched by the code.

`Enum.TryParse` also accepts numeric strings: `"theme":"5"` gives `(ThemeMode)5`, which renders the light theme with no tray
item checked.

Repro: write `{"theme":"Dark"}` to keepawake.json and start the app.

Fix, if DataContract is kept:
```csharp
[OnDeserializing] private void OnDeserializing(StreamingContext _) { LastDurationMinutes = Durations.DefaultMinutes; Theme = "System"; }
```
and in `ThemeMode`: `Enum.TryParse(Theme, true, out ThemeMode m) && Enum.IsDefined(typeof(ThemeMode), m) ? m : ThemeMode.System`.
The P6 hand parser starts from `new Settings()`, so it fixes this automatically. Add the tests in section D.

### 7. Medium - Changing the theme at runtime leaves the main window's menus with unreadable text
`MenuRenderer.InitializeItem` sets `item.ForeColor = P.Text` once, when the item is added. `OnRenderItemText` uses
`e.Item.ForeColor` for normal items, while the background is cleared with the current `P.Menu`. `TrayIcon.ApplyThemeToMenu`
re-applies the colours for the tray menu, but nothing does that for `_durationMenu` and `_extendMenu`.

Repro: in the Dark theme, choose tray > Theme > Light, then click the chevron of the main button. The items are #F2F4F8 on
white. Going the other way gives dark text on #171A21.

Fix: in `OnRenderItemText`, use `P.Text` instead of `e.Item.ForeColor` for the normal case. `OnRenderToolStripBackground`
already uses the current `P.Menu`. `ApplyThemeToMenu` then becomes unnecessary.

### 8. Medium - Per-monitor DPI changes are not handled consistently - Unconfirmed
`app.manifest` (PerMonitorV2), `MainForm.OnDpiChanged`, `Theme.Font`. There are two problems:
1. .NET Framework 4.8 WinForms only fully processes WM_DPICHANGED (raising `OnDpiChanged` and updating `DeviceDpi`) when
   `DpiAwareness=PerMonitorV2` is also set in the app's `.config` file. There is no App.config, so `OnDpiChanged` is probably
   dead code. Moving the window to a monitor with a different scale then leaves it at the old pixel size.
2. If the event does fire, the layout scales via `L()` (DeviceDpi), but fonts are created in points. GDI+ converts points using
   the Graphics' DPI, which for an HWND DC is the system DPI. The text then stays at the old size while the layout grows or
   shrinks.

Repro: use a 100 % primary monitor and a 150 % secondary monitor, then drag the window across. Or open it with the cursor on the
secondary monitor (CenterScreen centres on the cursor's screen).

Fix: handle DPI yourself. This keeps the single exe, whereas an exe.config would add a file.
```csharp
const int WM_DPICHANGED = 0x02E0;
if (m.Msg == WM_DPICHANGED) {
    _dpi = HIWORD(m.WParam);
    var r = Marshal.PtrToStructure<RECT>(m.LParam);
    SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SWP_NOZORDER | SWP_NOACTIVATE);
    OnDpiChangedManual(); return;
}
```
- Use `_dpi` everywhere instead of `DeviceDpi`, including in the child controls (pass a `Scale` property down). Initialise it
  from `GetDpiForWindow(Handle)` (Windows 10 1607+).
- Create fonts in pixels with `new Font(family, px * _dpi / 96f, style, GraphicsUnit.Pixel)` and cache them by (px, style, dpi).

Fonts and layout then follow the same scale factor.

### 9. Medium - The window can be restored off-screen
`MainForm.ShowFromTray`. The position is kept for the session. If the monitor it was on is disconnected, or the resolution or
docking changes while the window is hidden, `Show()` puts it somewhere no longer visible.
Fix: before `Show()`:
```csharp
if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(Bounds))) CenterToScreen();
```
`CenterToScreen` is a protected Form member, so call it from inside MainForm.

### 10. Medium - A second instance cannot bring the window to the foreground
`Program.Main` (second instance) and `MainForm.ShowFromTray`. The first instance runs in the background, so the foreground lock
blocks its `SetForegroundWindow`. The window appears behind the active app and the taskbar button flashes.
Fix: in the second instance, call `AllowSetForegroundWindow(ASFW_ANY)` before `ev.Set()`:
`[DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int dwProcessId); const int ASFW_ANY = -1;`
The user just launched the second instance, so it owns the foreground and is allowed to pass it on.

### 11. Medium - No global exception handling
`Program.Main`. An exception in a WinForms timer tick, paint or event handler shows the default WinForms "Unhandled exception"
dialog, with a stack trace and Continue/Quit buttons. An exception on a background thread (see finding 12) terminates the
process.
Fix:
```csharp
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
Application.ThreadException += (s, e) => Trace.WriteLine(e.Exception);         // keep running; visible in DebugView
AppDomain.CurrentDomain.UnhandledException += (s, e) => Trace.WriteLine(e.ExceptionObject);
```
Also wrap the bodies of `CoreTick`, `UiTick` and `AnimTick` in try/catch, so a failure while painting cannot stop expiry.

### 12. Medium - ThreadPowerHold has a race and ignores its result
`ThreadPowerHold.Hold` / `Release`.
- `ready.Wait(2000); ready.Dispose();`: if the thread has not reached `ready.Set()` within 2 s, `Set()` on the disposed event
  throws `ObjectDisposedException` on a background thread and kills the process. This can happen when thread start is slow
  under heavy load, when a VM is paused, or under a debugger. `IsHeld` also reports true before the state is asserted.
- In `Release`, if `t.Join(2000)` times out, `release.Dispose()` runs while the thread may still be inside `release.Wait()`.
- The return value of `SetThreadExecutionState` is ignored. Zero means failure, yet the UI still claims "screen stays on".

Fix:
```csharp
var t = new Thread(() => {
    var prev = NativeMethods.SetThreadExecutionState(Continuous | SystemRequired | DisplayRequired);
    _assertOk = prev != 0;            // volatile bool, exposed as IPowerHold.LastHoldSucceeded
    ready.Set();
    release.Wait();
    StateBeforeRelease = (uint)NativeMethods.SetThreadExecutionState(Continuous);
}, 64 * 1024);
t.Start();
ready.Wait();                         // no timeout: a started thread always runs
ready.Dispose();
...
// Release:
release.Set();
if (t.Join(2000)) release.Dispose();  // otherwise leave it to the GC
```
If `LastHoldSucceeded` is false, show a warning in the ring's sub line.

### 13. Medium - Expiry and elapsed time follow the wall clock
`SystemClock.UtcNow => DateTime.UtcNow` is used for `StartedUtc`, `EndUtc`, `Elapsed` and `Remaining`. The expiry moves by the
same amount whenever the clock jumps: a manual clock change, an NTP correction (common right after resume when the RTC has
drifted), or a VM time sync.
- Setting the clock forward 1 h ends a 2 h session after 1 h.
- Setting it back makes `Elapsed` negative (Progress clamps to 0, so the ring is empty) and extends the session.

Fix: make the controller's clock monotonic.
```csharp
public sealed class SystemClock : IClock {
    private static readonly DateTime Origin = DateTime.UtcNow;
    private static readonly ulong OriginTicks = NativeMethods.GetTickCount64();
    public DateTime UtcNow => Origin.AddMilliseconds(NativeMethods.GetTickCount64() - OriginTicks);
}
```
GetTickCount64 keeps counting during sleep and hibernate (verify on the target machine), so a timed session still ends at the
expected wall time after a resume. Show "ends HH:mm" as `DateTime.Now + Remaining` rather than `EndUtc.ToLocalTime()`, and
refresh it on WM_TIMECHANGE (see P2).

### 14. Low - The settings file may be read-only, and the probe writes an extra file on every start
`SettingsStore.ResolvePath`. When `keepawake.json` already exists beside the exe, the path is returned without checking that the
file is writable. It may not be: a read-only attribute, the exe copied into Program Files together with the file, or ACLs from a
ZIP extracted by another user. Every `Save` then fails silently (`SaveSettings` swallows the error) and nothing persists, while
the spec wants the %LOCALAPPDATA% fallback.

Separately, `IsWritable` creates and deletes `.keepawake-<guid>.tmp` beside the exe on every start. That contradicts "no files
written except settings", can raise a Controlled Folder Access notification, and triggers a Defender scan on the start-up path.

Fix:
```csharp
public static string ResolvePath(string exeDir) {
    var beside = Path.Combine(exeDir, FileName);
    var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeepAwake", FileName);
    if (File.Exists(fallback)) return fallback;                     // a previously chosen fallback wins
    if (File.Exists(beside)) return CanOpenForWrite(beside) ? beside : fallback;
    return beside;                                                  // decide on the first Save
}
```
- In `MainForm.SaveSettings`, on `UnauthorizedAccessException` or `IOException`, switch `_settingsPath` to the fallback and retry
  once with the current values (not the old file).
- `CanOpenForWrite` is `File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)` inside try/catch. It creates
  no file.

### 15. Low - Windows 11: the painted 16 px border does not match the DWM corner radius
`MainForm.OnPaint` (border `16 * s`), `SplashForm.OnPaint`, `WindowChrome.ApplyRoundedCorners`. `DWMWCP_ROUND` gives the system
radius (about 8 px), not 16 px. The window is clipped at 8 px while the painted border curves at 16 px, so each corner shows a
wedge of background outside the border. The spec says 16 px corners.
Fix: on Windows 11, when `DwmSetWindowAttribute` succeeds, paint the window border with radius `8 * s`. Store
`WindowChrome.CornerRadius` as 8 on Windows 11 and 16 on Windows 10. Alternatively, record in the spec that Windows 11 uses the
system radius.

### 16. Low - Windows 10 region path: rectangular shadow, and the region handle leaks on failure
`WindowChrome.ApplyRoundedCorners`. `CS_DROPSHADOW` draws a rectangular shadow that ignores the rounded region, so on
Windows 10 the shadow shows square corners (Unconfirmed visually). If `SetWindowRgn` fails (returns 0), the region is never freed.
Fix: `if (NativeMethods.SetWindowRgn(f.Handle, rgn, true) == 0) DeleteObject(rgn);` (add a gdi32 `DeleteObject` import). On
Windows 10, either drop `CS_DROPSHADOW` or accept the square shadow and record it in the spec.

### 17. Low - Keyboard access and accessibility of the custom controls
`PillButton.OnKeyDown`: Down is a dialog key, so `ProcessDialogKey` moves focus before `OnKeyDown` sees it, and the chevron
cannot be opened from the keyboard. None of the custom controls set `AccessibleName` or `AccessibleRole`, so Narrator reads them
as unnamed panes and UIA test scripts have to find them by coordinates.
Fix:
- add `protected override bool IsInputKey(Keys k) => k == Keys.Down || base.IsInputKey(k);`;
- set `AccessibleRole = AccessibleRole.PushButton` on PillButton and GlyphButton, and `CheckButton` on ToggleSwitch;
- set `AccessibleName` to the visible text ("Minimize", "Close", "Simulate activity");
- for the toggle, override `CreateAccessibilityInstance` to report the checked state.

### 18. Low - Single-instance race at start-up
`Program.Main` / `Run`. The named event is only created after settings, theme and path probing. A second launch in that window
(for example a double-click on the exe) finds no event, swallows the exception and exits, so nothing is shown.
Fix: create the `EventWaitHandle` immediately after acquiring the mutex, before anything else. In the second instance, retry
`OpenExisting` for up to 2 s (20 x 100 ms) before giving up.

### 19. Low - A fast double-click on the toggle only toggles once
`ToggleSwitch` and `GlyphButton` keep the default `ControlStyles.StandardDoubleClick`, so the second click of a fast pair
becomes a DoubleClick and is dropped. Two quick clicks leave the switch on instead of turning it back off.
Fix: call `SetStyle(ControlStyles.StandardDoubleClick, false)` in the `ToggleSwitch` and `GlyphButton` constructors so every
click raises Click.

### 20. Low - Activity simulation nudges even while the user is active, and ignores failures
`MouseNudger.Nudge`.
- It moves the cursor every 60 s even while the user is drawing, selecting or gaming, which shows as a 1 px jitter.
- `SendInput`'s return value is ignored. It returns 0 when the session is locked, or when UIPI blocks injection into an elevated
  foreground window.
- `GetLastInputInfo` is declared but never used.

Fix: skip the nudge while the user is active.
```csharp
var lii = new NativeMethods.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
if (NativeMethods.GetLastInputInfo(ref lii) && unchecked((uint)Environment.TickCount - lii.dwTime) < 50_000) return;  // user active
```
Optionally record the last `SendInput` result for diagnostics.

### 21. Low - Settings writes are not atomic
`Settings.Save` uses `File.WriteAllBytes`. A crash or power loss mid-write leaves a truncated file, which silently resets
everything to defaults on the next start.
Fix: write to `path + ".tmp"`, then `if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);`.

### 22. Low - The ring keeps the old progress after Deactivate
`MainForm.OnStateChanged` / `AnimTick`. After Deactivate, `_shownProgress` only animates toward 0 while the window is visible,
and the ring is not drawn when inactive. Re-activating during that animation, or after deactivating while hidden, starts the arc
from the stale value and animates it backwards.
Fix: in `OnStateChanged`, add `if (!_controller.IsActive) _shownProgress = 0;`.

### 23. Low - Remaining time rounds down
`TimeFormat.Compact(Remaining)`, used in the ring sub line and the tooltip. Right after "Keep awake for 8 hours" both show
"7 h 59 min left", and a 1 h session shows "59 min left".
Fix: for remaining-time displays, use
`TimeFormat.Compact(TimeSpan.FromMinutes(Math.Ceiling(rem.TotalMinutes - 1e-9)))`, and keep "under a minute" for the last 60 s.

### 24. Low - Some DPI-sensitive cosmetics are not scaled
- `MenuRenderer`: item padding 10/6, menu padding 6, radii 6, and the arrow and check geometry are all fixed pixels.
- `ToggleSwitch`: the 3 px knob inset is fixed.
- `TrayIcon.LoadIcon` reads `SmallIconSize` once, so a later change of the primary display's DPI leaves a blurry tray icon.

Fix: multiply these values by `DeviceDpi / 96f` (or the `_dpi` from finding 8). Reload the tray icons on `WM_DPICHANGED` or on
`SystemEvents.DisplaySettingsChanged`.

---

## C. Behaviour versus docs/DESIGN_SPEC.md

Each row is a spec rule that the code does not implement, or implements differently. Severity is Low unless stated.

| # | Spec rule | Code | Fix |
|---|---|---|---|
| C1 (Medium) | Footer: "GitHub" link on the right | Not implemented; `PaintFooter` draws only the version | Paint "GitHub" right-aligned in Muted. In `OnMouseMove`, show the Hand cursor over its rect. In `OnMouseClick`, call `Process.Start(new ProcessStartInfo("https://github.com/karthikbhaskar-1/KeepAwake") { UseShellExecute = true })`. This only opens the browser, so it conflicts with "No network" in spirit only. |
| C2 (Medium) | Splash: "main window is created meanwhile and shown when the splash closes" | The MainForm object is built **before** the splash; its HWND and first paint happen **after** it (P1) | P1 |
| C3 | Splash: 2000 ms total, 150 ms fades | The Stopwatch starts in the constructor, so start-up delay shortens the visible time and can skip the fade-in | P11 |
| C4 | Footer 11 px **muted** | Uses `p.Faint` at 8 pt (10.7 px) | Use `p.Muted` at 8.25 pt |
| C5 | Font sizes: header 13 px **semibold**; value 38 px; units 16 px; "Inactive" 26 px; label 11 px **letter-spaced**; sub line 12 px; uptime value 16 px semibold | Header bold; value 27 pt (36 px); units 11.5 pt (15.3 px); "Inactive" 19 pt (25.3 px); label 7.75 pt (10.3 px), no tracking; sub line 8.75 pt (11.7 px); uptime 11.5 pt (15.3 px) | Define sizes in px (finding 8) using the spec values. For semibold use the family "Segoe UI Semibold" or "Segoe UI Variable Display Semibold". For letter spacing, draw the characters with a +1.5 px advance (or insert hair spaces, U+200A). |
| C6 | Corners 16 px (Win11 via DWM) | DWM ROUND gives about 8 px | Finding 15 |
| C7 | Shadow on Win11 (architecture: DwmExtendFrameIntoClientArea) | Uses the `CS_DROPSHADOW` class style; `DwmExtendFrameIntoClientArea` is declared but unused | Either call `DwmExtendFrameIntoClientArea(Handle, new MARGINS{1,1,1,1})` on Win11 and drop CS_DROPSHADOW there, or update the spec |
| C8 | Ring gradient "accent -> cyan"; gradient end = accent shifted toward cyan #22D3EE | `Accent2` is the accent hue minus 30 degrees; `Theme.Cyan` is unused. With red or orange accents the gradient ends in pink | `accent2 = Painter.Mix(accent, Cyan, 0.55f)` |
| C9 | Accent = Windows AccentColor, else #4F8CFF | `EnsureVivid` changes the saturation and lightness of the system accent | Keep it (it is a good idea), but note it in the spec |
| C10 | Ring "glow when active" | Glow only in the dark theme | Draw the glow in light too, with a lower alpha (about 35) |
| C11 | Expiry toast "Kept awake for 8 h" | "Kept awake for 8 h 0 min." | Add `TimeFormat.Duration(TimeSpan)`, which omits zero trailing units ("8 h", "1 h 30 min"), and drop the full stop |
| C12 | "since {ddd HH:mm} (or date if > 6 days)" | Shows the date when age >= 6 days (the test is `< 6`) | Use `age.TotalDays <= 6`, or fix the spec |
| C13 | Tray menu: Extend "disabled when inactive" | Also disabled when Forever | Functionally fine; update the spec |
| C14 | Theme submenu "radio" | Uses `Checked`, rendered as bold accent text; there is no radio mark because the check margin is hidden | Acceptable as is, or draw a dot in `OnRenderItemText` for checked items in that submenu |
| C15 | Theme "System ... applies live" | Works, but the main window's menus keep stale colours | Finding 7 |
| C16 | "Missing or invalid file -> defaults" | A partial file gives Forever | Finding 6 |
| C17 | Settings fall back when the exe folder is read-only | No fallback when the file exists but is read-only | Finding 14 |
| C18 | Non-functional: "no files written except settings" | A probe temp file is written on every start | Finding 14 |
| C19 | Non-functional: "Warm start to splash < 200 ms" | Not measured; the heavy work runs before the splash | P1, P6, P11 |
| C20 | "Second instance signals the first to show" | Breaks permanently if launched during the splash, and cannot take the foreground | Findings 1, 10, 18 |
| C21 | "Quit: Deactivate, dispose tray icon, exit" | Correct via the tray; broken during the splash; the sign-out path is not covered | Findings 2, 5 |
| C22 | "Deactivate ... ring resets" | Stale `_shownProgress` | Finding 22 |
| C23 | "DPI-aware, scales with system" | Only at the start-up DPI; fonts are in points | Finding 8 |

Checked and compliant:
- the durations list and the default;
- the "Keep awake for {duration}" label;
- extend semantics: `endUtc += span`, Forever stays Forever, null makes it Forever;
- the expiry check every 1 s (P2 changes it to fire at the exact time) and the toast title;
- activity simulation: +1/-1 every 60 s, only while Active with the toggle on;
- tooltip formats, under 64 chars (truncated at 63), refreshed on state change and every 60 s;
- the bold Show item and the Deactivate enable state;
- hide on minimize, close and Alt+F4, with no taskbar button while hidden;
- the 42 px header drag strip;
- the uptime card texts and the inactive ring texts;
- Forever shows "until deactivated" with a full ring.

---

## D. Test gaps (unit-testable logic only)

1. `Settings.Load` with a partial file: `{"theme":"Dark"}` should give LastDurationMinutes == 480 (fails today, finding 6), and
   `{"lastDurationMinutes":60}` should give Theme "System" and SimulateActivity false.
2. `Settings.ThemeMode` with `"5"`, `"-1"`, `" dark "` and `"DARK"`: undefined numeric values fall back to System.
3. `Settings.Load` with a JSON `null` literal, an empty file, a file with a UTF-8 BOM, and extra unknown keys.
4. `SettingsStore.ResolvePath` when the file beside the exe exists and is read-only (set `FileAttributes.ReadOnly`): returns the
   fallback once finding 14 is fixed. Also check that the call leaves no extra files in the directory (no probe residue).
5. Atomic save: `Save` leaves no `.tmp` behind, and `Save` over a read-only file throws (so `SaveSettings` can fall back).
6. `TimeFormat.RingParts`:
   - 0 gives ("0","00");
   - 59 min 59 s gives ("0","59");
   - 25 h 5 min gives ("25","05");
   - a negative span gives ("0","00").
7. `TimeFormat.Compact`: "1 d 0 h 5 min" (zero middle unit) and "8 h 0 min". The second pins the current text, or the new
   `Duration` text "8 h" for the toast (C11).
8. `TimeFormat.Since` with a fixed culture (an overload taking `CultureInfo.InvariantCulture`, or the test thread's culture
   set): 5.99 days gives the weekday format; 6 and 7 days give the date format (pins C12).
9. `Durations`:
   - the `Extend` list is exactly 1 h, 2 h, 4 h, Forever;
   - `ActivateLabel` for Forever and for 8 h;
   - `FromMinutes(0)` returns the Forever option (MainForm line 53 relies on this).
10. `AwakeController`:
    - `Tick` exactly at `EndUtc` expires (the boundary uses `<`);
    - Extend after `EndUtc` has passed but before `Tick` runs (the resume scenario): pin the intended semantics;
    - `Activate` while already active resets StartedUtc and does not call `Hold` twice on a real hold;
    - `Expired` is raised after `StateChanged`, and the state is Inactive inside the Expired handler;
    - clock moving backwards: `Elapsed` < 0 gives `Progress == 0` and `RingParts` "0","00" (or, after finding 13, the clock is
      monotonic);
    - `Dispose` while Active raises `StateChanged` (documents finding 2's precondition) and releases, then disposes, the hold;
    - a tooltip with more than 24 h remaining (24 h plus Extend 4 h) stays under 64 chars and shows days.
11. `Theme`:
    - extract `static Color FromAbgr(int)` and test that `0xFFD77800` gives RGB(0x00,0x78,0xD7);
    - make `Build(bool dark, Color accent)` `internal` (plus `InternalsVisibleTo("KeepAwake.Tests")`) and assert the spec hex
      values for Bg, Text, Muted, Faint, Track, Menu, MenuBorder, Success and Danger in both themes. That turns the spec's colour
      table into a regression test.
12. `Painter.FromHsl` / `Mix`: primary colours round-trip, and `Mix` clamps t outside 0..1.
13. Split-button hit-testing: extract `static bool IsChevronHit(int width, int dpi, int chevronWidth, Point p)` and test it at
    96, 120, 144 and 192 DPI (guards finding 3).
14. Core timer scheduling (after P2), `NextWakeMs(now, end, msSinceTray)`:
    - inactive: no wake;
    - Forever: no wake;
    - 10 s to the end: 10 s;
    - 3 min to the end, 20 s since the last tray update: 40 s;
    - past the end: the 50 ms minimum.
15. `ActivitySimulatorTests` use 15 ms intervals and `Thread.Sleep`, so they can be flaky on a loaded CI runner. Inject a timer
    factory (`Func<TimerCallback, TimeSpan, IDisposable>`) and drive the callbacks by hand. Keep one real-timer smoke test with
    generous margins.
16. `ThreadPowerHold`:
    - Hold, Release, then Hold again works (the thread is re-created);
    - `Dispose` without `Hold` is safe;
    - `StateBeforeRelease` includes `ES_CONTINUOUS` (0x80000000).

---

## E. CI workflow, README and docs

### E1. Medium - The build depends on the runner's preinstalled .NET Framework 4.8 targeting pack
`KeepAwake.csproj` targets `net48` with the .NET 8 SDK but does not reference `Microsoft.NETFramework.ReferenceAssemblies`,
although ARCHITECTURE says it does. The build works on `windows-latest` only because Visual Studio's targeting packs are
installed there. On a dev box with only the .NET 8 SDK (which the README says is enough), it fails with MSB3644 "reference
assemblies for .NETFramework,Version=v4.8 were not found".
Fix (pick one; it is the user's decision because of the "no new packages" rule):
- add `<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />` to both
  projects. It is build-time only; nothing ships in the exe.
- or change the README to "requires the .NET 8 SDK and the .NET Framework 4.8 Developer Pack".

### E2. Low - Workflow hardening
`.github/workflows/build.yml`:
- `permissions: contents: write` applies to every run, including pull requests. Set `contents: read` at the top, and grant
  `contents: write` only to a separate `release` job (`needs: build`, `if: startsWith(github.ref, 'refs/tags/v')`).
- Actions are pinned to tags (`@v4`, `@v2`). Pin them to commit SHAs for supply-chain safety, at least
  `softprops/action-gh-release`.
- Nothing enforces the "< 1 MB" requirement. Add a size gate:
  `if ((Get-Item src/KeepAwake/bin/Release/net48/KeepAwake.exe).Length -ge 1MB) { throw "exe exceeds 1 MB" }`.
- Nothing checks that the tag matches the version. A `v1.1.0` tag with `<Version>1.0.0</Version>` publishes an exe that reports
  1.0.0. Add a step that compares `${{ github.ref_name }}` with the csproj Version, or pass `-p:Version=${GITHUB_REF_NAME#v}` on
  tag builds.
- Publish a SHA-256 next to the exe (`Get-FileHash ... | Out-File KeepAwake.exe.sha256`). This is useful for an unsigned binary.
- Upload `results.trx` with `actions/upload-artifact` (`if: always()`) so test failures can be inspected.

### E3. Low - README inaccuracies
- "Build from source ... Requires the .NET 8 SDK": see E1.
- The output path is `src/KeepAwake/bin/Release/net48/`, not `bin/Release/`.
- "a missing or invalid file is recreated safely": a missing file is not recreated until a setting changes, and an invalid file
  is silently overwritten (losing its contents) on the next change. Reword to: "A missing or invalid file means defaults; it is
  rewritten on the next change."
- "the only file it creates is its own settings JSON": the app also creates the probe temp file (finding 14) until that is fixed.
- Add the honest limitation: SetThreadExecutionState does not stop a sleep the user starts (Start > Sleep, the power button,
  closing the lid when the lid action is Sleep), and some corporate policies or Modern Standby setups can still turn off the
  display.
- "Requirements: ... 32- or 64-bit": the app also runs on Windows 11 ARM64 (AnyCPU, .NET Framework 4.8.1 native ARM64). Worth
  stating once verified.
- "Quit" is only in the tray menu, and closing the window just hides it. Say so in Usage > Window.

### E4. Low - ARCHITECTURE.md is out of date
- It says C# 7.3; the csproj uses 9.0.
- It lists `UI/Toast.cs`, `RingGauge`, `SplitButton` and `FlatIconButton`. In the code, the toast is part of `TrayIcon`, the
  controls are `PillButton` and `GlyphButton`, and the ring is painted in MainForm.
- The risk section says the execution state is "asserted from the UI thread timer"; it is actually asserted on a dedicated thread.
- It describes the shadow via `DwmExtendFrameIntoClientArea`, which is not used.
- Icon file names use hyphens (`tray-active.ico`); the files use underscores.

Update it before the 1.1 tag so reviewers are not misled.

### E5. Low - Repository hygiene
`tools/readme_usage.txt`, `tools/review_usage.txt` and `tools/tests_usage.txt` are tracked although `.gitignore` lists
`tools/*.txt`; they were committed before the rule was added. Their content is harmless (a token usage ledger), but
`git rm --cached tools/*.txt` keeps the repo consistent with the ignore file.

---

## F. Checked and found OK (no action)

- `INPUT`/`MOUSEINPUT` layout: `sizeof(INPUT)` is 40 on x64 (type 4 + pad 4 + union 32) and 28 on x86. The explicit union with
  `IntPtr dwExtraInfo` gets the right alignment, and `Marshal.SizeOf(typeof(INPUT))` is passed as `cbSize`. This is correct for
  the AnyCPU build on x86, x64 and ARM64.
- `SetThreadExecutionState` from a dedicated, blocked thread: the per-thread requirement lasts as long as the thread, needs no
  message loop, and is cleared on release, on thread exit or on process death. The `ES_CONTINUOUS` call on release runs on the
  same thread. Correct.
- `GetTickCount64` returns `ulong`, and `DwmSetWindowAttribute(…, ref int, sizeof(int))` and the corner preference constants are
  right. The Windows 11 check works because the manifest declares Windows 10/11 support, so `OSVersion` reports 10.0.build.
- NotifyIcon re-adds itself after an Explorer restart (WinForms handles `TaskbarCreated`), so no code is needed.
- Tooltip length: the worst case, "KeepAwake - Active - 99 d 23 h 59 min left", is 42 chars, so the 63-char truncation guard is
  fine.
- `ActivitySimulator.Stop` disposing a `System.Threading.Timer` while a callback may be running is harmless: the callback is
  idempotent and catches its own exceptions.
- `_uiTimer` and `_animTimer` stop on hide (see the analysis in section A). GDI objects flat at 40 confirm there is no GDI handle
  leak in the paint paths.

---

## G. Next version candidates (features and behaviour changes, separate from the fixes)

| Candidate | Rationale | Effort |
|---|---|---|
| "Until a time" duration (for example until 17:30) | The most common real need ("until the end of the workday"), without doing the arithmetic | S |
| "Keep screen on" switch (system-only mode) | Downloads and builds often need the PC awake but not the display; drop `ES_DISPLAY_REQUIRED` | S |
| Expiry warning 5 min before the end, with "Extend 1 h" on the tray balloon click | Avoids a surprise sleep in the middle of a meeting or download | S |
| Command-line verbs `--for 2h`, `--until 17:30`, `--forever`, `--off`, `--quit`, forwarded to the running instance (named pipe, or a second named event plus a small memory-mapped payload) | Scriptable from shortcuts, Task Scheduler and build scripts | M |
| Global hotkey to toggle (RegisterHotKey, configurable in the JSON) | One-keystroke activation without opening the window | S |
| Middle-click on the tray icon toggles activate/deactivate | A quick action without the window | S |
| Smarter simulation: only nudge when idle, with an optional F15 key press instead of a mouse move | Avoids visible jitter, and works in remote sessions that ignore mouse moves | S |
| Pause when on battery, or below X % battery (`GetSystemPowerStatus`, `WM_POWERBROADCAST`) | Protects the laptop battery when "Forever" is forgotten | S |
| Optional deactivate on session lock (`WTSRegisterSessionNotification`) | Many users only want it while they are at the desk | S |
| Remember the window position across sessions (in settings) | Small usability gain; the spec currently says per session | S |
| Tray icon that shows progress (a few pre-rendered ring states) | Remaining time at a glance, without hovering | M |
| "Start with Windows" opt-in (an HKCU Run value, removed when toggled off) | Often requested for this kind of tool. It conflicts with "no registry writes", so it must be opt-in and documented | S |
| Activate automatically while selected processes run (for example a presenter app, OBS or a build tool) | Removes manual toggling for known long tasks | M |
| Accessibility pass: UIA names and states, a high-contrast theme (`SystemInformation.HighContrast`), keyboard shortcuts in the window (Space = activate/deactivate, Esc = hide) | More inclusive, and makes UI tests robust | M |
| Localisation using an embedded string table chosen by `CurrentUICulture` (no satellite assemblies) | A wider audience while keeping the single exe | M |
| Code signing (Azure Trusted Signing or SignPath OSS) | Removes the SmartScreen warning, the main adoption barrier for an unsigned exe | M |
| winget and Scoop manifests generated by CI on release | Easy install and update while staying portable | S |
| Only if start-up still misses 200 ms after P1, P6 and P11: a tiny native (C++/Win32) splash that paints instantly while the CLR loads (net48 has no AOT option) | Last resort for the start-up target | L |
