using System;
using System.Windows.Forms;
using KeepAwake.Native;

namespace KeepAwake.UI
{
    /// <summary>Rounded corners for frameless windows: DWM on Windows 11, a window region on Windows 10.</summary>
    public static class WindowChrome
    {
        public const int CS_DROPSHADOW = 0x20000;

        public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

        public static void ApplyRoundedCorners(Form f, int radiusLogical)
        {
            if (IsWindows11)
            {
                int pref = NativeMethods.DWMWCP_ROUND;
                try
                {
                    if (NativeMethods.DwmSetWindowAttribute(f.Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0)
                        return;
                }
                catch { }
            }
            int r = (int)Math.Round(radiusLogical * f.DeviceDpi / 96f);
            var rgn = NativeMethods.CreateRoundRectRgn(0, 0, f.Width + 1, f.Height + 1, r * 2, r * 2);
            NativeMethods.SetWindowRgn(f.Handle, rgn, true);
        }

        public static void BeginDrag(Form f)
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(f.Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HT_CAPTION, IntPtr.Zero);
        }
    }
}
