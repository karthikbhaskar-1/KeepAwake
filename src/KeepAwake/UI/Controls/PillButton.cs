using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeepAwake.UI.Controls
{
    public enum PillKind { Primary, Secondary, Danger }

    /// <summary>Rounded, owner-drawn button with an optional split chevron part.</summary>
    public sealed class PillButton : Control
    {
        private bool _hover, _down, _hoverChevron;

        public PillButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public PillKind Kind { get; set; } = PillKind.Primary;
        public bool SplitChevron { get; set; }
        public int ChevronWidth { get; set; } = 46;
        public float Radius { get; set; } = 12f;
        public event EventHandler ChevronClick;

        private Rectangle ChevronRect => SplitChevron ? new Rectangle(Width - Scale(ChevronWidth), 0, Scale(ChevronWidth), Height) : Rectangle.Empty;
        private int Scale(int v) => (int)Math.Round(v * DeviceDpi / 96f);

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _hoverChevron = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hc = ChevronRect.Contains(e.Location);
            if (hc != _hoverChevron) { _hoverChevron = hc; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            // Control raises Click after this; remember whether it belongs to the chevron part.
            _chevronPending = e.Button == MouseButtons.Left && ChevronRect.Contains(e.Location);
            Invalidate();
            base.OnMouseUp(e);
            _chevronPending = false;
        }

        private bool _chevronPending;

        protected override void OnClick(EventArgs e)
        {
            if (_chevronPending) { _chevronPending = false; ChevronClick?.Invoke(this, EventArgs.Empty); return; }
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { base.OnClick(EventArgs.Empty); e.Handled = true; }
            else if (e.KeyCode == Keys.Down && SplitChevron) { ChevronClick?.Invoke(this, EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Painter.Setup(g);
            var p = Theme.Current;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = Radius * DeviceDpi / 96f;
            float press = _down ? 0.06f : 0f;

            if (Kind == PillKind.Primary)
            {
                using (var path = Painter.RoundRect(r, rad))
                {
                    var c1 = Painter.Mix(p.Accent, Color.White, _hover ? 0.08f : 0f);
                    var c2 = Painter.Mix(p.Accent2, Color.White, _hover ? 0.08f : 0f);
                    if (_down) { c1 = Painter.Mix(c1, Color.Black, press); c2 = Painter.Mix(c2, Color.Black, press); }
                    using (var lg = new LinearGradientBrush(r, c1, c2, LinearGradientMode.Horizontal)) g.FillPath(lg, path);
                }
                if (SplitChevron)
                {
                    var cr = ChevronRect;
                    using (var pen = new Pen(Painter.Alpha(p.OnAccent, 70), 1f))
                        g.DrawLine(pen, cr.Left + 0.5f, r.Y + rad * 0.6f, cr.Left + 0.5f, r.Bottom - rad * 0.6f);
                    if (_hoverChevron)
                        using (var b = new SolidBrush(Painter.Alpha(Color.White, 28)))
                        using (var cp = Painter.RoundRect(new RectangleF(cr.X, r.Y, cr.Width - 0.5f, r.Height), 0, rad, rad, 0))
                            g.FillPath(b, cp);
                }
                DrawContent(g, p.OnAccent, p.OnAccent);
            }
            else
            {
                var fill = Kind == PillKind.Danger ? p.DangerBg : (_hover ? p.ButtonHover : p.Button);
                var border = Kind == PillKind.Danger ? p.DangerBorder : p.ButtonBorder;
                var text = Kind == PillKind.Danger ? p.Danger : p.Text;
                if (_down) fill = Painter.Mix(fill, p.IsDark ? Color.Black : Color.Gray, 0.1f);
                using (var b = new SolidBrush(fill)) Painter.FillRoundRect(g, b, r, rad);
                if (Kind == PillKind.Danger && _hover)
                    using (var b = new SolidBrush(Painter.Alpha(p.Danger, 22))) Painter.FillRoundRect(g, b, r, rad);
                using (var pen = new Pen(border)) Painter.DrawRoundRect(g, pen, r, rad);
                if (SplitChevron)
                {
                    var cr = ChevronRect;
                    using (var pen = new Pen(border)) g.DrawLine(pen, cr.Left + 0.5f, r.Y + 1, cr.Left + 0.5f, r.Bottom - 1);
                    if (_hoverChevron)
                        using (var b = new SolidBrush(Painter.Alpha(p.Text, 14)))
                        using (var cp = Painter.RoundRect(new RectangleF(cr.X + 1, r.Y + 1, cr.Width - 2, r.Height - 2), 0, rad - 1, rad - 1, 0))
                            g.FillPath(b, cp);
                }
                DrawContent(g, text, Kind == PillKind.Danger ? p.Danger : p.Muted);
            }

            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Painter.Alpha(p.Text, 90)) { DashStyle = DashStyle.Dot })
                    Painter.DrawRoundRect(g, pen, new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4), Math.Max(2, rad - 2));
        }

        private void DrawContent(Graphics g, Color text, Color chevron)
        {
            float s = DeviceDpi / 96f;
            var textRect = new RectangleF(0, 0, SplitChevron ? Width - ChevronRect.Width : Width, Height);
            var font = Theme.Font(Kind == PillKind.Primary ? 11f : 10.5f, FontStyle.Bold);
            Painter.Text(g, Text, font, text, textRect, StringAlignment.Center);
            if (SplitChevron)
            {
                var cr = ChevronRect;
                float cx = cr.X + cr.Width / 2f, cy = Height / 2f;
                using (var pen = new Pen(chevron, 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, cx - 4.5f * s, cy - 2 * s, cx, cy + 2.5f * s);
                    g.DrawLine(pen, cx, cy + 2.5f * s, cx + 4.5f * s, cy - 2 * s);
                }
            }
        }
    }
}
