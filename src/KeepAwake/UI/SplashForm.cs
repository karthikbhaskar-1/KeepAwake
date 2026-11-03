using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using KeepAwake.Native;

namespace KeepAwake.UI
{
    /// <summary>360 x 240 splash: fades in, holds, fades out. Click anywhere to skip.</summary>
    public sealed class SplashForm : Form
    {
        private const int TotalMs = 2000, FadeMs = 150;
        private const double MaxOpacity = 0.99;   // exactly 1.0 would drop the layered window style and cause an extra repaint
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly Timer _timer = new Timer { Interval = 15 };
        private readonly Image _logo;
        private bool _skipped;
        private double _skipOffset;

        public SplashForm()
        {
            Text = "KeepAwake Splash";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            float scale = Theme.Scale;
            ClientSize = new Size((int)Math.Round(360 * scale), (int)Math.Round(240 * scale));
            Opacity = 0;
            BackColor = Theme.Current.Bg;
            Cursor = Cursors.Hand;
            _logo = LoadLogo(64);
            _timer.Tick += (s, e) => Step();
            Click += (s, e) =>
            {
                _skipped = true;
                if (_timer.Enabled) _timer.Interval = 1;
            };
        }

        public string Version { get; set; } = "1.0.0";

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            WindowChrome.ApplyRoundedCorners(this, 16);
        }

        /// <summary>
        /// Starts the fade-in, hold and fade-out. Called once the main window is ready, so the 2 s are
        /// measured from here and the fade is not eaten by start-up work.
        /// </summary>
        public void StartTimeline()
        {
            _clock.Restart();
            _skipOffset = 0;
            _timer.Interval = 15;
            _timer.Start();
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= WindowChrome.CS_DROPSHADOW; return cp; }
        }

        private void Step()
        {
            double t = _clock.ElapsedMilliseconds + _skipOffset;
            if (_skipped)
            {
                _skipped = false;
                if (t < TotalMs - FadeMs) { _skipOffset += (TotalMs - FadeMs) - t; t = TotalMs - FadeMs; }
            }
            if (t < FadeMs)
            {
                Opacity = Math.Min(MaxOpacity, t / FadeMs);
            }
            else if (t < TotalMs - FadeMs)
            {
                // Nothing changes during the hold: sleep until it ends instead of ticking every 15 ms.
                if (Opacity != MaxOpacity) Opacity = MaxOpacity;
                _timer.Interval = Math.Max(15, (int)(TotalMs - FadeMs - t));
            }
            else if (t < TotalMs)
            {
                if (_timer.Interval != 15) _timer.Interval = 15;
                Opacity = Math.Min(MaxOpacity, (TotalMs - t) / FadeMs);
            }
            else { _timer.Stop(); Close(); }
        }

        private static Image LoadLogo(int px)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (s != null) using (var ico = new Icon(s, px, px)) return ico.ToBitmap();
            }
            catch { }
            return null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Painter.Setup(g);
            var p = Theme.Current;
            float s = Theme.Scale;
            var r = ClientRectangle;
            g.Clear(p.Bg);
            Painter.TopGlow(g, r, p.Glow, p.Bg);

            float cx = r.Width / 2f;
            float y = 44 * s;
            if (_logo != null)
            {
                float size = 64 * s;
                using (var glow = new GraphicsPath())
                {
                    glow.AddEllipse(cx - size, y - size * 0.4f, size * 2, size * 2);
                    using (var pgb = new PathGradientBrush(glow) { CenterColor = Painter.Alpha(p.Accent, p.IsDark ? 110 : 60), SurroundColors = new[] { Color.Transparent } })
                        g.FillPath(pgb, glow);
                }
                g.DrawImage(_logo, cx - size / 2, y, size, size);
                y += size + 10 * s;
            }
            Painter.Text(g, "KeepAwake", Theme.Font(24f, FontStyle.Bold), p.Text, new RectangleF(0, y, r.Width, 40 * s), StringAlignment.Center);
            y += 40 * s;
            Painter.Text(g, "VERSION " + Version, Theme.Font(8.5f), p.Muted, new RectangleF(0, y, r.Width, 18 * s), StringAlignment.Center);
            y += 24 * s;
            Painter.Text(g, "by Karthik", Theme.Font(10.5f), Painter.Mix(p.Text, p.Muted, 0.3f), new RectangleF(0, y, r.Width, 20 * s), StringAlignment.Center);

            using (var pen = new Pen(p.CardBorder))
                Painter.DrawRoundRect(g, pen, new RectangleF(0.5f, 0.5f, r.Width - 1, r.Height - 1), WindowChrome.CornerRadius * s);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _logo?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
