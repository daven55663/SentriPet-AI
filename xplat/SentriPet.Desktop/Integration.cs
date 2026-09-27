using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Platform;

namespace SentriPet
{
    /// <summary>
    /// What each operating system does differently: start at sign-in, notifications, click-through windows,
    /// hiding during full-screen apps, one running copy, opening files.
    /// </summary>
    static class Integration
    {
        // ------------------------------------------------------------------ start at sign-in

        public static string AutostartHint
        {
            get { return Os.Windows ? L.T("登入 Windows 後自動出現在桌面上") : L.T("登入後自動出現在桌面上"); }
        }

        static string ExePath { get { return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule.FileName; } }

        /// <summary>The .app bundle this program runs from on macOS (…/SentriPet.app), or null.</summary>
        static string MacBundle
        {
            get
            {
                string p = ExePath;
                int i = p.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
                return i > 0 ? p.Substring(0, i + 4) : null;
            }
        }

        public static string LaunchAgentPath { get { return Path.Combine(AppPaths.Home, "Library", "LaunchAgents", "com.sentripet.app.plist"); } }
        public static string AutostartDesktopPath
        {
            get
            {
                string cfg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrEmpty(cfg)) cfg = Path.Combine(AppPaths.Home, ".config");
                return Path.Combine(cfg, "autostart", "sentripet.desktop");
            }
        }

        /// <summary>Registers (or removes) the program to start at sign-in. Never touches the system in the dev profile.</summary>
        public static void SetAutostart(bool enable)
        {
            if (AppPaths.Dev) return;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        if (enable) k.SetValue("SentriPet", "\"" + ExePath + "\" --autostart");
                        else if (k.GetValue("SentriPet") != null) k.DeleteValue("SentriPet", false);
                    }
                }
                else if (Os.Mac) WriteOrDelete(LaunchAgentPath, enable ? LaunchAgent(MacBundle, ExePath) : null);
                else WriteOrDelete(AutostartDesktopPath, enable ? DesktopEntry(ExePath) : null);
            }
            catch (Exception ex) { Log.Error("autostart", ex); }
        }

        static void WriteOrDelete(string path, string content)
        {
            if (content == null) { if (File.Exists(path)) File.Delete(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path) || File.ReadAllText(path) != content) File.WriteAllText(path, content);
        }

        static string Xml(string s) { return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); }

        /// <summary>macOS LaunchAgent: opens the .app bundle (or runs the program) at sign-in.</summary>
        internal static string LaunchAgent(string bundle, string exe)
        {
            string args = bundle != null
                ? "    <string>/usr/bin/open</string>\n    <string>" + Xml(bundle) + "</string>\n    <string>--args</string>\n    <string>--autostart</string>\n"
                : "    <string>" + Xml(exe) + "</string>\n    <string>--autostart</string>\n";
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                   "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                   "<plist version=\"1.0\">\n<dict>\n  <key>Label</key>\n  <string>com.sentripet.app</string>\n" +
                   "  <key>ProgramArguments</key>\n  <array>\n" + args + "  </array>\n" +
                   "  <key>RunAtLoad</key>\n  <true/>\n</dict>\n</plist>\n";
        }

        /// <summary>freedesktop.org autostart entry (GNOME, KDE, Xfce, …).</summary>
        internal static string DesktopEntry(string exe)
        {
            return "[Desktop Entry]\nType=Application\nName=" + AppInfo.Name + "\nComment=AI usage desk pet\n" +
                   "Exec=\"" + exe.Replace("\"", "\\\"") + "\" --autostart\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
        }

        // ------------------------------------------------------------------ notifications

        public static string NotificationHint
        {
            get { return Os.Windows ? L.T("用量越過門檻、或額度重置時跳出 Windows 通知") : L.T("用量越過門檻、或額度重置時跳出系統通知"); }
        }

        /// <summary>Shows a system notification (Windows toast, macOS Notification Center, Linux notify-send). Never blocks.</summary>
        public static void Notify(string title, string text)
        {
            if (AppPaths.Dev && Environment.GetEnvironmentVariable("SENTRIPET_NOTIFY") == null) { Log.Info("notify (dev, not shown): " + title + " · " + text); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string exe; string[] args;
                    if (!NotifyCommand(title, text, out exe, out args)) return;
                    var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    foreach (var a in args) psi.ArgumentList.Add(a);
                    using (var p = Process.Start(psi)) { if (p != null) p.WaitForExit(15000); }
                }
                catch (Exception ex) { Log.Warn("notify: " + ex.Message); }
            });
        }

        /// <summary>The command that shows a notification on this system (exposed for the tests).</summary>
        internal static bool NotifyCommand(string title, string text, out string exe, out string[] args)
        {
            exe = null; args = null;
            if (Os.Windows)
            {
                // a toast through PowerShell (WinRT), so no extra runtime is needed
                string xml = "<toast><visual><binding template=\"ToastGeneric\"><text>" + SecurityElementEscape(title) + "</text><text>" +
                             SecurityElementEscape(text) + "</text></binding></visual></toast>";
                string script =
                    "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null;" +
                    "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null;" +
                    "$x = New-Object Windows.Data.Xml.Dom.XmlDocument; $x.LoadXml([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(xml)) + "')));" +
                    "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe').Show([Windows.UI.Notifications.ToastNotification]::new($x))";
                exe = "powershell.exe";
                args = new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", script };
                return true;
            }
            if (Os.Mac)
            {
                exe = "/usr/bin/osascript";
                args = new[] { "-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv)", "-e", "end run", title, text };
                return true;
            }
            string ns = AppPaths.Which("notify-send");
            if (ns == null) return false;
            exe = ns;
            args = new[] { "--app-name=" + AppInfo.Name, title, text };
            return true;
        }

        static string SecurityElementEscape(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        // ------------------------------------------------------------------ click-through

        /// <summary>Windows, macOS and X11 (also XWayland) can let clicks pass through the widget.</summary>
        public static bool CanClickThrough { get { return Os.Windows || Os.Mac || Os.Linux; } }

        public static void SetClickThrough(Window w, bool on)
        {
            try
            {
                var h = w.TryGetPlatformHandle();
                if (h == null) return;
                if (Os.Windows)
                {
                    int ex = Win.GetExStyle(h.Handle);
                    int want = on ? ex | Win.WS_EX_TRANSPARENT | Win.WS_EX_LAYERED : ex & ~Win.WS_EX_TRANSPARENT;
                    if (want != ex) Win.SetExStyle(h.Handle, want);
                }
                else if (Os.Mac)
                {
                    var mac = h as IMacOSTopLevelPlatformHandle;
                    if (mac != null && mac.NSWindow != IntPtr.Zero)
                        Mac.objc_msgSend(mac.NSWindow, Mac.sel_registerName("setIgnoresMouseEvents:"), on);
                }
                else if (h.HandleDescriptor == "XID") X11.SetInputPassThrough(h.Handle, on);
            }
            catch (Exception ex) { Log.Warn("click-through: " + ex.Message); }
        }

        // ------------------------------------------------------------------ full screen

        /// <summary>Only Windows can tell reliably whether another app is full screen.</summary>
        public static bool CanDetectFullscreen { get { return Os.Windows; } }

        public static bool ForegroundIsFullscreen(Window mine)
        {
            if (!Os.Windows) return false;
            var h = mine.TryGetPlatformHandle();
            return h != null && Win.ForegroundIsFullscreen(h.Handle);
        }

        // ------------------------------------------------------------------ files and folders

        public static void OpenPath(string path)
        {
            try
            {
                if (Os.Mac) Process.Start("/usr/bin/open", new[] { path });
                else if (Os.Linux) Process.Start(AppPaths.Which("xdg-open") ?? "xdg-open", new[] { path });
                else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("open " + path, ex); }
        }

        // ------------------------------------------------------------------ one running copy

        static Mutex singleton;

        /// <summary>
        /// True for the first copy. A second copy asks the first to show itself (through a local pipe) and should exit.
        /// </summary>
        public static bool ClaimSingleInstance(Action showRequested, string name = null)
        {
            if (name == null) name = "SentriPet." + (Environment.UserName ?? "user") + (AppPaths.Dev ? ".dev" : "");
            bool created;
            try
            {
                var m = new Mutex(true, (Os.Windows ? @"Local\" : "") + name, out created);
                if (created) singleton = m;
                else m.Dispose();
            }
            catch (Exception ex) { Log.Warn("single instance: " + ex.Message); return true; }
            if (!created)
            {
                try
                {
                    using (var c = new NamedPipeClientStream(".", name + ".show", PipeDirection.Out))
                    {
                        c.Connect(1500);
                        c.WriteByte(1);
                    }
                }
                catch (Exception ex) { Log.Warn("could not reach the running copy: " + ex.Message); }
                return false;
            }
            var t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var s = new NamedPipeServerStream(name + ".show", PipeDirection.In, 1))
                        {
                            s.WaitForConnection();
                            s.ReadByte();
                        }
                        showRequested();
                    }
                    catch (Exception ex) { Log.Warn("show pipe: " + ex.Message); Thread.Sleep(2000); }
                }
            }) { IsBackground = true, Name = "show-pipe" };
            t.Start();
            return true;
        }

        // ------------------------------------------------------------------ native bits

        static class Win
        {
            public const int GWL_EXSTYLE = -20, GWL_STYLE = -16, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_CAPTION = 0x00C00000;

            [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
            [StructLayout(LayoutKind.Sequential)]
            public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

            [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
            [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtrW(IntPtr h, int i, IntPtr v);
            [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
            [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
            [DllImport("user32.dll")] static extern bool GetMonitorInfoW(IntPtr m, ref MONITORINFO mi);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
            [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
            [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();

            public static int GetExStyle(IntPtr h) { return (int)GetWindowLongPtrW(h, GWL_EXSTYLE).ToInt64(); }
            public static void SetExStyle(IntPtr h, int v) { SetWindowLongPtrW(h, GWL_EXSTYLE, new IntPtr(v)); }

            /// <summary>Same test as the WPF version: a caption-less foreground window covering our monitor.</summary>
            public static bool ForegroundIsFullscreen(IntPtr mine)
            {
                try
                {
                    IntPtr fg = GetForegroundWindow();
                    if (fg == IntPtr.Zero || fg == mine || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
                    var cls = new StringBuilder(64);
                    GetClassNameW(fg, cls, 64);
                    string c = cls.ToString();
                    if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
                    int style = (int)GetWindowLongPtrW(fg, GWL_STYLE).ToInt64();
                    if ((style & WS_CAPTION) == WS_CAPTION) return false;
                    RECT r;
                    if (!GetWindowRect(fg, out r)) return false;
                    IntPtr mon = MonitorFromWindow(fg, 2);
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                    if (!GetMonitorInfoW(mon, ref mi)) return false;
                    if (MonitorFromWindow(mine, 2) != mon) return false;
                    return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
                }
                catch { return false; }
            }
        }

        static class Mac
        {
            [DllImport("/usr/lib/libobjc.A.dylib")] public static extern IntPtr sel_registerName(string name);
            [DllImport("/usr/lib/libobjc.A.dylib")] public static extern void objc_msgSend(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
        }

        /// <summary>X11: an empty input shape lets clicks through to the windows below.</summary>
        static class X11
        {
            [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
            [DllImport("libX11.so.6")] static extern int XFlush(IntPtr display);
            [DllImport("libXext.so.6")] static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int destKind, int x, int y, IntPtr rects, int n, int op, int ordering);
            [DllImport("libXext.so.6")] static extern void XShapeCombineMask(IntPtr display, IntPtr window, int destKind, int x, int y, IntPtr pixmap, int op);
            const int ShapeInput = 2, ShapeSet = 0, Unsorted = 0;
            static IntPtr display;

            public static void SetInputPassThrough(IntPtr window, bool on)
            {
                if (display == IntPtr.Zero) display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) return;
                if (on) XShapeCombineRectangles(display, window, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, Unsorted);
                else XShapeCombineMask(display, window, ShapeInput, 0, 0, IntPtr.Zero, ShapeSet);   // no mask = the whole window again
                XFlush(display);
            }
        }
    }
}
