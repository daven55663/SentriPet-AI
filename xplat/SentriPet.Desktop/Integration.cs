using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;
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
                    "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('" + ToastAppId + "').Show([Windows.UI.Notifications.ToastNotification]::new($x))";
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

        /// <summary>
        /// Windows shows a toast under the name and icon of a Start menu shortcut that carries the same AppUserModelID;
        /// without the shortcut (dev profile) the toast goes out as PowerShell's.
        /// </summary>
        static string ToastAppId
        {
            get { return File.Exists(StartMenuShortcut) ? AppUserModelId : @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe"; }
        }

        public const string AppUserModelId = "SentriPet.App";

        public static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk"); }
        }

        /// <summary>Windows: a Start menu entry for SentriPet (also what toasts are shown under). Not in the dev profile.</summary>
        public static void EnsureStartMenuShortcut()
        {
            if (!OperatingSystem.IsWindows() || AppPaths.Dev) return;
            try
            {
                string target = ExePath;
                if (File.Exists(StartMenuShortcut) && ShellLink.ReadTarget(StartMenuShortcut) == target && ShellLink.ReadAppId(StartMenuShortcut) == AppUserModelId) return;
                ShellLink.Create(StartMenuShortcut, target, "", AppUserModelId, AppInfo.Name);
                Log.Info("start menu shortcut -> " + target);
            }
            catch (Exception ex) { Log.Warn("start menu shortcut: " + ex.Message); }
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

        /// <summary>
        /// Windows and X11 desktops can tell whether the active app is full screen. (A full-screen macOS app gets a
        /// Space of its own where the widget is not shown anyway.)
        /// </summary>
        public static bool CanDetectFullscreen { get { return Os.Windows || Os.Linux; } }

        static bool x11FullscreenFailed;

        public static bool ForegroundIsFullscreen(Window mine)
        {
            var h = mine.TryGetPlatformHandle();
            if (h == null) return false;
            if (Os.Windows) return Win.ForegroundIsFullscreen(h.Handle);
            if (Os.Linux && h.HandleDescriptor == "XID" && !x11FullscreenFailed)
            {
                try
                {
                    var center = mine.Position + new PixelPoint((int)(mine.Bounds.Width * mine.DesktopScaling / 2), (int)(mine.Bounds.Height * mine.DesktopScaling / 2));
                    return X11.ActiveWindowIsFullscreen(h.Handle, center.X, center.Y);
                }
                catch (Exception ex) { x11FullscreenFailed = true; Log.Warn("X11 full screen: " + ex.Message); }
            }
            return false;
        }

        // ------------------------------------------------------------------ mouse

        static bool x11CursorFailed, macCursorFailed;

        /// <summary>The mouse position on macOS in global points (top-left origin), or null.</summary>
        public static Point? MacCursor()
        {
            if (!Os.Mac || macCursorFailed) return null;
            try { return Mac.CursorLocation(); }
            catch (Exception ex) { macCursorFailed = true; Log.Warn("macOS cursor: " + ex.Message); return null; }
        }

        /// <summary>The mouse position on an X11 desktop (also XWayland), in screen pixels; null elsewhere.</summary>
        public static Avalonia.PixelPoint? X11Cursor()
        {
            if (!Os.Linux || x11CursorFailed) return null;
            try
            {
                int x, y;
                if (X11.Pointer(out x, out y)) return new Avalonia.PixelPoint(x, y);
            }
            catch (Exception ex) { x11CursorFailed = true; Log.Warn("X11 cursor: " + ex.Message); }
            return null;
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

        /// <summary>Windows shortcuts (.lnk) with an AppUserModelID, through the shell's COM objects.</summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        internal static class ShellLink
        {
            [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
            class CShellLink { }

            [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
            interface IShellLinkW
            {
                void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
                void GetIDList(out IntPtr pidl);
                void SetIDList(IntPtr pidl);
                void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
                void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
                void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
                void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
                void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
                void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
                void GetHotkey(out short hotkey);
                void SetHotkey(short hotkey);
                void GetShowCmd(out int cmd);
                void SetShowCmd(int cmd);
                void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
                void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
                void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
                void Resolve(IntPtr hwnd, uint flags);
                void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
            }

            [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
            interface IPersistFile
            {
                void GetClassID(out Guid clsid);
                [PreserveSig] int IsDirty();
                void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
                void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
                void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
                void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
            }

            [StructLayout(LayoutKind.Sequential, Pack = 4)]
            struct PropertyKey { public Guid FormatId; public int PropertyId; }

            [StructLayout(LayoutKind.Sequential)]
            struct PropVariant { public ushort Type; public ushort R1, R2, R3; public IntPtr Value; public IntPtr Value2; }

            [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
            interface IPropertyStore
            {
                void GetCount(out uint count);
                void GetAt(uint index, out PropertyKey key);
                void GetValue(ref PropertyKey key, out PropVariant value);
                void SetValue(ref PropertyKey key, ref PropVariant value);
                void Commit();
            }

            [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pv);

            // System.AppUserModel.ID
            static PropertyKey AppIdKey { get { return new PropertyKey { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 }; } }
            const ushort VT_LPWSTR = 31;

            public static void Create(string lnk, string target, string args, string appId, string description)
            {
                var link = (IShellLinkW)new CShellLink();
                link.SetPath(target);
                link.SetArguments(args ?? "");
                link.SetWorkingDirectory(Path.GetDirectoryName(target));
                link.SetDescription(description ?? "");
                link.SetIconLocation(target, 0);
                var store = (IPropertyStore)link;
                var key = AppIdKey;
                var pv = new PropVariant { Type = VT_LPWSTR, Value = Marshal.StringToCoTaskMemUni(appId) };
                try
                {
                    store.SetValue(ref key, ref pv);
                    store.Commit();
                }
                finally { PropVariantClear(ref pv); }
                Directory.CreateDirectory(Path.GetDirectoryName(lnk));
                ((IPersistFile)link).Save(lnk, true);
                Marshal.ReleaseComObject(link);
            }

            public static string ReadTarget(string lnk)
            {
                var link = (IShellLinkW)new CShellLink();
                try
                {
                    ((IPersistFile)link).Load(lnk, 0);
                    var sb = new StringBuilder(1024);
                    link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                    return sb.ToString();
                }
                finally { Marshal.ReleaseComObject(link); }
            }

            public static string ReadAppId(string lnk)
            {
                var link = (IShellLinkW)new CShellLink();
                try
                {
                    ((IPersistFile)link).Load(lnk, 0);
                    var key = AppIdKey;
                    PropVariant pv;
                    ((IPropertyStore)link).GetValue(ref key, out pv);
                    try { return pv.Type == VT_LPWSTR ? Marshal.PtrToStringUni(pv.Value) : null; }
                    finally { PropVariantClear(ref pv); }
                }
                finally { Marshal.ReleaseComObject(link); }
            }
        }

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
            const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
            const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
            [DllImport("/usr/lib/libobjc.A.dylib")] public static extern IntPtr sel_registerName(string name);
            [DllImport("/usr/lib/libobjc.A.dylib")] public static extern void objc_msgSend(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
            [StructLayout(LayoutKind.Sequential)] struct CGPoint { public double X, Y; }
            [DllImport(CoreGraphics)] static extern IntPtr CGEventCreate(IntPtr source);
            [DllImport(CoreGraphics)] static extern CGPoint CGEventGetLocation(IntPtr ev);
            [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr obj);

            public static Point CursorLocation()
            {
                IntPtr ev = CGEventCreate(IntPtr.Zero);
                if (ev == IntPtr.Zero) throw new InvalidOperationException("CGEventCreate failed");
                try { var p = CGEventGetLocation(ev); return new Point(p.X, p.Y); }
                finally { CFRelease(ev); }
            }
        }

        /// <summary>X11: an empty input shape lets clicks through to the windows below.</summary>
        static class X11
        {
            [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
            [DllImport("libX11.so.6")] static extern int XFlush(IntPtr display);
            [DllImport("libXext.so.6")] static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int destKind, int x, int y, IntPtr rects, int n, int op, int ordering);
            [DllImport("libXext.so.6")] static extern void XShapeCombineMask(IntPtr display, IntPtr window, int destKind, int x, int y, IntPtr pixmap, int op);
            [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
            [DllImport("libX11.so.6")]
            static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
            const int ShapeInput = 2, ShapeSet = 0, Unsorted = 0;
            static IntPtr display;

            [DllImport("libX11.so.6")] static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
            [DllImport("libX11.so.6")]
            static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr offset, IntPtr length, bool delete, IntPtr reqType,
                                                 out IntPtr actualType, out int actualFormat, out IntPtr nItems, out IntPtr bytesAfter, out IntPtr prop);
            [DllImport("libX11.so.6")] static extern int XFree(IntPtr data);
            [DllImport("libX11.so.6")]
            static extern bool XTranslateCoordinates(IntPtr display, IntPtr src, IntPtr dest, int x, int y, out int destX, out int destY, out IntPtr child);
            [DllImport("libX11.so.6")]
            static extern int XGetGeometry(IntPtr display, IntPtr d, out IntPtr root, out int x, out int y, out uint w, out uint h, out uint border, out uint depth);

            /// <summary>Items of a 32-bit window property (window ids, atoms); C long-sized in memory.</summary>
            static long[] Property(IntPtr window, string name, int max)
            {
                IntPtr type, n, after, data;
                int format;
                if (XGetWindowProperty(display, window, XInternAtom(display, name, false), IntPtr.Zero, new IntPtr(max), false, IntPtr.Zero,
                                       out type, out format, out n, out after, out data) != 0 || data == IntPtr.Zero) return new long[0];
                try
                {
                    var items = new long[(int)n];
                    for (int i = 0; i < items.Length; i++) items[i] = Marshal.ReadIntPtr(data, i * IntPtr.Size).ToInt64();
                    return format == 32 ? items : new long[0];
                }
                finally { XFree(data); }
            }

            /// <summary>EWMH: is the active window (not ours) full screen and covering the point (x, y)?</summary>
            public static bool ActiveWindowIsFullscreen(IntPtr mine, int x, int y)
            {
                if (display == IntPtr.Zero) display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) return false;
                IntPtr root = XDefaultRootWindow(display);
                var active = Property(root, "_NET_ACTIVE_WINDOW", 1);
                if (active.Length == 0 || active[0] == 0 || active[0] == mine.ToInt64()) return false;
                var win = new IntPtr(active[0]);
                long fullscreen = XInternAtom(display, "_NET_WM_STATE_FULLSCREEN", false).ToInt64();
                if (!Property(win, "_NET_WM_STATE", 64).Contains(fullscreen)) return false;
                IntPtr r, child;
                int gx, gy, ax, ay;
                uint w, h, b, d;
                if (XGetGeometry(display, win, out r, out gx, out gy, out w, out h, out b, out d) == 0) return true;
                if (!XTranslateCoordinates(display, win, root, 0, 0, out ax, out ay, out child)) return true;
                return x >= ax && x < ax + w && y >= ay && y < ay + h;
            }

            public static bool Pointer(out int x, out int y)
            {
                x = y = 0;
                if (display == IntPtr.Zero) display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) return false;
                IntPtr root, child;
                int wx, wy;
                uint mask;
                return XQueryPointer(display, XDefaultRootWindow(display), out root, out child, out x, out y, out wx, out wy, out mask);
            }

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
