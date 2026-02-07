using System;
using System.Runtime.InteropServices;

namespace RefScrn.Services
{
    public static class NativeMethods
    {
        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_WIN = 0x0008;
        public const int VK_A = 0x41;
        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        // DPI & Monitor Support
        public const int MONITOR_DEFAULTTONEAREST = 2;
        public enum MonitorDpiType { Effective = 0, Angular = 1, Raw = 2 }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, int dwFlags);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);
    }
}
