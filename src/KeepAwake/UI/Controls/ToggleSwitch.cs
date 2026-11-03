using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeepAwake.UI.Controls
{
    /// <summary>40 x 22 owner-drawn switch with a short knob animation.</summary>
    public sealed class ToggleSwitch : Control
    {
        private bool _checked, _hover;
        private float _pos; // 0 = off, 1 = on
        private readonly Timer _anim = new Timer { Interval = 15 };

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            BackColor = Color.Transparent;
            AccessibleRole = AccessibleRole.CheckButton;
            AccessibleName = "Simulate activity";
            Cursor = Cursors.Hand;
            TabStop = true;
            _anim.Tick += (s, e) =>
            {
                float target = _checked ? 1 : 0;
                _pos += (target - _pos) * 0.35f;
                if (Math.Abs(target - _pos) < 0.01f) { _pos = target; _anim.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                _anim.Start();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler CheckedChanged;

        /// <summary>Sets the value without animating or raising the event (initial load).</summary>
        public void SetSilently(bool value)
        {
            _checked = value;
            _pos = value ? 1 : 0;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Painter.Setup(g);
            var p = Theme.Current;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = r.Height / 2f;
            var off = p.IsDark ? Color.FromArgb(0x33, 0x3A, 0x4A) : Color.FromArgb(0xC9, 0xCF, 0xDC);
            var fill = Painter.Mix(off, p.Accent, _pos);
            if (_hover) fill = Painter.Mix(fill, Color.White, 0.06f);
            using (var b = new SolidBrush(fill)) Painter.FillRoundRect(g, b, r, rad);
            float inset = 3 * Theme.Scale;
            float knob = r.Height - 2 * inset;
            float x = r.X + inset + (r.Width - 2 * inset - knob) * _pos;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, r.Y + inset, knob, knob);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Painter.Alpha(p.Text, 90)))
                    Painter.DrawRoundRect(g, pen, new RectangleF(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), rad + 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _anim.Dispose();
            base.Dispose(disposing);
        }
    }
}
