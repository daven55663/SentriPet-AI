using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SentriPet
{
    /// <summary>Wires the data service, the widget, the tray icon and the menus together.</summary>
    class Controller
    {
        Application app;
        PetWindow window;
        Tray tray;
        SettingsWindow settingsWindow;
        DispatcherTimer second;
        readonly AlertTracker alerts = new AlertTracker();
        List<ProviderView> views = new List<ProviderView>();
        bool greeted;
        DateTime greetedAt;

        public AppSettings Settings { get; private set; }
        public UsageService Service { get; private set; }
        public List<ProviderView> Views { get { return views; } }
        public PetWindow Widget { get { return window; } }

        public int Run(EventWaitHandle showSignal)
        {
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => { Log.Error("ui", e.Exception); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("fatal", e.ExceptionObject as Exception);
            Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = 30 });
            // A small layered window renders cheaper on the CPU than through a GPU device + read-back,
            // and it avoids the large driver allocations of a D3D device.
            if (!Environment.GetCommandLineArgs().Contains("--gpu"))
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            Styles.Load(app);
            Log.Info("start " + App.Version + (AppPaths.Dev ? " (dev)" : "") + " exe=" + AppPaths.ExeDir);

            Settings = AppSettings.Load();
            if (Settings.DailyRandomTheme) PickDailyTheme(false);
            Autostart.Set(Settings.AutoStart);

            Service = new UsageService(Settings);
            Service.Changed += () => app.Dispatcher.BeginInvoke(new Action(RefreshViews));

            window = new PetWindow(this, Settings);
            window.SetTheme(ThemeCatalog.Get(Settings.Theme).Create());
            tray = new Tray(this);
            window.Show();

            // --show-detail <id>: keep one hover card open for a while (testing placement and stacking order)
            var cmd = Environment.GetCommandLineArgs();
            int sd = Array.IndexOf(cmd, "--show-detail");
            if (sd >= 0 && sd + 1 < cmd.Length) window.ForceDetail(cmd[sd + 1], 45);

            if (cmd.Contains("--autostart"))
            {
                // give the desktop a moment to settle after sign-in
                var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                delay.Tick += (s, e) => { delay.Stop(); Service.Start(); };
                delay.Start();
            }
            else Service.Start();

            second = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            second.Tick += (s, e) =>
            {
                RefreshViews();
                window.Periodic(menuOpen);
                if (Settings.DailyRandomTheme) PickDailyTheme(true);
            };
            second.Start();

            var listener = new Thread(() =>
            {
                while (true)
                {
                    try { showSignal.WaitOne(); }
                    catch { return; }
                    app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        window.ShowWidget();
                        window.Say(null, "我在這裡！");
                    }));
                }
            }) { IsBackground = true };
            listener.Start();

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            app.Run();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            return 0;
        }

        void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            bool locked = e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock || e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect;
            bool unlocked = e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock || e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleConnect;
            if (!locked && !unlocked) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                window.SetPaused(locked);
                if (unlocked)
                {
                    Service.RefreshNow(null);
                    if (Settings.Chatty && views.Count > 0) window.Say(views[0].Id, "歡迎回來！");
                }
            }));
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            app.Dispatcher.BeginInvoke(new Action(() => window.EnsureOnScreen()), DispatcherPriority.Background);
        }

        public void RefreshViews()
        {
            views = Service.BuildViews(Settings, false);
            if (window.Theme != null) window.Theme.Update(views);
            tray.Update(views);
            alerts.Check(this, views);
            if (!greeted && views.Count > 0 && Service.Providers.All(p => Service.SnapshotFor(p.Id) != null || !IsInstalled(p.Id)))
            {
                greeted = true;
                greetedAt = DateTime.UtcNow;
                Greet();
            }
            // reminders wait until the greeting has been read
            if (greeted && (DateTime.UtcNow - greetedAt).TotalSeconds > 8) CheckUseIt();
        }

        bool IsInstalled(string id)
        {
            var d = Service.DetectionFor(id);
            return d != null && d.Installed && Settings.IsEnabled(id);
        }

        void Greet()
        {
            if (window.Theme == null) return;
            if (!Settings.FirstRunDone)
            {
                Settings.FirstRunDone = true;
                Settings.Save();
                window.Say(views[0].Id, Lines.Greeting(views));
                return;
            }
            if (!Settings.Chatty) return;
            int h = DateTime.Now.Hour;
            string hello = h < 5 ? "這麼晚還在寫 code？別熬夜喔" : h < 11 ? "早安！今天也一起努力吧" : h < 14 ? "午安～吃飽了嗎？" : h < 18 ? "下午好，來杯咖啡？" : h < 22 ? "晚上好！" : "夜深了，早點休息喔";
            window.Say(views[0].Id, hello);
        }

        // ------------------------------------------------------------------ alerts

        public void Alert(ProviderView v, Meter m, int level)
        {
            string text = level >= 2 ? Lines.Critical(v, m) : Lines.Warn(v, m);
            window.Say(v.Id, text);
            if (Settings.Notifications) tray.Notify(App.DisplayName, text);
        }

        // ------------------------------------------------------------------ use it or lose it

        readonly Random rng = new Random();
        readonly Dictionary<string, DateTime> nextNudge = new Dictionary<string, DateTime>();

        /// <summary>Minutes between reminders for an urgency level (see ProviderView.UseItLevelFor).</summary>
        static double NudgeMinutes(int level) { return level >= 3 ? 12 : level == 2 ? 25 : 60; }

        /// <summary>
        /// A weekly/monthly window that resets soon with quota left over: the pet keeps reminding you to spend it,
        /// more often as the reset gets closer, and a notification goes out whenever the urgency goes up.
        /// </summary>
        void CheckUseIt()
        {
            if (!Settings.UseItReminder) return;
            var now = DateTime.UtcNow;
            foreach (var v in views)
            {
                var m = v.UseIt;
                if (!v.HasData || v.UseItLevel == 0 || m == null || !m.ResetsAt.HasValue) continue;
                string key = v.Id + "|" + m.Key;
                if (v.UseItLevel > AnnouncedLevel(key, m.ResetsAt.Value))
                {
                    // each level is announced once per window (remembered across restarts)
                    string text = Lines.UseItAlert(v);
                    Log.Info("use-it " + key + " level " + v.UseItLevel + ": " + text);
                    PruneAnnounced(now);
                    Settings.UseItNotified[key] = m.ResetsAt.Value.ToString("o") + "#" + v.UseItLevel;
                    Settings.Save();
                    if (window.CanTalk) window.Say(v.Id, text);
                    if (Settings.Notifications) tray.Notify(App.DisplayName + " · 額度快過期了", text);
                    nextNudge[key] = now.AddMinutes(NudgeMinutes(v.UseItLevel));
                    continue;
                }
                DateTime due;
                if (!nextNudge.TryGetValue(key, out due))
                {
                    // first reminder a few minutes after start-up (not on top of the greeting)
                    nextNudge[key] = now.AddMinutes(Math.Min(6, NudgeMinutes(v.UseItLevel)));
                    continue;
                }
                if (now < due) continue;
                if (!window.CanTalk || menuOpen) { nextNudge[key] = now.AddMinutes(2); continue; }
                nextNudge[key] = now.AddMinutes(NudgeMinutes(v.UseItLevel) * (0.85 + 0.3 * rng.NextDouble()));
                if (v.Active) continue;           // already on it
                window.Say(v.Id, Lines.UseIt(v, rng));
            }
        }

        /// <summary>The level already announced for the window that resets at <paramref name="reset"/> (0 = none).</summary>
        int AnnouncedLevel(string key, DateTime reset)
        {
            string s;
            if (!Settings.UseItNotified.TryGetValue(key, out s) || s == null) return 0;
            int hash = s.LastIndexOf('#');
            DateTime at;
            int level;
            if (hash < 0 || !int.TryParse(s.Substring(hash + 1), out level) ||
                !DateTime.TryParse(s.Substring(0, hash), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out at))
                return 0;
            // same window? (an estimated reset time can move a little between readings)
            return Math.Abs((at.ToUniversalTime() - reset).TotalHours) < 6 ? level : 0;
        }

        /// <summary>Forgets announcements for windows that have long since reset.</summary>
        void PruneAnnounced(DateTime now)
        {
            foreach (var k in Settings.UseItNotified.Keys.ToList())
            {
                string s = Settings.UseItNotified[k] ?? "";
                int hash = s.LastIndexOf('#');
                DateTime at;
                if (hash < 0 || !DateTime.TryParse(s.Substring(0, hash), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out at) ||
                    at.ToUniversalTime() < now.AddDays(-2))
                    Settings.UseItNotified.Remove(k);
            }
        }

        public void CelebrateReset(ProviderView v, Meter m, double previousUsed)
        {
            string text = Lines.Reset(v, m);
            if (window.Theme != null) window.Theme.Celebrate(v.Id);
            window.Say(v.Id, text);
            if (Settings.Notifications && previousUsed >= 50) tray.Notify(App.DisplayName, text);
        }

        // ------------------------------------------------------------------ commands

        public void ChangeTheme(string id, bool manual)
        {
            if (manual) Settings.DailyRandomTheme = false;
            Log.Info("theme -> " + id + (manual ? " (by user)" : ""));
            Settings.Theme = ThemeCatalog.Get(id).Id;
            Settings.Save();
            window.SetTheme(ThemeCatalog.Get(id).Create());
            if (settingsWindow != null) settingsWindow.OnThemeChanged();
        }

        void PickDailyTheme(bool apply)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (Settings.RandomThemeDate == today) return;
            var choices = ThemeCatalog.All.Where(t => t.Id != Settings.Theme).ToList();
            var pick = choices[new Random().Next(choices.Count)];
            Settings.RandomThemeDate = today;
            Settings.Theme = pick.Id;
            Settings.Save();
            if (apply && window != null)
            {
                window.SetTheme(pick.Create());
                window.Say(null, "新的一天，今天換成「" + pick.Name + "」！");
            }
        }

        public void ToggleWidget()
        {
            if (window.UserHidden || !window.IsVisible) window.ShowWidget();
            else window.HideWidget();
        }

        public void ApplyWidgetSettings()
        {
            window.ApplySettings();
            Settings.Save();
        }

        public void OpenSettings()
        {
            if (settingsWindow == null || !settingsWindow.IsLoaded)
            {
                settingsWindow = new SettingsWindow(this);
                settingsWindow.Closed += (s, e) => settingsWindow = null;
                settingsWindow.Show();
            }
            if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
            settingsWindow.Activate();
        }

        public void Quit()
        {
            try { Settings.Save(); } catch { }
            try { tray.Dispose(); } catch { }
            try { Service.Dispose(); } catch { }
            app.Shutdown();
        }

        public static void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("open " + path, ex); }
        }

        // ------------------------------------------------------------------ menu

        static TextBlock Glyph(string g)
        {
            return new TextBlock { Text = g, FontFamily = new FontFamily(Styles.IconFont), FontSize = 14, Foreground = G.B(Palette.Hex("#AEB6C6")), VerticalAlignment = VerticalAlignment.Center };
        }

        MenuItem Item(string header, string glyph, Action click)
        {
            var mi = new MenuItem { Header = header };
            if (glyph != null) mi.Icon = Glyph(glyph);
            if (click != null)
                mi.Click += (s, e) =>
                {
                    try { click(); }
                    catch (Exception ex) { Log.Error("menu " + header, ex); }
                };
            return mi;
        }

        MenuItem Toggle(string header, string glyph, bool value, Action<bool> set)
        {
            var mi = Item(header, glyph, () => { set(!value); Settings.Save(); });
            mi.IsChecked = value;
            return mi;
        }

        bool menuOpen;

        /// <summary>True while one of our context menus is open (the widget stays out of its way).</summary>
        public bool MenuOpen { get { return menuOpen; } }

        public void ShowMenu(bool fromTray = false)
        {
            var menu = BuildMenu();
            menu.Opened += (s, e) => menuOpen = true;
            menu.Closed += (s, e) => menuOpen = false;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            if (fromTray)
            {
                var src = PresentationSource.FromVisual(menu) as HwndSource;
                if (src != null) Native.SetForegroundWindow(src.Handle);
            }
        }

        /// <summary>--snapshot-ui outDir: renders the menu and the settings page off-screen (for checking the design).</summary>
        public int SnapshotUi(string outDir)
        {
            Directory.CreateDirectory(outDir);
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Styles.Load(app);
            Settings = AppSettings.Load();
            Service = new UsageService(Settings);
            Service.Start();
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 25)
            {
                Thread.Sleep(200);
                bool detected = Service.Providers.All(p => Service.DetectionFor(p.Id) != null);
                if (detected && Service.Providers.All(p => !IsInstalled(p.Id) || Service.SnapshotFor(p.Id) != null)) break;
            }
            views = Service.BuildViews(Settings, false);
            window = new PetWindow(this, Settings);
            window.SetTheme(ThemeCatalog.Get(Settings.Theme).Create());

            var menu = BuildMenu();
            menu.ApplyTemplate();
            Snapshots.Render(menu, Path.Combine(outDir, "menu.png"), 1.5, false);
            var sub = (MenuItem)menu.Items[2];
            var subMenu = new ContextMenu { Style = (Style)app.FindResource("PetMenu") };
            foreach (var item in sub.Items.Cast<object>().ToList())
            {
                sub.Items.Remove(item);
                subMenu.Items.Add(item);
            }
            Snapshots.Render(subMenu, Path.Combine(outDir, "menu_themes.png"), 1.5, false);
            if (views.Count > 0)
            {
                var detail = DetailCardView.Build(views[0]);
                detail.SetPointer(DetailPlacement.Side.Above, 90);
                Snapshots.Render(detail.Root, Path.Combine(outDir, "detail.png"), 1.5, false);
            }

            var win = new SettingsWindow(this);
            win.RenderPreviewsNow();
            var scroll = (ScrollViewer)win.Content;
            var page = (FrameworkElement)scroll.Content;
            scroll.Content = null;
            var hostBorder = new Border { Background = win.Background, Width = 700, Child = page };
            TextOptions.SetTextFormattingMode(hostBorder, TextFormattingMode.Display);
            Snapshots.Render(hostBorder, Path.Combine(outDir, "settings.png"), 1.0, false);
            Service.Dispose();
            return 0;
        }

        ContextMenu BuildMenu()
        {
            var menu = new ContextMenu { Style = (Style)app.FindResource("PetMenu") };

            var title = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            title.Children.Add(G.T(App.DisplayName, 13.5, Colors.White, FontWeights.Bold, G.Ui));
            string sum = views.Count == 0 ? "正在偵測 AI…" : string.Join("  ·  ", views.Select(v => v.Summary));
            title.Children.Add(G.T(sum, 11, Palette.Hex("#8F98A8")));
            menu.Items.Add(new MenuItem { Header = title, Icon = Glyph(""), IsHitTestVisible = false });
            menu.Items.Add(new Separator());

            var themes = Item("換造型", "", null);
            foreach (var t in ThemeCatalog.All)
            {
                var info = t;
                var mi = Item(t.Name, null, () => ChangeTheme(info.Id, true));
                mi.InputGestureText = t.Mood;
                mi.IsChecked = Settings.Theme == t.Id;
                themes.Items.Add(mi);
            }
            themes.Items.Add(new Separator());
            themes.Items.Add(Toggle("每天隨機換一個", "", Settings.DailyRandomTheme, v =>
            {
                Settings.DailyRandomTheme = v;
                Settings.RandomThemeDate = null;
                if (v) PickDailyTheme(true);
            }));
            menu.Items.Add(themes);

            var mood = Item("今天心情如何？", "", null);
            foreach (var t in ThemeCatalog.All)
            {
                var info = t;
                var mi = Item(t.Mood, null, () =>
                {
                    ChangeTheme(info.Id, true);
                    window.Say(null, "收到！今天是「" + info.Mood + "」模式 ✦");
                });
                mi.InputGestureText = t.Name;
                mood.Items.Add(mi);
            }
            mood.Items.Add(new Separator());
            mood.Items.Add(Item("交給命運吧（隨機）", "", () =>
            {
                var choices = ThemeCatalog.All.Where(x => x.Id != Settings.Theme).ToList();
                var pick = choices[new Random().Next(choices.Count)];
                ChangeTheme(pick.Id, true);
                window.Say(null, "命運選擇了「" + pick.Name + "」！");
            }));
            menu.Items.Add(mood);
            menu.Items.Add(Item("立即更新", "", () =>
            {
                Service.RefreshNow(null);
                if (views.Count > 0) window.Say(views[0].Id, "更新中…");
            }));
            menu.Items.Add(new Separator());

            var size = Item("大小", "", null);
            foreach (var z in new[] { 0.7, 0.85, 1.0, 1.15, 1.3, 1.5, 1.75, 2.0 })
            {
                double zz = z;
                var mi = Item(Math.Round(z * 100) + "%", null, () => { Settings.Scale = zz; ApplyWidgetSettings(); });
                mi.IsChecked = Math.Abs(Settings.Scale - z) < 0.01;
                size.Items.Add(mi);
            }
            menu.Items.Add(size);

            var opacity = Item("透明度", "", null);
            foreach (var o in new[] { 1.0, 0.9, 0.75, 0.6, 0.45 })
            {
                double oo = o;
                var mi = Item(o >= 1 ? "不透明" : Math.Round(o * 100) + "%", null, () => { Settings.Opacity = oo; ApplyWidgetSettings(); });
                mi.IsChecked = Math.Abs(Settings.Opacity - o) < 0.01;
                opacity.Items.Add(mi);
            }
            menu.Items.Add(opacity);

            var monitors = Item("移到螢幕", "", null);
            var screens = Forms.Screen.AllScreens.OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).ToList();
            var current = window.CurrentScreen;
            for (int i = 0; i < screens.Count; i++)
            {
                var sc = screens[i];
                string pos = screens.Count == 1 ? "" : i == 0 ? "左" : i == screens.Count - 1 ? "右" : "中";
                var mi = Item("螢幕 " + (i + 1) + (pos.Length > 0 ? "（" + pos + "）" : "") + (sc.Primary ? " · 主螢幕" : ""), null, () => window.MoveToScreen(sc));
                mi.InputGestureText = sc.Bounds.Width + "×" + sc.Bounds.Height;
                mi.IsChecked = current != null && current.DeviceName == sc.DeviceName;
                monitors.Items.Add(mi);
            }
            menu.Items.Add(monitors);
            menu.Items.Add(Toggle("永遠在最上層", "", Settings.AlwaysOnTop, v => { Settings.AlwaysOnTop = v; window.ApplySettings(); }));
            menu.Items.Add(Toggle("滑鼠穿透（不擋點擊）", "", Settings.ClickThrough, v =>
            {
                Settings.ClickThrough = v;
                window.ApplySettings();
                if (v) tray.Notify(App.DisplayName, "已開啟滑鼠穿透：桌寵不會擋住點擊。要關閉請在右下角系統匣圖示按右鍵。");
            }));
            menu.Items.Add(Toggle("會說話", "", Settings.Chatty, v => Settings.Chatty = v));
            menu.Items.Add(Toggle("額度提醒通知", "", Settings.Notifications, v => Settings.Notifications = v));
            menu.Items.Add(Toggle("催我用完週額度（重置前提醒）", "", Settings.UseItReminder, v => { Settings.UseItReminder = v; RefreshViews(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("設定…", "", OpenSettings));
            menu.Items.Add(Toggle("開機自動啟動", "", Settings.AutoStart, v => { Settings.AutoStart = v; Autostart.Set(v); }));
            menu.Items.Add(Item(window.UserHidden ? "顯示桌寵" : "先藏起來（點系統匣叫回）", "", ToggleWidget));
            menu.Items.Add(Item("結束", "", Quit));
            return menu;
        }
    }

    /// <summary>Notices when a window crosses the warning thresholds or resets.</summary>
    class AlertTracker
    {
        readonly Dictionary<string, double> used = new Dictionary<string, double>();
        readonly Dictionary<string, int> level = new Dictionary<string, int>();

        public void Check(Controller c, List<ProviderView> views)
        {
            var s = c.Settings;
            foreach (var v in views)
            {
                if (!v.HasData) continue;
                foreach (var m in v.Meters)
                {
                    if (m.Unlimited) continue;
                    string key = v.Id + "|" + m.Key;
                    int lv = m.Used >= s.CriticalAt ? 2 : m.Used >= s.WarnAt ? 1 : 0;
                    double prevUsed;
                    int prevLv;
                    if (used.TryGetValue(key, out prevUsed) && level.TryGetValue(key, out prevLv))
                    {
                        if (lv > prevLv) c.Alert(v, m, lv);
                        else if (prevUsed >= 30 && m.Used <= 3) c.CelebrateReset(v, m, prevUsed);
                    }
                    used[key] = m.Used;
                    level[key] = lv;
                }
            }
        }
    }
}
