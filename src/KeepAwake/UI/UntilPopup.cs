using System;
using System.Drawing;
using System.Windows.Forms;
using KeepAwake.Core;
using KeepAwake.Native;
using KeepAwake.UI.Controls;

namespace KeepAwake.UI
{
    /// <summary>Small painted popup that asks for a 24 h time of day. Enter accepts, Esc or clicking away cancels.</summary>
    public sealed class UntilPopup : Form
    {
        private const int W = 232, H = 172, Pad = 16;
        private const int CaptionY = 14, PanelY = 36, PanelH = 44, HintY = 88, ButtonY = 118, ButtonH = 38;

        private readonly TextBox _text;
        private readonly PillButton _ok, _cancel;
        private readonly Color _fill;
        private string _hint = "Tomorrow if earlier than now";
        private bool _hintIsError, _closing;

        /// <summary>The time the user entered; valid after the dialog returns OK.</summary>
        public TimeSpan Chosen { get; private set; }

        private static float S => Theme.Scale;
        private static int L(int logical) => (int)Math.Round(logical * S);

        public UntilPopup(TimeSpan initial)
        {
            var p = Theme.Current;
            _fill = Painter.Mix(p.Menu, p.Text, p.IsDark ? 0.07f : 0.04f);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = p.Menu;
            ForeColor = p.Text;
            Text = "Keep awake until";
            ClientSize = new Size(L(W), L(H));

            _text = new TextBox
            {
                BorderStyle = BorderStyle.None,
                MaxLength = 5,
                Font = Theme.Font(14f, FontStyle.Bold),
                BackColor = _fill,
                ForeColor = p.Text,
                TextAlign = HorizontalAlignment.Center,
                Text = UntilTime.Format(initial),
                AccessibleName = "Time, 24 hour HH:mm",
            };
            _text.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != ':') e.Handled = true;
            };
            _text.TextChanged += (s, e) =>
            {
                var clean = new System.Text.StringBuilder();
                foreach (var c in _text.Text) if ((c >= '0' && c <= '9') || c == ':') clean.Append(c);
                if (clean.ToString() != _text.Text)
                {
                    int caret = _text.SelectionStart;
                    _text.Text = clean.ToString();
                    _text.SelectionStart = Math.Min(caret, _text.TextLength);
                }
                if (_hintIsError) SetHint("Tomorrow if earlier than now", false);
            };
            _text.GotFocus += (s, e) => Invalidate();
            _text.LostFocus += (s, e) => Invalidate();

            _ok = new PillButton { Kind = PillKind.Primary, Text = "OK", Radius = 10f };
            _ok.Click += (s, e) => Accept();
            _cancel = new PillButton { Kind = PillKind.Secondary, Text = "Cancel", Radius = 10f };
            _cancel.Click += (s, e) => Close(DialogResult.Cancel);

            Controls.AddRange(new Control[] { _text, _ok, _cancel });
            LayoutControls();
        }

        private Rectangle PanelRect => new Rectangle(L(Pad), L(PanelY), L(W - 2 * Pad), L(PanelH));

        private void LayoutControls()
        {
            var pr = PanelRect;
            int th = _text.PreferredHeight;
            _text.SetBounds(pr.X + L(10), pr.Y + (pr.Height - th) / 2, pr.Width - L(20), th);
            int gap = L(10), bw = (L(W - 2 * Pad) - gap) / 2;
            _ok.SetBounds(L(Pad), L(ButtonY), bw, L(ButtonH));
            _cancel.SetBounds(L(Pad) + bw + gap, L(ButtonY), bw, L(ButtonH));
        }

        private void SetHint(string text, bool error)
        {
            _hint = text;
            _hintIsError = error;
            Invalidate(new Rectangle(0, L(HintY), Width, L(20)));
        }

        private void Accept()
        {
            if (!UntilTime.TryParse(_text.Text, out var t))
            {
                SetHint("Enter a 24 h time, like 17:30", true);
                _text.Focus();
                _text.SelectAll();
                return;
            }
            Chosen = t;
            Close(DialogResult.OK);
        }

        private void Close(DialogResult result)
        {
            if (_closing) return;
            _closing = true;
            DialogResult = result;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(DialogResult.Cancel); return true; }
            if (keyData == Keys.Enter && !_cancel.Focused) { Accept(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!_closing && Visible) Close(DialogResult.Cancel);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // ShowDialog disables the owner, which would swallow the click-away; re-enable it so clicking it cancels.
            if (Owner != null) NativeMethods.EnableWindow(Owner.Handle, true);
            _text.Focus();
            _text.SelectAll();
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= WindowChrome.CS_DROPSHADOW; return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            bool rounded = false;
            if (WindowChrome.IsWindows11)
            {
                int pref = NativeMethods.DWMWCP_ROUND;
                try { rounded = NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0; }
                catch { }
            }
            if (!rounded)
            {
                int r = L(10);
                var rgn = NativeMethods.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, r * 2, r * 2);
                if (NativeMethods.SetWindowRgn(Handle, rgn, true) == 0) NativeMethods.DeleteObject(rgn);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.Current;
            Painter.Setup(g);
            Painter.Text(g, "KEEP AWAKE UNTIL", Theme.Font(7.75f), p.Muted, new RectangleF(L(Pad), L(CaptionY), L(W - 2 * Pad), L(16)));

            var pr = PanelRect;
            var r = new RectangleF(pr.X + 0.5f, pr.Y + 0.5f, pr.Width - 1, pr.Height - 1);
            using (var b = new SolidBrush(_fill)) Painter.FillRoundRect(g, b, r, 10 * S);
            using (var pen = new Pen(_text.Focused ? p.Accent : p.MenuBorder, _text.Focused ? 1.5f : 1f))
                Painter.DrawRoundRect(g, pen, r, 10 * S);

            Painter.Text(g, _hint, Theme.Font(8.25f), _hintIsError ? p.Danger : p.Muted,
                new RectangleF(L(Pad), L(HintY), L(W - 2 * Pad), L(18)));

            using (var pen = new Pen(p.MenuBorder))
                Painter.DrawRoundRect(g, pen, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10 * S);
        }

        /// <summary>
        /// Shows the popup above the anchor control, right edges aligned like the duration menus.
        /// Returns true and the chosen time when the user pressed OK.
        /// </summary>
        public static bool Prompt(IWin32Window owner, Control anchor, TimeSpan initial, out TimeSpan chosen)
        {
            using (var f = new UntilPopup(initial))
            {
                var corner = anchor.PointToScreen(new Point(anchor.Width, -L(6)));
                var area = Screen.FromControl(anchor).WorkingArea;
                int x = Math.Max(area.Left, Math.Min(corner.X - f.Width, area.Right - f.Width));
                int y = Math.Max(area.Top, corner.Y - f.Height);
                f.Location = new Point(x, y);
                var result = f.ShowDialog(owner);
                chosen = f.Chosen;
                return result == DialogResult.OK;
            }
        }
    }
}
