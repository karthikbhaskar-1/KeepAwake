using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeepAwake.UI
{
    /// <summary>Flat, rounded, theme-aware rendering for ContextMenuStrip.</summary>
    public sealed class MenuRenderer : ToolStripRenderer
    {
        private static Palette P => Theme.Current;

        public static ContextMenuStrip CreateMenu()
        {
            var m = new ContextMenuStrip
            {
                Renderer = new MenuRenderer(),
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Font = Theme.Font(9.75f),
                DropShadowEnabled = true,
                Padding = new Padding(6, 6, 6, 6),
            };
            return m;
        }

        protected override void Initialize(ToolStrip toolStrip)
        {
            base.Initialize(toolStrip);
            toolStrip.BackColor = P.Menu;
            toolStrip.ForeColor = P.Text;
        }

        protected override void InitializeItem(ToolStripItem item)
        {
            base.InitializeItem(item);
            item.ForeColor = P.Text;
            item.Padding = new Padding(10, 6, 10, 6);
            if (item is ToolStripDropDownItem d)
            {
                d.DropDown.BackColor = P.Menu;
                d.DropDown.ForeColor = P.Text;
                if (d.DropDown is ToolStripDropDownMenu dm)
                {
                    dm.ShowImageMargin = false;
                    dm.ShowCheckMargin = false;
                }
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(P.Menu);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Painter.Setup(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var pen = new Pen(P.MenuBorder)) Painter.DrawRoundRect(e.Graphics, pen, r, 6);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            Painter.Setup(g);
            var r = new RectangleF(3, 0.5f, e.Item.Width - 6, e.Item.Height - 1);
            if (e.Item.Selected && e.Item.Enabled)
            {
                using (var b = new SolidBrush(P.MenuHover)) Painter.FillRoundRect(g, b, r, 6);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool isChecked = (e.Item as ToolStripMenuItem)?.Checked == true;
            var color = !e.Item.Enabled ? P.Faint : e.Item.Tag as string == "danger" ? P.Danger : isChecked ? P.Accent : P.Text;
            e.TextColor = color;
            if (isChecked && !e.TextFont.Bold) e.TextFont = Theme.Font(9.75f, FontStyle.Bold);
            e.TextFormat = (e.TextFormat & ~TextFormatFlags.HidePrefix) | TextFormatFlags.NoPrefix;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(P.MenuBorder))
            {
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            var g = e.Graphics;
            Painter.Setup(g);
            var c = e.Item.Enabled ? P.Muted : P.Faint;
            var r = e.ArrowRectangle;
            var mid = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            using (var pen = new Pen(c, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, mid.X - 2, mid.Y - 4, mid.X + 2, mid.Y);
                g.DrawLine(pen, mid.X + 2, mid.Y, mid.X - 2, mid.Y + 4);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            Painter.Setup(g);
            var r = e.ImageRectangle;
            using (var pen = new Pen(P.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float x = e.Item.Width - 22, y = e.Item.Height / 2f;
                g.DrawLine(pen, x, y, x + 3.5f, y + 3.5f);
                g.DrawLine(pen, x + 3.5f, y + 3.5f, x + 10, y - 3.5f);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    }
}
