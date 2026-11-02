using System;
using System.Runtime.InteropServices;

namespace KeepAwake.Native
{
    internal static class NativeMethods
    {
        [Flags]
        public enum ExecutionState : uint
        {
            SystemRequired = 0x00000001,
            DisplayRequired = 0x00000002,
            Continuous = 0x80000000,
        }

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_MOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int Left, Right, Top, Bottom;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int w, int h);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    }
}
