using System;
using System.Windows.Forms;
using KeepAwake.Native;

namespace KeepAwake.UI
{
    /// <summary>Rounded corners for frameless windows: DWM on Windows 11, a window region on Windows 10.</summary>
    public static class WindowChrome
    {
        public const int CS_DROPSHADOW = 0x20000;

        /// <summary>Logical corner radius the border is painted with. DWM uses its own radius (about 8 px) on Windows 11.</summary>
        public static int CornerRadius { get; private set; } = 16;

        public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

        public static void ApplyRoundedCorners(Form f, int radiusLogical)
        {
            if (IsWindows11)
            {
                int pref = NativeMethods.DWMWCP_ROUND;
                try
                {
                    if (NativeMethods.DwmSetWindowAttribute(f.Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0)
                    {
                        CornerRadius = 8;
                        return;
                    }
                }
                catch { }
            }
            CornerRadius = radiusLogical;
            int r = (int)Math.Round(radiusLogical * Theme.Scale);
            var rgn = NativeMethods.CreateRoundRectRgn(0, 0, f.Width + 1, f.Height + 1, r * 2, r * 2);
            // On success the system owns the region; on failure it must be freed here.
            if (NativeMethods.SetWindowRgn(f.Handle, rgn, true) == 0) NativeMethods.DeleteObject(rgn);
        }

        public static void BeginDrag(Form f)
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(f.Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HT_CAPTION, IntPtr.Zero);
        }

        /// <summary>DPI of the monitor under the mouse cursor (where a centred window will open); 96 when unknown.</summary>
        public static int DpiAtCursor()
        {
            try
            {
                var c = Cursor.Position;
                var mon = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = c.X, Y = c.Y }, 2 /* MONITOR_DEFAULTTONEAREST */);
                if (NativeMethods.GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out var x, out _) == 0 && x >= 96) return (int)x;
            }
            catch { }
            return 96;
        }

        public static int DpiOf(Form f)
        {
            try
            {
                var d = NativeMethods.GetDpiForWindow(f.Handle);
                if (d >= 96) return (int)d;
            }
            catch { }
            return Theme.Dpi;
        }
    }
}
