using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace KeepAwake.UI
{
    /// <summary>GDI+ helpers shared by the custom-painted surfaces.</summary>
    public static class Painter
    {
        public static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static GraphicsPath RoundRect(RectangleF r, float tl, float tr, float br, float bl)
        {
            var p = new GraphicsPath();
            if (tl > 0) p.AddArc(r.X, r.Y, tl * 2, tl * 2, 180, 90); else p.AddLine(r.X, r.Y, r.X, r.Y);
            if (tr > 0) p.AddArc(r.Right - tr * 2, r.Y, tr * 2, tr * 2, 270, 90); else p.AddLine(r.Right, r.Y, r.Right, r.Y);
            if (br > 0) p.AddArc(r.Right - br * 2, r.Bottom - br * 2, br * 2, br * 2, 0, 90); else p.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
            if (bl > 0) p.AddArc(r.X, r.Bottom - bl * 2, bl * 2, bl * 2, 90, 90); else p.AddLine(r.X, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }

        public static void FillRoundRect(Graphics g, Brush b, RectangleF r, float radius)
        {
            using (var p = RoundRect(r, radius)) g.FillPath(b, p);
        }

        public static void DrawRoundRect(Graphics g, Pen pen, RectangleF r, float radius)
        {
            using (var p = RoundRect(r, radius)) g.DrawPath(pen, p);
        }

        public static void Text(Graphics g, string s, Font f, Color c, RectangleF r,
            StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center, bool noWrap = true, bool trim = true)
        {
            using (var sf = new StringFormat { Alignment = h, LineAlignment = v, Trimming = trim ? StringTrimming.EllipsisCharacter : StringTrimming.None })
            using (var b = new SolidBrush(c))
            {
                if (noWrap) sf.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(s, f, b, r, sf);
            }
        }

        public static SizeF Measure(Graphics g, string s, Font f)
        {
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces })
                return g.MeasureString(s, f, PointF.Empty, sf);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        public static Color FromHsl(float h, float s, float l)
        {
            h = ((h % 360f) + 360f) % 360f;
            float c = (1 - Math.Abs(2 * l - 1)) * s;
            float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
            float m = l - c / 2;
            float r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(Clamp((r + m) * 255), Clamp((g + m) * 255), Clamp((b + m) * 255));
        }

        private static int Clamp(float v) => v < 0 ? 0 : v > 255 ? 255 : (int)Math.Round(v);

        /// <summary>Soft radial glow at the top of a surface, as in the mockups.</summary>
        public static void TopGlow(Graphics g, RectangleF surface, Color glow, Color bg)
        {
            using (var path = new GraphicsPath())
            {
                var ellipse = new RectangleF(surface.X - surface.Width * 0.1f, surface.Y - surface.Height * 0.55f, surface.Width * 1.2f, surface.Height * 1.3f);
                path.AddEllipse(ellipse);
                using (var pgb = new PathGradientBrush(path) { CenterColor = glow, SurroundColors = new[] { bg }, CenterPoint = new PointF(surface.X + surface.Width / 2, surface.Y) })
                {
                    var old = g.Clip;
                    g.SetClip(surface);
                    g.FillPath(pgb, path);
                    g.Clip = old;
                }
            }
        }

        public static Image IconImage(Icon icon, int px)
        {
            using (var sized = new Icon(icon, px, px))
                return sized.ToBitmap();
        }
    }
}
