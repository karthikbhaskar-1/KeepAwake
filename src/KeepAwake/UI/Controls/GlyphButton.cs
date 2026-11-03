using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeepAwake.UI.Controls
{
    public enum Glyph { Minimize, Close }

    /// <summary>Header glyph button (minimize / close), drawn as thin strokes with a soft hover pill.</summary>
    public sealed class GlyphButton : Control
    {
        private bool _hover, _down;

        public GlyphButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            BackColor = Color.Transparent;
            TabStop = false;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public Glyph Glyph
        {
            get => _glyph;
            set { _glyph = value; AccessibleName = value == Glyph.Close ? "Close" : "Minimize"; }
        }

        private Glyph _glyph;

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Painter.Setup(g);
            var p = Theme.Current;
            float s = Theme.Scale;
            if (_hover)
            {
                var bg = Glyph == Glyph.Close ? Painter.Alpha(p.Danger, _down ? 70 : 45) : Painter.Alpha(p.Text, _down ? 30 : 18);
                using (var b = new SolidBrush(bg)) Painter.FillRoundRect(g, b, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * s);
            }
            var color = _hover && Glyph == Glyph.Close ? p.Text : p.Muted;
            float cx = Width / 2f, cy = Height / 2f, a = 4.5f * s;
            using (var pen = new Pen(color, 1.4f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (Glyph == Glyph.Minimize)
                    g.DrawLine(pen, cx - a, cy, cx + a, cy);
                else
                {
                    g.DrawLine(pen, cx - a, cy - a, cx + a, cy + a);
                    g.DrawLine(pen, cx - a, cy + a, cx + a, cy - a);
                }
            }
        }
    }
}
