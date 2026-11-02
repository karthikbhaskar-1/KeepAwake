using System;
using System.Collections.Generic;
using System.Drawing;
using KeepAwake.Core;
using Microsoft.Win32;

namespace KeepAwake.UI
{
    public sealed class Palette
    {
        public bool IsDark;
        public Color Bg, Glow, Card, CardBorder, Text, Muted, Faint, Track, Menu, MenuBorder, MenuHover;
        public Color Success, SuccessBg, SuccessBorder, Danger, DangerBg, DangerBorder;
        public Color Accent, Accent2, OnAccent, AccentSoft, Button, ButtonBorder, ButtonHover;
    }

    /// <summary>Current palette, following Windows (theme + accent) unless overridden.</summary>
    public static class Theme
    {
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        private static string _family;

        public static Palette Current { get; private set; } = Build(true, DefaultAccent);
        public static ThemeMode Mode { get; private set; } = ThemeMode.System;
        public static event Action Changed;

        public static readonly Color DefaultAccent = Color.FromArgb(0x4F, 0x8C, 0xFF);
        public static readonly Color Cyan = Color.FromArgb(0x22, 0xD3, 0xEE);

        public static void Apply(ThemeMode mode)
        {
            Mode = mode;
            Refresh();
        }

        /// <summary>Re-reads the Windows theme and accent. Safe to call on every WM_SETTINGCHANGE.</summary>
        public static void Refresh()
        {
            bool dark = Mode == ThemeMode.Dark || (Mode == ThemeMode.System && SystemIsDark());
            var accent = SystemAccent();
            Current = Build(dark, accent);
            Changed?.Invoke();
        }

        public static bool SystemIsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k?.GetValue("AppsUseLightTheme");
                    return v is int i ? i == 0 : true;
                }
            }
            catch
            {
                return true;
            }
        }

        public static Color SystemAccent()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    var v = k?.GetValue("AccentColor");
                    if (v is int abgr)
                    {
                        var c = Color.FromArgb((abgr) & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                        return EnsureVivid(c);
                    }
                }
            }
            catch { }
            return DefaultAccent;
        }

        private static Color EnsureVivid(Color c)
        {
            // Very dark or very grey accents disappear on the dark background; lift them a little.
            float h = c.GetHue(), s = c.GetSaturation(), l = c.GetBrightness();
            if (s < 0.35f) s = 0.35f;
            if (l < 0.45f) l = 0.5f;
            if (l > 0.75f) l = 0.72f;
            return Painter.FromHsl(h, s, l);
        }

        private static Palette Build(bool dark, Color accent)
        {
            var accent2 = Painter.FromHsl((accent.GetHue() + 330f) % 360f, Math.Min(1f, accent.GetSaturation() + 0.15f), Math.Min(0.8f, accent.GetBrightness() + 0.05f));
            if (dark)
            {
                return new Palette
                {
                    IsDark = true,
                    Bg = Color.FromArgb(0x0F, 0x11, 0x15),
                    Glow = Color.FromArgb(0x18, 0x22, 0x38),
                    Card = Color.FromArgb(0x16, 0x1A, 0x22),
                    CardBorder = Color.FromArgb(0x26, 0x2B, 0x37),
                    Text = Color.FromArgb(0xF2, 0xF4, 0xF8),
                    Muted = Color.FromArgb(0x8B, 0x93, 0xA7),
                    Faint = Color.FromArgb(0x5F, 0x67, 0x80),
                    Track = Color.FromArgb(0x22, 0x28, 0x37),
                    Menu = Color.FromArgb(0x17, 0x1A, 0x21),
                    MenuBorder = Color.FromArgb(0x2E, 0x34, 0x42),
                    MenuHover = Color.FromArgb(0x26, 0x2C, 0x3A),
                    Success = Color.FromArgb(0x34, 0xD3, 0x99),
                    SuccessBg = Color.FromArgb(30, 0x34, 0xD3, 0x99),
                    SuccessBorder = Color.FromArgb(90, 0x34, 0xD3, 0x99),
                    Danger = Color.FromArgb(0xF8, 0x71, 0x71),
                    DangerBg = Color.FromArgb(26, 0xF8, 0x71, 0x71),
                    DangerBorder = Color.FromArgb(128, 0xF8, 0x71, 0x71),
                    Accent = accent,
                    Accent2 = accent2,
                    OnAccent = Color.FromArgb(0x0B, 0x0F, 0x1A),
                    AccentSoft = Color.FromArgb(46, accent),
                    Button = Color.FromArgb(0x1B, 0x1F, 0x2A),
                    ButtonBorder = Color.FromArgb(0x2E, 0x34, 0x42),
                    ButtonHover = Color.FromArgb(0x24, 0x29, 0x36),
                };
            }
            return new Palette
            {
                IsDark = false,
                Bg = Color.FromArgb(0xF7, 0xF8, 0xFC),
                Glow = Color.FromArgb(0xE8, 0xEE, 0xFB),
                Card = Color.White,
                CardBorder = Color.FromArgb(0xE2, 0xE5, 0xEE),
                Text = Color.FromArgb(0x11, 0x18, 0x27),
                Muted = Color.FromArgb(0x6B, 0x72, 0x80),
                Faint = Color.FromArgb(0x9C, 0xA3, 0xAF),
                Track = Color.FromArgb(0xE3, 0xE7, 0xF0),
                Menu = Color.White,
                MenuBorder = Color.FromArgb(0xD5, 0xD9, 0xE4),
                MenuHover = Color.FromArgb(0xEE, 0xF1, 0xF7),
                Success = Color.FromArgb(0x04, 0x78, 0x57),
                SuccessBg = Color.FromArgb(30, 0x10, 0xB9, 0x81),
                SuccessBorder = Color.FromArgb(100, 0x10, 0xB9, 0x81),
                Danger = Color.FromArgb(0xB9, 0x1C, 0x1C),
                DangerBg = Color.FromArgb(16, 0xDC, 0x26, 0x26),
                DangerBorder = Color.FromArgb(115, 0xDC, 0x26, 0x26),
                Accent = Painter.FromHsl(accent.GetHue(), accent.GetSaturation(), Math.Min(accent.GetBrightness(), 0.5f)),
                Accent2 = Painter.FromHsl(accent2.GetHue(), accent2.GetSaturation(), Math.Min(accent2.GetBrightness(), 0.5f)),
                OnAccent = Color.White,
                AccentSoft = Color.FromArgb(36, accent),
                Button = Color.White,
                ButtonBorder = Color.FromArgb(0xD5, 0xD9, 0xE4),
                ButtonHover = Color.FromArgb(0xF1, 0xF3, 0xF8),
            };
        }

        public static string FontFamily
        {
            get
            {
                if (_family != null) return _family;
                foreach (var name in new[] { "Segoe UI Variable Display", "Segoe UI Variable", "Segoe UI" })
                {
                    try
                    {
                        using (var f = new FontFamily(name))
                        {
                            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) { _family = name; break; }
                        }
                    }
                    catch { }
                }
                return _family ?? (_family = "Segoe UI");
            }
        }

        public static Font Font(float pointSize, FontStyle style = FontStyle.Regular)
        {
            var key = pointSize.ToString("0.0") + "|" + (int)style;
            if (!Fonts.TryGetValue(key, out var f))
            {
                f = new Font(FontFamily, pointSize, style, GraphicsUnit.Point);
                Fonts[key] = f;
            }
            return f;
        }
    }
}
