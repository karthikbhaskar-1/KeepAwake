using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeepAwake.Core;
using KeepAwake.Native;
using KeepAwake.UI.Controls;

namespace KeepAwake.UI
{
    /// <summary>The 360 x 480 frameless main window. Everything except the buttons and the toggle is painted here.</summary>
    public sealed class MainForm : Form
    {
        private const int W = 360, H = 480, Pad = 18;
        private const int HeaderY = 14, HeaderH = 28;
        private const int RingY = 56, RingH = 196, RingD = 188, RingStroke = 10;
        private const int CardY = 266, CardH = 56;
        private const int ToggleY = 336, ToggleH = 24;
        private const int ActionH = 46, FooterH = 16;
        private const string GitHubUrl = "https://github.com/karthikbhaskar-1/KeepAwake";

        private readonly AwakeController _controller;
        private readonly ActivitySimulator _simulator;
        private readonly Settings _settings;
        private readonly TrayIcon _tray;
        private readonly Timer _coreTimer = new Timer { Interval = 1000 };   // scheduled: expiry and tray tooltip only when needed
        private readonly Timer _uiTimer = new Timer { Interval = 1000 };     // only while visible: repaint what changed
        private readonly Timer _animTimer = new Timer { Interval = 16 };
        private readonly Stopwatch _trayClock = Stopwatch.StartNew();

        private readonly PillButton _activate, _extend, _deactivate;
        private readonly ToggleSwitch _simulate;
        private readonly GlyphButton _minimize, _close;
        private ContextMenuStrip _durationMenu, _extendMenu;
        private Image _logoActive, _logoInactive;

        private string _settingsPath;
        private string _lastSavedJson;
        private DurationOption _chosen;
        private double _shownProgress;
        private bool _allowClose, _shutDown;
        private int _ignoreClicksUntil;

        private Bitmap _bgCache;
        private Palette _bgPalette;
        private int _bgDpi;
        private Rectangle _githubRect;
        private string _sinceText;
        private string _ringKey, _cardKey;
        private int _arcPx = -1;

        public string Version { get; }

        /// <summary>Raised after the window has torn down its state because the user chose Quit.</summary>
        public event EventHandler ExitRequested;

        public MainForm(AwakeController controller, ActivitySimulator simulator, Settings settings, string settingsPath, TrayIcon tray)
        {
            _controller = controller;
            _simulator = simulator;
            _settings = settings;
            _settingsPath = settingsPath;
            _tray = tray;
            try { _lastSavedJson = System.IO.File.Exists(settingsPath) ? settings.ToJson() : null; } catch { }
            Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            _chosen = settings.LastDurationMinutes == Durations.ForeverMinutes
                ? Durations.All[Durations.All.Count - 1]
                : Durations.FromMinutes(settings.LastDurationMinutes);

            Text = "KeepAwake";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowIcon = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            ClientSize = new Size(L(W), L(H));
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (s != null) Icon = new Icon(s);
            }
            catch { }

            _activate = new PillButton { Kind = PillKind.Primary, SplitChevron = true };
            _activate.Click += (s, e) => { if (ClickAllowed()) StartKeepAwake(_chosen); };
            _activate.ChevronClick += (s, e) => { if (ClickAllowed()) ShowMenu(DurationMenu, _activate); };

            _extend = new PillButton { Kind = PillKind.Secondary, SplitChevron = true, ChevronWidth = 40, Text = "Extend 1 h", Visible = false };
            _extend.Click += (s, e) => { if (ClickAllowed()) Extend(Durations.Extend[0]); };
            _extend.ChevronClick += (s, e) => { if (ClickAllowed()) ShowMenu(ExtendMenu, _extend); };

            _deactivate = new PillButton { Kind = PillKind.Danger, Text = "Deactivate", Visible = false };
            _deactivate.Click += (s, e) => { if (ClickAllowed()) _controller.Deactivate(); };

            _simulate = new ToggleSwitch();
            _simulate.SetSilently(settings.SimulateActivity);
            _simulate.CheckedChanged += (s, e) =>
            {
                _settings.SimulateActivity = _simulate.Checked;
                SaveSettings();
                SyncSimulator();
            };

            _minimize = new GlyphButton { Glyph = Glyph.Minimize };
            _minimize.Click += (s, e) => HideToTray();
            _close = new GlyphButton { Glyph = Glyph.Close };
            _close.Click += (s, e) => HideToTray();

            Controls.AddRange(new Control[] { _activate, _extend, _deactivate, _simulate, _minimize, _close });

            _controller.StateChanged += OnControllerStateChanged;
            _controller.Expired += OnControllerExpired;
            _tray.ShowRequested += OnTrayShow;
            _tray.ExtendRequested += OnTrayExtend;
            _tray.DeactivateRequested += OnTrayDeactivate;
            _tray.ThemeRequested += OnTrayTheme;
            _tray.QuitRequested += OnTrayQuit;
            Theme.Changed += OnThemeChanged;

            _coreTimer.Tick += (s, e) => CoreTick();
            _uiTimer.Tick += (s, e) => UiTick();
            _animTimer.Tick += (s, e) => AnimTick();

            LoadLogos();
            LayoutControls();
            OnThemeChanged();
            OnStateChanged();
        }

        // ----- event handlers (named so they can be unsubscribed) ----------------------

        private void OnControllerStateChanged(object s, EventArgs e) => OnStateChanged();

        private void OnControllerExpired(object s, ExpiredEventArgs e) =>
            _tray.Toast("KeepAwake finished", "Kept awake for " + TimeFormat.Duration(e.KeptAwake));

        private void OnTrayShow(object s, EventArgs e) => ShowFromTray();
        private void OnTrayExtend(object s, DurationOption d) => Extend(d);
        private void OnTrayDeactivate(object s, EventArgs e) => _controller.Deactivate();

        private void OnTrayTheme(object s, ThemeMode m)
        {
            _settings.ThemeMode = m;
            SaveSettings();
            Theme.Apply(m);
        }

        private void OnTrayQuit(object s, EventArgs e) => Quit();

        // ----- lazy menus --------------------------------------------------------------

        private ContextMenuStrip DurationMenu
        {
            get
            {
                if (_durationMenu != null) return _durationMenu;
                _durationMenu = MenuRenderer.CreateMenu();
                foreach (var d in Durations.All)
                {
                    var opt = d;
                    var mi = new ToolStripMenuItem(opt.Label);
                    mi.Click += (s, e) => StartKeepAwake(opt);
                    _durationMenu.Items.Add(mi);
                }
                _durationMenu.Opening += (s, e) =>
                {
                    foreach (ToolStripMenuItem mi in _durationMenu.Items) mi.Checked = mi.Text == _chosen.Label;
                };
                return _durationMenu;
            }
        }

        private ContextMenuStrip ExtendMenu
        {
            get
            {
                if (_extendMenu != null) return _extendMenu;
                _extendMenu = MenuRenderer.CreateMenu();
                foreach (var d in Durations.Extend)
                {
                    var opt = d;
                    var mi = new ToolStripMenuItem(opt.Label);
                    mi.Click += (s, e) => Extend(opt);
                    _extendMenu.Items.Add(mi);
                }
                return _extendMenu;
            }
        }

        /// <summary>Builds the menus ahead of the first click. Called once the splash is up, off the start-up path.</summary>
        public void PrepareMenus()
        {
            if (IsDisposed) return;
            _tray.EnsureMenu();
            var a = DurationMenu;
            var b = ExtendMenu;
        }

        // ----- start-up ----------------------------------------------------------------

        /// <summary>
        /// Creates the window handle and runs every paint path once off-screen, so JIT and font realisation
        /// happen behind the splash and the window appears instantly when the splash closes.
        /// </summary>
        public void Prewarm()
        {
            var h = Handle;
            foreach (Control c in Controls) { var ch = c.Handle; }
            EnsureBgCache();
            using (var bmp = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                var r = ClientRectangle;
                OnPaintBackground(new PaintEventArgs(g, r));
                OnPaint(new PaintEventArgs(g, r));
                foreach (Control c in Controls)
                {
                    if (!c.Visible) continue;
                    using (var cb = new Bitmap(c.Width, c.Height, PixelFormat.Format32bppPArgb))
                        c.DrawToBitmap(cb, new Rectangle(Point.Empty, c.Size));
                }
            }
        }

        // ----- layout -----------------------------------------------------------------

        private static float S => Theme.Scale;
        private static int L(int logical) => (int)Math.Round(logical * S);

        private Rectangle HeaderRect => new Rectangle(0, 0, Width, L(HeaderY + HeaderH + 4));
        private Rectangle RingRect => new Rectangle(0, L(RingY) - 4, Width, L(RingH) + 8);
        private Rectangle CardRect => new Rectangle(L(Pad), L(CardY), L(W - 2 * Pad), L(CardH));

        private void LayoutControls()
        {
            ClientSize = new Size(L(W), L(H));
            _close.SetBounds(L(W - Pad - 32), L(HeaderY), L(32), L(HeaderH));
            _minimize.SetBounds(L(W - Pad - 32 - 32), L(HeaderY), L(32), L(HeaderH));
            _simulate.SetBounds(L(W - Pad - 40), L(ToggleY + 1), L(40), L(22));
            int actionY = H - 14 - FooterH - 10 - ActionH;
            _activate.SetBounds(L(Pad), L(actionY), L(W - 2 * Pad), L(ActionH));
            int deactW = 118;
            _extend.SetBounds(L(Pad), L(actionY), L(W - 2 * Pad - deactW - 10), L(ActionH));
            _deactivate.SetBounds(L(W - Pad - deactW), L(actionY), L(deactW), L(ActionH));
        }

        private void OnDpiChangedManually(int dpi, NativeMethods.RECT suggested)
        {
            if (dpi < 96) return;
            Theme.Dpi = dpi;
            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, suggested.Left, suggested.Top,
                suggested.Right - suggested.Left, suggested.Bottom - suggested.Top,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            DiscardBgCache();
            LoadLogos();
            LayoutControls();
            WindowChrome.ApplyRoundedCorners(this, 16);
            Invalidate(true);
        }

        private void LoadLogos()
        {
            _logoActive?.Dispose();
            _logoInactive?.Dispose();
            _logoActive = LoadResourceImage("tray_active.ico", L(18));
            _logoInactive = LoadResourceImage("tray_inactive.ico", L(18));
        }

        private static Image LoadResourceImage(string name, int px)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    if (s != null)
                        using (var ico = new Icon(s, 32, 32))
                        using (var src = ico.ToBitmap())
                            return new Bitmap(src, px, px);
            }
            catch { }
            return null;
        }

        // ----- window behaviour -------------------------------------------------------

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= WindowChrome.CS_DROPSHADOW; return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dpi = WindowChrome.DpiOf(this);
            if (dpi != Theme.Dpi)
            {
                Theme.Dpi = dpi;
                DiscardBgCache();
                LoadLogos();
                LayoutControls();
            }
            WindowChrome.ApplyRoundedCorners(this, 16);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                _ringKey = _cardKey = null;
                _arcPx = -1;
                _sinceText = null;
                UiTick();
                _uiTimer.Start();
            }
            else
            {
                _uiTimer.Stop();
                _animTimer.Stop();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        /// <summary>Runs on every real close (Quit, sign-out, shutdown, End task) so nothing fires into disposed objects.</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Shutdown();
            base.OnFormClosed(e);
        }

        private void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;
            _controller.StateChanged -= OnControllerStateChanged;
            _controller.Expired -= OnControllerExpired;
            _tray.ShowRequested -= OnTrayShow;
            _tray.ExtendRequested -= OnTrayExtend;
            _tray.DeactivateRequested -= OnTrayDeactivate;
            _tray.ThemeRequested -= OnTrayTheme;
            _tray.QuitRequested -= OnTrayQuit;
            Theme.Changed -= OnThemeChanged;
            _coreTimer.Stop();
            _uiTimer.Stop();
            _animTimer.Stop();
            _simulator.Stop();
            _controller.Deactivate();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112, SC_MINIMIZE = 0xF020;
            switch (m.Msg)
            {
                case WM_SYSCOMMAND when ((int)m.WParam & 0xFFF0) == SC_MINIMIZE:
                    HideToTray();
                    return;
                case NativeMethods.WM_DPICHANGED:
                    OnDpiChangedManually((int)(((long)m.WParam >> 16) & 0xFFFF), Marshal.PtrToStructure<NativeMethods.RECT>(m.LParam));
                    m.Result = IntPtr.Zero;
                    return;
                case NativeMethods.WM_SETTINGCHANGE:
                    // Windows broadcasts this for many unrelated changes; only a theme change matters here.
                    if (Theme.Mode == ThemeMode.System && m.LParam != IntPtr.Zero
                        && Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet")
                        Theme.Refresh();
                    break;
                case NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED:
                    Theme.Refresh();
                    break;
                case NativeMethods.WM_POWERBROADCAST:
                    if ((int)m.WParam == NativeMethods.PBT_APMRESUMEAUTOMATIC || (int)m.WParam == NativeMethods.PBT_APMRESUMESUSPEND)
                        CoreTick();
                    break;
                case NativeMethods.WM_TIMECHANGE:
                    _sinceText = null;
                    CoreTick();
                    break;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < L(HeaderY + HeaderH)) WindowChrome.BeginDrag(this);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var want = _githubRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (Cursor != want) Cursor = want;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && _githubRect.Contains(e.Location))
            {
                try { Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true }); } catch { }
            }
        }

        public void HideToTray() => Hide();

        public void ShowFromTray()
        {
            if (IsDisposed) return;
            if (!Visible)
            {
                if (!OnAnyScreen()) CenterToScreen();
                Show();
            }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            NativeMethods.SetForegroundWindow(Handle);
        }

        private bool OnAnyScreen()
        {
            foreach (var s in Screen.AllScreens)
                if (s.WorkingArea.IntersectsWith(Bounds)) return true;
            return false;
        }

        private void Quit()
        {
            _allowClose = true;
            Shutdown();
            Close();
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ShowMenu(ContextMenuStrip menu, Control anchor)
        {
            menu.Show(anchor, new Point(anchor.Width, -L(6)), ToolStripDropDownDirection.AboveLeft);
        }

        // ----- actions ----------------------------------------------------------------

        /// <summary>
        /// The action buttons swap places when the state changes, so the second click of a double-click would hit
        /// a different button. Clicks are ignored for one double-click interval after a state change.
        /// </summary>
        private bool ClickAllowed() => unchecked(Environment.TickCount - _ignoreClicksUntil) >= 0;

        private void StartKeepAwake(DurationOption d)
        {
            _chosen = d;
            _settings.LastDurationMinutes = d.Minutes;
            SaveSettings();
            _controller.Activate(d.Span);
        }

        private void Extend(DurationOption d) => _controller.Extend(d.Span);

        private void SaveSettings()
        {
            var json = _settings.ToJson();
            if (json == _lastSavedJson) return;
            try
            {
                _settings.Save(_settingsPath);
                _lastSavedJson = json;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                // Read-only location: move to the per-user folder and write the current values there.
                var fallback = SettingsStore.FallbackPath();
                if (string.Equals(fallback, _settingsPath, StringComparison.OrdinalIgnoreCase)) return;
                _settingsPath = fallback;
                try { _settings.Save(_settingsPath); _lastSavedJson = json; } catch { }
            }
            catch { /* keep running with in-memory settings */ }
        }

        private void SyncSimulator()
        {
            if (_controller.IsActive && _settings.SimulateActivity) _simulator.Start();
            else _simulator.Stop();
        }

        private void OnStateChanged()
        {
            bool active = _controller.IsActive;
            _ignoreClicksUntil = Environment.TickCount + SystemInformation.DoubleClickTime;
            _activate.Visible = !active;
            _extend.Visible = active;
            _deactivate.Visible = active;
            _extend.Enabled = !_controller.IsForever;
            _activate.Text = Durations.ActivateLabel(_chosen);
            if (!active) _shownProgress = 0;
            SyncSimulator();
            _tray.Update(_controller, _settings.ThemeMode);
            _trayClock.Restart();
            if (Visible && !_animTimer.Enabled) _animTimer.Start();
            ScheduleCore();
            Invalidate();
        }

        private void OnThemeChanged()
        {
            BackColor = Theme.Current.Bg;
            DiscardBgCache();
            _tray.Update(_controller, _settings.ThemeMode);
            Invalidate(true);
        }

        // ----- timers -----------------------------------------------------------------

        /// <summary>Sets the core timer to fire only when something must happen: the expiry or the next tooltip refresh.</summary>
        private void ScheduleCore()
        {
            if (_shutDown) return;
            int wait = Schedule.NextWakeMs(_controller.IsActive, _controller.UtcNow, _controller.EndUtc, _trayClock.ElapsedMilliseconds);
            if (wait < 0)
            {
                _coreTimer.Stop();
                return;
            }
            _coreTimer.Interval = Math.Min(Schedule.TrayRefreshMs, wait + 20);
            if (!_coreTimer.Enabled) _coreTimer.Start();
        }

        private void CoreTick()
        {
            if (_shutDown) return;
            try
            {
                _controller.Tick();
                if (_trayClock.ElapsedMilliseconds >= Schedule.TrayRefreshMs - 100)
                {
                    _tray.Update(_controller, _settings.ThemeMode);
                    _trayClock.Restart();
                }
            }
            catch (Exception ex) { Trace.WriteLine(ex); }
            ScheduleCore();
        }

        private string RingText()
        {
            TimeFormat.RingParts(_controller.Elapsed, out var hh, out var mm);
            return hh + ":" + mm;
        }

        private string SubText()
        {
            if (_controller.HoldFailed) return "Windows refused the request";
            if (_controller.IsForever) return "until deactivated";
            var rem = _controller.Remaining.Value;
            return TimeFormat.Remaining(rem) + "  ·  ends " + TimeFormat.Clock(DateTime.Now + rem);
        }

        private static string UptimeText() => TimeFormat.Compact(Uptime.Current);

        private string SinceText() =>
            _sinceText ?? (_sinceText = "since " + TimeFormat.Since(Uptime.BootTimeLocal(SystemClock.Instance), DateTime.Now));

        /// <summary>Once a second: repaint only the parts whose text or arc position actually changed.</summary>
        private void UiTick()
        {
            try
            {
                bool active = _controller.IsActive;
                var ringKey = active ? RingText() + "|" + SubText() : "inactive";
                var cardKey = UptimeText();
                int arcPx = (int)(_controller.Progress * Math.PI * L(RingD - RingStroke));
                bool animating = Math.Abs(_controller.Progress - _shownProgress) > 0.0005;

                if (animating && arcPx != _arcPx && !_animTimer.Enabled) _animTimer.Start();
                if (ringKey != _ringKey)
                {
                    _ringKey = ringKey;
                    Invalidate(RingRect);
                }
                _arcPx = arcPx;
                if (cardKey != _cardKey)
                {
                    _cardKey = cardKey;
                    Invalidate(CardRect);
                }
            }
            catch (Exception ex) { Trace.WriteLine(ex); }
        }

        private void AnimTick()
        {
            try
            {
                double target = _controller.Progress;
                _shownProgress += (target - _shownProgress) * 0.18;
                if (Math.Abs(target - _shownProgress) < 0.0005) { _shownProgress = target; _animTimer.Stop(); }
                Invalidate(RingRect);
            }
            catch (Exception ex) { Trace.WriteLine(ex); }
        }

        // ----- painting ---------------------------------------------------------------

        private void DiscardBgCache()
        {
            _bgCache?.Dispose();
            _bgCache = null;
        }

        private void EnsureBgCache()
        {
            var size = ClientSize;
            if (_bgCache != null && _bgCache.Size == size && ReferenceEquals(_bgPalette, Theme.Current) && _bgDpi == Theme.Dpi) return;
            DiscardBgCache();
            _bgPalette = Theme.Current;
            _bgDpi = Theme.Dpi;
            _bgCache = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(_bgCache)) RenderStatic(g, _bgPalette, size);
        }

        /// <summary>Everything that never changes while the palette, size and DPI stay the same.</summary>
        private void RenderStatic(Graphics g, Palette p, Size size)
        {
            float s = S;
            g.Clear(p.Bg);
            Painter.Setup(g);
            Painter.TopGlow(g, new RectangleF(0, 0, size.Width, size.Height), p.Glow, p.Bg);

            // Header title.
            Painter.Text(g, "KeepAwake", Theme.Font(9.75f, FontStyle.Bold), p.Text, new RectangleF(L(Pad + 26), L(HeaderY), L(120), L(HeaderH)));

            // Ring track.
            float d = L(RingD), stroke = RingStroke * s;
            float cx = size.Width / 2f, cy = L(RingY) + L(RingH) / 2f;
            var ring = new RectangleF(cx - d / 2 + stroke / 2, cy - d / 2 + stroke / 2, d - stroke, d - stroke);
            using (var pen = new Pen(p.Track, stroke)) g.DrawEllipse(pen, ring);

            // Uptime card frame and label.
            var r = new RectangleF(L(Pad) + 0.5f, L(CardY) + 0.5f, L(W - 2 * Pad) - 1, L(CardH) - 1);
            using (var b = new SolidBrush(p.Card)) Painter.FillRoundRect(g, b, r, 12 * s);
            using (var pen = new Pen(p.CardBorder)) Painter.DrawRoundRect(g, pen, r, 12 * s);
            Painter.Text(g, "SYSTEM UP", Theme.Font(7.75f), p.Muted, new RectangleF(r.X + L(14), r.Y + L(9), L(160), L(14)));

            // Toggle label.
            var labelFont = Theme.Font(9.75f);
            Painter.Text(g, "Simulate activity", labelFont, Painter.Mix(p.Text, p.Muted, 0.25f),
                new RectangleF(L(Pad + 4), L(ToggleY), L(130), L(ToggleH)));
            var labelW = Painter.Measure(g, "Simulate activity", labelFont).Width;
            Painter.Text(g, "(Teams, Slack)", labelFont, p.Muted, new RectangleF(L(Pad + 4) + labelW + L(4), L(ToggleY), L(120), L(ToggleH)));

            // Footer.
            float fy = L(H - 14 - FooterH);
            Painter.Text(g, "v" + Version + "  ·  by Karthik", Theme.Font(8f), p.Faint, new RectangleF(L(Pad), fy, L(200), L(FooterH)));
            var linkFont = Theme.Font(8f);
            float linkW = Painter.Measure(g, "GitHub", linkFont).Width;
            // DrawString pads the text, so the box is wider than the typographic measure.
            float linkBox = linkW + L(20);
            var linkRect = new RectangleF(L(W - Pad) + L(2) - linkBox, fy, linkBox, L(FooterH));
            Painter.Text(g, "GitHub", linkFont, p.Muted, linkRect, StringAlignment.Far, trim: false);
            _githubRect = Rectangle.Round(new RectangleF(L(W - Pad) - linkW - L(4), fy, linkW + L(8), L(FooterH)));

            // Window border.
            using (var pen = new Pen(p.CardBorder))
                Painter.DrawRoundRect(g, pen, new RectangleF(0.5f, 0.5f, size.Width - 1, size.Height - 1), WindowChrome.CornerRadius * s);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            EnsureBgCache();
            var g = e.Graphics;
            var clip = Rectangle.Intersect(e.ClipRectangle, new Rectangle(Point.Empty, _bgCache.Size));
            if (clip.IsEmpty) return;
            var mode = g.CompositingMode;
            var interp = g.InterpolationMode;
            var offset = g.PixelOffsetMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_bgCache, clip, clip, GraphicsUnit.Pixel);
            g.CompositingMode = mode;
            g.InterpolationMode = interp;
            g.PixelOffsetMode = offset;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var clip = e.ClipRectangle;
            var p = Theme.Current;
            float s = S;
            bool active = _controller.IsActive;
            Painter.Setup(g);

            if (clip.IntersectsWith(HeaderRect)) PaintHeader(g, p, active);
            if (clip.IntersectsWith(RingRect)) PaintRing(g, p, s, active);
            if (clip.IntersectsWith(CardRect)) PaintUptimeCard(g, p, active);
        }

        private void PaintHeader(Graphics g, Palette p, bool active)
        {
            var logo = active ? _logoActive : _logoInactive;
            float y = L(HeaderY) + (L(HeaderH) - L(18)) / 2f;
            if (logo != null)
            {
                // The logo is already the right size; resampling it again only blurs it.
                var mode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(logo, L(Pad), y, L(18), L(18));
                g.InterpolationMode = mode;
            }
            if (active)
            {
                var font = Theme.Font(7.5f, FontStyle.Bold);
                var size = Painter.Measure(g, "ACTIVE", font);
                float w = size.Width + L(18), h = L(20);
                var r = new RectangleF(_minimize.Left - L(10) - w, L(HeaderY) + (L(HeaderH) - h) / 2f, w, h);
                using (var b = new SolidBrush(p.SuccessBg)) Painter.FillRoundRect(g, b, r, h / 2);
                using (var pen = new Pen(p.SuccessBorder)) Painter.DrawRoundRect(g, pen, r, h / 2);
                Painter.Text(g, "ACTIVE", font, p.Success, r, StringAlignment.Center);
            }
        }

        private void PaintRing(Graphics g, Palette p, float s, bool active)
        {
            float d = L(RingD), stroke = RingStroke * s;
            float cx = Width / 2f, cy = L(RingY) + L(RingH) / 2f;
            var ring = new RectangleF(cx - d / 2 + stroke / 2, cy - d / 2 + stroke / 2, d - stroke, d - stroke);

            float sweep = (float)(360 * _shownProgress);
            if (active && sweep > 0.5f)
            {
                var gradRect = new RectangleF(ring.X - stroke, ring.Y - stroke, ring.Width + 2 * stroke, ring.Height + 2 * stroke);
                if (p.IsDark)
                    using (var glow = new Pen(Painter.Alpha(p.Accent, 60), stroke * 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(glow, ring, -90, sweep);
                using (var lg = new LinearGradientBrush(gradRect, p.Accent, p.Accent2, 45f))
                using (var pen = new Pen(lg, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, ring, -90, sweep);
            }

            var centre = new RectangleF(cx - d / 2, cy - d / 2, d, d);
            if (!active)
            {
                Painter.Text(g, "Inactive", Theme.Font(19f, FontStyle.Bold), Painter.Mix(p.Text, p.Muted, 0.25f),
                    new RectangleF(centre.X, cy - L(26), d, L(34)), StringAlignment.Center);
                Painter.Text(g, "Windows may sleep as usual", Theme.Font(8.75f), p.Muted,
                    new RectangleF(centre.X, cy + L(8), d, L(18)), StringAlignment.Center);
                return;
            }

            TimeFormat.RingParts(_controller.Elapsed, out var hh, out var mm);
            Painter.Text(g, "KEPT AWAKE", Theme.Font(7.75f), p.Muted, new RectangleF(centre.X, cy - L(44), d, L(16)), StringAlignment.Center);
            var big = Theme.Font(27f, FontStyle.Bold);
            var unit = Theme.Font(11.5f, FontStyle.Bold);
            float wh = Painter.Measure(g, hh, big).Width, wu1 = Painter.Measure(g, "h ", unit).Width;
            float wm = Painter.Measure(g, mm, big).Width, wu2 = Painter.Measure(g, "m", unit).Width;
            float total = wh + wu1 + wm + wu2, x = cx - total / 2, baseY = cy - L(24), lineH = L(44), slack = L(16);
            Painter.Text(g, hh, big, p.Text, new RectangleF(x, baseY, wh + slack, lineH), trim: false);
            x += wh;
            Painter.Text(g, "h ", unit, p.Muted, new RectangleF(x, baseY + L(4), wu1 + slack, lineH), trim: false);
            x += wu1;
            Painter.Text(g, mm, big, p.Text, new RectangleF(x, baseY, wm + slack, lineH), trim: false);
            x += wm;
            Painter.Text(g, "m", unit, p.Muted, new RectangleF(x, baseY + L(4), wu2 + slack, lineH), trim: false);

            Painter.Text(g, SubText(), Theme.Font(8.75f), Painter.Mix(p.Text, p.Muted, 0.35f),
                new RectangleF(centre.X, cy + L(22), d, L(18)), StringAlignment.Center);
        }

        private void PaintUptimeCard(Graphics g, Palette p, bool active)
        {
            float x = CardRect.X + 0.5f + L(14), y = CardRect.Y + 0.5f;
            Painter.Text(g, UptimeText(), Theme.Font(11.5f, FontStyle.Bold), p.Text, new RectangleF(x, y + L(24), L(200), L(22)));
            string right = active ? "screen stays on" : SinceText();
            Painter.Text(g, right, Theme.Font(8.25f), p.Muted, new RectangleF(CardRect.Right - 0.5f - L(170), y, L(156), CardRect.Height - 1), StringAlignment.Far);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Shutdown();
                _coreTimer.Dispose();
                _uiTimer.Dispose();
                _animTimer.Dispose();
                _durationMenu?.Dispose();
                _extendMenu?.Dispose();
                _logoActive?.Dispose();
                _logoInactive?.Dispose();
                _bgCache?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
