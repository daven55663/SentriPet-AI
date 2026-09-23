using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SentriPet
{
    static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern int GetWindowLong32(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        public static int GetExStyle(IntPtr h)
        {
            return IntPtr.Size == 8 ? (int)GetWindowLongPtr64(h, GWL_EXSTYLE).ToInt64() : GetWindowLong32(h, GWL_EXSTYLE);
        }

        public static void SetExStyle(IntPtr h, int style)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, GWL_EXSTYLE, new IntPtr(style));
            else SetWindowLong32(h, GWL_EXSTYLE, style);
        }

        /// <summary>True when the foreground window is a full-screen app (video, game, slideshow) on the given monitor.</summary>
        public static bool ForegroundIsFullscreen(IntPtr myWindow)
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == myWindow || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
                var cls = new StringBuilder(64);
                GetClassName(fg, cls, 64);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
                // real full-screen apps (games, F11 browsers, slideshows, video players) drop the caption
                const int GWL_STYLE = -16, WS_CAPTION = 0x00C00000;
                int style = IntPtr.Size == 8 ? (int)GetWindowLongPtr64(fg, GWL_STYLE).ToInt64() : GetWindowLong32(fg, GWL_STYLE);
                if ((style & WS_CAPTION) == WS_CAPTION) return false;
                RECT r;
                if (!GetWindowRect(fg, out r)) return false;
                IntPtr mon = MonitorFromWindow(fg, 2);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (!GetMonitorInfo(mon, ref mi)) return false;
                if (MonitorFromWindow(myWindow, 2) != mon) return false;
                return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
            }
            catch { return false; }
        }
    }
}
