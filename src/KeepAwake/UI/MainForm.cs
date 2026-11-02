using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
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

        private readonly AwakeController _controller;
        private readonly ActivitySimulator _simulator;
        private readonly Settings _settings;
        private readonly string _settingsPath;
        private readonly TrayIcon _tray;
        private readonly Timer _coreTimer = new Timer { Interval = 1000 };   // always on: expiry + tray tooltip
        private readonly Timer _uiTimer = new Timer { Interval = 1000 };     // only while visible: repaint
        private readonly Timer _animTimer = new Timer { Interval = 16 };
        private readonly Stopwatch _trayClock = Stopwatch.StartNew();

        private readonly PillButton _activate, _extend, _deactivate;
        private readonly ToggleSwitch _simulate;
        private readonly GlyphButton _minimize, _close;
        private readonly ContextMenuStrip _durationMenu, _extendMenu;
        private Image _logoActive, _logoInactive;

        private DurationOption _chosen;
        private double _shownProgress;
        private bool _allowClose;

        public string Version { get; }

        public MainForm(AwakeController controller, ActivitySimulator simulator, Settings settings, string settingsPath, TrayIcon tray)
        {
            _controller = controller;
            _simulator = simulator;
            _settings = settings;
            _settingsPath = settingsPath;
            _tray = tray;
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
            ClientSize = new Size(W, H);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _activate = new PillButton { Kind = PillKind.Primary, SplitChevron = true };
            _activate.Click += (s, e) => StartKeepAwake(_chosen);
            _activate.ChevronClick += (s, e) => ShowMenu(_durationMenu, _activate);

            _extend = new PillButton { Kind = PillKind.Secondary, SplitChevron = true, ChevronWidth = 40, Text = "Extend 1 h", Visible = false };
            _extend.Click += (s, e) => Extend(Durations.Extend[0]);
            _extend.ChevronClick += (s, e) => ShowMenu(_extendMenu, _extend);

            _deactivate = new PillButton { Kind = PillKind.Danger, Text = "Deactivate", Visible = false };
            _deactivate.Click += (s, e) => _controller.Deactivate();

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
            _extendMenu = MenuRenderer.CreateMenu();
            foreach (var d in Durations.Extend)
            {
                var opt = d;
                var mi = new ToolStripMenuItem(opt.Label);
                mi.Click += (s, e) => Extend(opt);
                _extendMenu.Items.Add(mi);
            }

            _controller.StateChanged += (s, e) => OnStateChanged();
            _controller.Expired += (s, e) => _tray.Toast("KeepAwake finished", "Kept awake for " + TimeFormat.Compact(e.KeptAwake) + ".");
            _tray.ShowRequested += (s, e) => ShowFromTray();
            _tray.ExtendRequested += (s, d) => Extend(d);
            _tray.DeactivateRequested += (s, e) => _controller.Deactivate();
            _tray.ThemeRequested += (s, m) => { _settings.ThemeMode = m; SaveSettings(); Theme.Apply(m); };
            _tray.QuitRequested += (s, e) => Quit();
            Theme.Changed += OnThemeChanged;

            _coreTimer.Tick += (s, e) => CoreTick();
            _uiTimer.Tick += (s, e) => UiTick();
            _animTimer.Tick += (s, e) => AnimTick();

            LoadLogos();
            LayoutControls();
            OnThemeChanged();
            OnStateChanged();
            _coreTimer.Start();
        }

        // ----- layout -----------------------------------------------------------------

        private float S => DeviceDpi / 96f;
        private int L(int logical) => (int)Math.Round(logical * S);

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

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            LoadLogos();
            LayoutControls();
            WindowChrome.ApplyRoundedCorners(this, 16);
            Invalidate();
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
            WindowChrome.ApplyRoundedCorners(this, 16);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { UiTick(); _uiTimer.Start(); }
            else { _uiTimer.Stop(); _animTimer.Stop(); }
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

        protected override void WndProc(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112, SC_MINIMIZE = 0xF020;
            if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_MINIMIZE) { HideToTray(); return; }
            if (m.Msg == NativeMethods.WM_SETTINGCHANGE || m.Msg == NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED)
            {
                if (Theme.Mode == ThemeMode.System || m.Msg == NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED) Theme.Refresh();
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < L(HeaderY + HeaderH)) WindowChrome.BeginDrag(this);
        }

        public void HideToTray() => Hide();

        public void ShowFromTray()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            NativeMethods.SetForegroundWindow(Handle);
        }

        private void Quit()
        {
            _allowClose = true;
            _coreTimer.Stop();
            _simulator.Stop();
            _controller.Deactivate();
            Close();
        }

        private void ShowMenu(ContextMenuStrip menu, Control anchor)
        {
            menu.Show(anchor, new Point(anchor.Width, -L(6)), ToolStripDropDownDirection.AboveLeft);
        }

        // ----- actions ----------------------------------------------------------------

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
            try { _settings.Save(_settingsPath); } catch { /* read-only media: keep running with in-memory settings */ }
        }

        private void SyncSimulator()
        {
            if (_controller.IsActive && _settings.SimulateActivity) _simulator.Start();
            else _simulator.Stop();
        }

        private void OnStateChanged()
        {
            bool active = _controller.IsActive;
            _activate.Visible = !active;
            _extend.Visible = active;
            _deactivate.Visible = active;
            _extend.Enabled = !_controller.IsForever;
            _activate.Text = Durations.ActivateLabel(_chosen);
            SyncSimulator();
            _tray.Update(_controller, _settings.ThemeMode);
            _trayClock.Restart();
            if (Visible && !_animTimer.Enabled) _animTimer.Start();
            Invalidate();
        }

        private void OnThemeChanged()
        {
            BackColor = Theme.Current.Bg;
            _tray.Update(_controller, _settings.ThemeMode);
            Invalidate(true);
        }

        private void CoreTick()
        {
            _controller.Tick();
            if (_trayClock.ElapsedMilliseconds >= 60000)
            {
                _tray.Update(_controller, _settings.ThemeMode);
                _trayClock.Restart();
            }
        }

        private void UiTick()
        {
            if (Math.Abs(_controller.Progress - _shownProgress) > 0.0005 && !_animTimer.Enabled) _animTimer.Start();
            Invalidate(new Rectangle(0, 0, Width, L(ToggleY)));
        }

        private void AnimTick()
        {
            double target = _controller.Progress;
            _shownProgress += (target - _shownProgress) * 0.18;
            if (Math.Abs(target - _shownProgress) < 0.0005) { _shownProgress = target; _animTimer.Stop(); }
            Invalidate(new Rectangle(0, L(RingY) - 4, Width, L(RingH) + 8));
        }

        // ----- painting ---------------------------------------------------------------

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.Current;
            g.Clear(p.Bg);
            Painter.Setup(g);
            Painter.TopGlow(g, ClientRectangle, p.Glow, p.Bg);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Painter.Setup(g);
            var p = Theme.Current;
            float s = S;
            bool active = _controller.IsActive;

            PaintHeader(g, p, s, active);
            PaintRing(g, p, s, active);
            PaintUptimeCard(g, p, s, active);
            Painter.Text(g, "Simulate activity", Theme.Font(9.75f), Painter.Mix(p.Text, p.Muted, 0.25f),
                new RectangleF(L(Pad + 4), L(ToggleY), L(130), L(ToggleH)));
            var labelW = Painter.Measure(g, "Simulate activity", Theme.Font(9.75f)).Width;
            Painter.Text(g, "(Teams, Slack)", Theme.Font(9.75f), p.Muted, new RectangleF(L(Pad + 4) + labelW + L(4), L(ToggleY), L(120), L(ToggleH)));
            PaintFooter(g, p, s);
            using (var pen = new Pen(p.CardBorder))
                Painter.DrawRoundRect(g, pen, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 16 * s);
        }

        private void PaintHeader(Graphics g, Palette p, float s, bool active)
        {
            var logo = active ? _logoActive : _logoInactive;
            float y = L(HeaderY) + (L(HeaderH) - L(18)) / 2f;
            if (logo != null) g.DrawImage(logo, L(Pad), y, L(18), L(18));
            Painter.Text(g, "KeepAwake", Theme.Font(9.75f, FontStyle.Bold), p.Text, new RectangleF(L(Pad + 26), L(HeaderY), L(120), L(HeaderH)));
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
            using (var pen = new Pen(p.Track, stroke)) g.DrawEllipse(pen, ring);

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

            string sub = _controller.IsForever
                ? "until deactivated"
                : TimeFormat.Compact(_controller.Remaining.Value) + " left  ·  ends " + TimeFormat.Clock(_controller.EndUtc.Value.ToLocalTime());
            Painter.Text(g, sub, Theme.Font(8.75f), Painter.Mix(p.Text, p.Muted, 0.35f),
                new RectangleF(centre.X, cy + L(22), d, L(18)), StringAlignment.Center);
        }

        private void PaintUptimeCard(Graphics g, Palette p, float s, bool active)
        {
            var r = new RectangleF(L(Pad) + 0.5f, L(CardY) + 0.5f, L(W - 2 * Pad) - 1, L(CardH) - 1);
            using (var b = new SolidBrush(p.Card)) Painter.FillRoundRect(g, b, r, 12 * s);
            using (var pen = new Pen(p.CardBorder)) Painter.DrawRoundRect(g, pen, r, 12 * s);
            float x = r.X + L(14);
            Painter.Text(g, "SYSTEM UP", Theme.Font(7.75f), p.Muted, new RectangleF(x, r.Y + L(9), L(160), L(14)));
            Painter.Text(g, TimeFormat.Compact(Uptime.Current), Theme.Font(11.5f, FontStyle.Bold), p.Text, new RectangleF(x, r.Y + L(24), L(200), L(22)));
            string right = active ? "screen stays on" : "since " + TimeFormat.Since(Uptime.BootTimeLocal(SystemClock.Instance), DateTime.Now);
            Painter.Text(g, right, Theme.Font(8.25f), p.Muted, new RectangleF(r.Right - L(170), r.Y, L(156), r.Height), StringAlignment.Far);
        }

        private void PaintFooter(Graphics g, Palette p, float s)
        {
            float y = L(H - 14 - FooterH);
            Painter.Text(g, "v" + Version + "  ·  by Karthik", Theme.Font(8f), p.Faint, new RectangleF(L(Pad), y, L(200), L(FooterH)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Theme.Changed -= OnThemeChanged;
                _coreTimer.Dispose();
                _uiTimer.Dispose();
                _animTimer.Dispose();
                _durationMenu.Dispose();
                _extendMenu.Dispose();
                _logoActive?.Dispose();
                _logoInactive?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
