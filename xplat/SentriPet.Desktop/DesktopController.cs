using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SentriPet
{
    /// <summary>Wires the data service, the widget, the tray icon and the menus together (Avalonia version).</summary>
    class DesktopController : ISettingsHost
    {
        /// <summary>The running controller (the single-instance pipe asks it to show the widget).</summary>
        public static DesktopController Instance { get; private set; }
        SettingsWindow settingsWindow;
        readonly Application app;
        readonly IClassicDesktopStyleApplicationLifetime desktop;
        readonly AlertTracker alerts = new AlertTracker();
        readonly UseItTracker useIt = new UseItTracker();
        readonly Random rng = new Random();
        List<ProviderView> views = new List<ProviderView>();
        PetWindow window;
        TrayIcon tray;
        DispatcherTimer second;
        bool greeted, menuOpen;
        DateTime greetedAt;

        public AppSettings Settings { get; private set; }
        public UsageService Service { get; private set; }
        public List<ProviderView> Views { get { return views; } }
        /// <summary>True while a menu is open (the widget keeps its hover card out of the way).</summary>
        public bool MenuOpen { get { return menuOpen; } }

        public DesktopController(Application app, IClassicDesktopStyleApplicationLifetime desktop)
        {
            this.app = app;
            this.desktop = desktop;
            Instance = this;
        }

        public void Start()
        {
            Settings = AppSettings.Load();
            Program.UseLanguage(Program.LanguageOverride ?? Settings.Language);
            Log.Info("language " + L.Current);
            if (Settings.DailyRandomTheme) PickDailyTheme(false);
            Integration.SetAutostart(Settings.AutoStart);
            Integration.EnsureStartMenuShortcut();
            if (!AppPaths.Dev) ClaudeStatusLine.Repair(Settings, Environment.ProcessPath);
            Service = new UsageService(Settings);
            Service.Changed += () => Dispatcher.UIThread.Post(RefreshViews);

            window = new PetWindow(this, Settings);
            window.SetTheme(ThemeCatalog.Get(Settings.Theme).Create());
            window.Show();
            // --show-detail <id>: keep one hover card open for a while (testing placement and stacking order)
            var args = Environment.GetCommandLineArgs();
            int sd = Array.IndexOf(args, "--show-detail");
            if (sd >= 0 && sd + 1 < args.Length) window.ForceDetail(args[sd + 1], 45);
            CreateTray();
            if (Array.IndexOf(args, "--autostart") >= 0)
            {
                // give the desktop a moment to settle after sign-in
                var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                delay.Tick += (s, e) => { delay.Stop(); Service.Start(); };
                delay.Start();
            }
            else Service.Start();

            second = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) =>
            {
                RefreshViews();
                window.Periodic(menuOpen);
                if (Settings.DailyRandomTheme) PickDailyTheme(true);
            });
            second.Start();

            // --smoke-test FILE: run for real for a while, then report whether it worked and quit (CI, on every system)
            int st = Array.IndexOf(args, "--smoke-test");
            if (st >= 0 && st + 1 < args.Length)
            {
                string report = args[st + 1];
                var done = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                done.Tick += (s, e) => { done.Stop(); SmokeReport(report); };
                done.Start();
            }
        }

        /// <summary>What a person would check after starting the program: the widget is on screen, animates, has a theme, no errors.</summary>
        void SmokeReport(string file)
        {
            var lines = new List<string>();
            int failed = 0;
            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (!ok) failed++;
                lines.Add((ok ? "PASS " : "FAIL ") + name + (detail != null ? "  — " + detail : ""));
            };
            var screens = window.Screens.All;
            var pos = window.Position;
            check("widget window is shown", window.IsVisible, null);
            check("widget is on a screen", screens.Any(s => s.Bounds.Contains(pos)), pos + " in " + string.Join(", ", screens.Select(s => s.Bounds.ToString())));
            if (window.PlacedInDefaultCorner)
            {
                var wa = (window.Screens.ScreenFromWindow(window) ?? screens.FirstOrDefault()).WorkingArea;
                check("first start: bottom-right corner", pos.X > wa.X + wa.Width / 2 && pos.Y > wa.Y + wa.Height / 2, pos + " in work area " + wa);
            }
            check("animation frames drawn", window.Frames > 30, window.Frames + " frames");
            check("theme attached", window.CurrentTheme != null && window.CurrentTheme.Root != null, window.CurrentTheme != null ? window.CurrentTheme.Id : "none");
            check("tray / menu-bar icon", tray != null || !Os.Linux, tray != null ? "created" : "not available on this desktop");
            check("no errors logged", Log.Errors == 0, Log.Errors + " error(s), see " + System.IO.Path.Combine(AppPaths.LogDir, "app.log"));
            lines.Insert(0, AppInfo.Name + " " + AppInfo.Version + " smoke test on " + Os.Name + ": " + views.Count + " AI(s), language " + L.Current);
            lines.Add(failed == 0 ? "ALL PASS" : failed + " FAILED");
            try { System.IO.File.WriteAllLines(file, lines); } catch (Exception ex) { Log.Error("smoke report", ex); }
            Quit(failed);
        }

        /// <summary>A second copy was started: bring the widget back.</summary>
        public void ShowFromOtherCopy()
        {
            window.UserHidden = false;
            window.UpdateVisibility();
            window.Say(null, L.T("我在這裡！"));
        }

        void PickDailyTheme(bool apply)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (Settings.RandomThemeDate == today) return;
            var choices = ThemeCatalog.All.Where(t => t.Id != Settings.Theme).ToList();
            var pick = choices[rng.Next(choices.Count)];
            Settings.RandomThemeDate = today;
            Settings.Theme = pick.Id;
            Settings.Save();
            if (apply && window != null)
            {
                window.SetTheme(pick.Create());
                window.Say(null, L.F("新的一天，今天換成「{0}」！", pick.Name));
                if (settingsWindow != null) settingsWindow.OnThemeChanged();
            }
        }

        public void RefreshViews()
        {
            views = Service.BuildViews(Settings, false);
            if (window.CurrentTheme != null) window.CurrentTheme.Update(views);
            UpdateTray();
            alerts.Check(Settings, views, Alert, CelebrateReset);
            if (!greeted && views.Count > 0 && Service.Providers.All(p => Service.SnapshotFor(p.Id) != null || !IsInstalled(p.Id)))
            {
                greeted = true;
                greetedAt = DateTime.UtcNow;
                Greet();
            }
            if (greeted && (DateTime.UtcNow - greetedAt).TotalSeconds > 8) CheckUseIt();
        }

        bool IsInstalled(string id)
        {
            var d = Service.DetectionFor(id);
            return d != null && d.Installed && Settings.IsEnabled(id);
        }

        void Greet()
        {
            if (!Settings.FirstRunDone)
            {
                Settings.FirstRunDone = true;
                Settings.Save();
                window.Say(views[0].Id, Lines.Greeting(views));
                return;
            }
            if (!Settings.Chatty) return;
            int h = DateTime.Now.Hour;
            window.Say(views[0].Id, h < 5 ? L.T("這麼晚還在寫 code？別熬夜喔") : h < 11 ? L.T("早安！今天也一起努力吧") : h < 14 ? L.T("午安～吃飽了嗎？") : h < 18 ? L.T("下午好，來杯咖啡？") : h < 22 ? L.T("晚上好！") : L.T("夜深了，早點休息喔"));
        }

        void Alert(ProviderView v, Meter m, int level)
        {
            string text = level >= 2 ? Lines.Critical(v, m) : Lines.Warn(v, m);
            window.Say(v.Id, text);
            if (Settings.Notifications) Integration.Notify(AppInfo.Name, text);
        }

        void CelebrateReset(ProviderView v, Meter m, double previousUsed)
        {
            string text = Lines.Reset(v, m);
            if (window.CurrentTheme != null) window.CurrentTheme.Celebrate(v.Id);
            window.Say(v.Id, text);
            if (Settings.Notifications && previousUsed >= 50) Integration.Notify(AppInfo.Name, text);
        }

        void CheckUseIt()
        {
            foreach (var e in useIt.Check(Settings, views, DateTime.UtcNow, window.IsVisible && !menuOpen, rng))
            {
                if (e.Announce)
                {
                    Log.Info("use-it " + e.View.Id + "|" + e.View.UseIt.Key + " level " + e.View.UseItLevel + ": " + e.Text);
                    Settings.Save();
                    if (Settings.Notifications) Integration.Notify(AppInfo.Name + " · " + L.T("額度快過期了"), e.Text);
                }
                window.Say(e.View.Id, e.Text);
            }
        }

        // ------------------------------------------------------------------ tray and menus

        void CreateTray()
        {
            try
            {
                tray = new TrayIcon
                {
                    Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SentriPet/Assets/app.ico"))),
                    ToolTipText = AppInfo.Name,
                    Menu = BuildTrayMenu(),
                    IsVisible = true,
                };
                tray.Clicked += (s, e) => ToggleWidget();
                TrayIcon.SetIcons(app, new TrayIcons { tray });
            }
            catch (Exception ex)
            {
                // some Linux desktops have no tray; the widget's own menu still works
                Log.Warn("tray icon unavailable: " + ex.Message);
            }
        }

        string traySig;

        void UpdateTray()
        {
            if (tray == null) return;
            // the icon is a tiny jelly filled to the lowest remaining quota (blue dot: an AI is working)
            var limited = views.Where(v => v.HasData && !v.Unlimited).ToList();
            double min = limited.Count > 0 ? limited.Min(v => v.Remaining) : -1;
            bool active = views.Any(v => v.Active);
            string sig = (int)Math.Round(min) + (active ? "a" : "");
            if (sig != traySig)
            {
                traySig = sig;
                try { tray.Icon = new WindowIcon(TrayArt.Draw(min, active, 64)); }
                catch (Exception ex) { Log.Warn("tray icon: " + ex.Message); }
            }
            string tip = views.Count == 0 ? AppInfo.Name : string.Join(" · ", views.Select(v => v.Summary));
            if (tip.Length > 120) tip = tip.Substring(0, 119) + "…";
            if (tray.ToolTipText != tip) tray.ToolTipText = tip;
        }

        NativeMenu BuildTrayMenu()
        {
            var m = new NativeMenu();
            m.Add(Native(L.T("顯示／隱藏桌寵"), ToggleWidget));
            m.Add(Native(L.T("立即更新"), () => Service.RefreshNow(null)));
            if (Integration.CanClickThrough)
            {
                // the only way back when the widget lets clicks through
                var ct = new NativeMenuItem(L.T("滑鼠穿透（不擋點擊）")) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = Settings.ClickThrough };
                ct.Click += (s, e) => { Settings.ClickThrough = !Settings.ClickThrough; ApplyWidgetSettings(); tray.Menu = BuildTrayMenu(); };
                m.Add(ct);
            }
            m.Add(Native(L.T("設定…"), OpenSettings));
            m.Add(new NativeMenuItemSeparator());
            m.Add(Native(L.T("結束"), Quit));
            return m;
        }

        static NativeMenuItem Native(string header, Action click)
        {
            var mi = new NativeMenuItem(header);
            mi.Click += (s, e) => click();
            return mi;
        }

        public void ShowMenu(Control target)
        {
            var menu = new ContextMenu();
            var items = new List<object>();
            string sum = views.Count == 0 ? L.T("正在偵測 AI…") : string.Join("  ·  ", views.Select(v => v.Summary));
            items.Add(new MenuItem { Header = sum, IsEnabled = false });
            items.Add(new Separator());
            var themes = new MenuItem { Header = L.T("換造型") };
            var themeItems = new List<object>();
            foreach (var t in ThemeCatalog.All)
            {
                var info = t;
                var mi = Toggle(t.Name, Settings.Theme == t.Id, () => ChangeTheme(info.Id));
                mi.ToggleType = MenuItemToggleType.Radio;
                themeItems.Add(mi);
            }
            themeItems.Add(new Separator());
            themeItems.Add(Toggle(L.T("每天隨機換一個"), Settings.DailyRandomTheme, () =>
            {
                Settings.DailyRandomTheme = !Settings.DailyRandomTheme;
                Settings.RandomThemeDate = null;
                Settings.Save();
                if (Settings.DailyRandomTheme) PickDailyTheme(true);
            }));
            themes.ItemsSource = themeItems;
            items.Add(themes);

            var mood = new MenuItem { Header = L.T("今天心情如何？") };
            var moodItems = new List<object>();
            foreach (var t in ThemeCatalog.All)
            {
                var info = t;
                moodItems.Add(Item(t.Mood, () => { ChangeTheme(info.Id); window.Say(null, L.F("收到！今天是「{0}」模式 ✦", info.Mood)); }));
            }
            moodItems.Add(new Separator());
            moodItems.Add(Item(L.T("交給命運吧（隨機）"), () =>
            {
                var choices = ThemeCatalog.All.Where(x => x.Id != Settings.Theme).ToList();
                var pick = choices[rng.Next(choices.Count)];
                ChangeTheme(pick.Id);
                window.Say(null, L.F("命運選擇了「{0}」！", pick.Name));
            }));
            mood.ItemsSource = moodItems;
            items.Add(mood);
            items.Add(Item(L.T("立即更新"), () => { Service.RefreshNow(null); if (views.Count > 0) window.Say(views[0].Id, L.T("更新中…")); }));

            var size = new MenuItem { Header = L.T("大小") };
            var sizes = new List<object>();
            foreach (var z in new[] { 0.7, 0.85, 1.0, 1.15, 1.3, 1.5, 1.75, 2.0 })
            {
                double zz = z;
                sizes.Add(Toggle(Math.Round(z * 100) + "%", Math.Abs(Settings.Scale - z) < 0.01, () => { Settings.Scale = zz; window.ApplySettings(); Settings.Save(); }));
            }
            size.ItemsSource = sizes;
            items.Add(size);

            var opacity = new MenuItem { Header = L.T("透明度") };
            var levels = new List<object>();
            foreach (var o in new[] { 1.0, 0.9, 0.75, 0.6, 0.45 })
            {
                double oo = o;
                levels.Add(Toggle(o >= 1 ? L.T("不透明") : Math.Round(o * 100) + "%", Math.Abs(Settings.Opacity - o) < 0.01, () => { Settings.Opacity = oo; ApplyWidgetSettings(); }));
            }
            opacity.ItemsSource = levels;
            items.Add(opacity);

            var screens = window.Screens.All.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToList();
            if (screens.Count > 1)
            {
                var move = new MenuItem { Header = L.T("移到螢幕") };
                var list = new List<object>();
                for (int i = 0; i < screens.Count; i++)
                {
                    var sc = screens[i];
                    list.Add(Item(L.F("螢幕 {0}", i + 1) + (sc.IsPrimary ? " · " + L.T("主螢幕") : ""), () => window.MoveToScreen(sc)));
                }
                move.ItemsSource = list;
                items.Add(move);
            }
            var lang = new MenuItem { Header = L.LanguageLabel };
            var langs = new List<object>();
            langs.Add(LanguageItem("auto", L.T("自動（跟隨系統）")));
            langs.Add(new Separator());
            foreach (var li in L.Languages) langs.Add(LanguageItem(li.Code, li.Native));
            lang.ItemsSource = langs;
            items.Add(lang);
            items.Add(Toggle(L.T("永遠在最上層"), Settings.AlwaysOnTop, () => { Settings.AlwaysOnTop = !Settings.AlwaysOnTop; window.ApplySettings(); Settings.Save(); }));
            if (Integration.CanClickThrough)
                items.Add(Toggle(L.T("滑鼠穿透（不擋點擊）"), Settings.ClickThrough, () =>
                {
                    Settings.ClickThrough = !Settings.ClickThrough;
                    ApplyWidgetSettings();
                    if (tray != null) tray.Menu = BuildTrayMenu();
                    if (Settings.ClickThrough && Settings.Notifications) Integration.Notify(AppInfo.Name, L.T("已開啟滑鼠穿透：桌寵不會擋住點擊。要關閉請在右下角系統匣圖示按右鍵。"));
                }));
            items.Add(Toggle(L.T("會說話"), Settings.Chatty, () => { Settings.Chatty = !Settings.Chatty; Settings.Save(); }));
            items.Add(Toggle(L.T("額度提醒通知"), Settings.Notifications, () => { Settings.Notifications = !Settings.Notifications; Settings.Save(); }));
            items.Add(Toggle(L.T("催我用完週額度（重置前提醒）"), Settings.UseItReminder, () => { Settings.UseItReminder = !Settings.UseItReminder; Settings.Save(); RefreshViews(); }));
            items.Add(new Separator());
            items.Add(Item(L.T("設定…"), OpenSettings));
            items.Add(Toggle(L.T("開機自動啟動"), Settings.AutoStart, () => { Settings.AutoStart = !Settings.AutoStart; Integration.SetAutostart(Settings.AutoStart); Settings.Save(); }));
            items.Add(Item(L.T("先藏起來（點系統匣叫回）"), ToggleWidget));
            items.Add(Item(L.T("結束"), Quit));
            menu.ItemsSource = items;
            menu.Opened += (s, e) => menuOpen = true;
            menu.Closed += (s, e) => menuOpen = false;
            menu.Open(target);
        }

        static MenuItem Item(string header, Action click)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (s, e) =>
            {
                try { click(); }
                catch (Exception ex) { Log.Error("menu " + header, ex); }
            };
            return mi;
        }

        static MenuItem Toggle(string header, bool on, Action click)
        {
            var mi = Item(header, click);
            mi.ToggleType = MenuItemToggleType.CheckBox;
            mi.IsChecked = on;
            return mi;
        }

        public void ChangeTheme(string id)
        {
            Log.Info("theme -> " + id);
            Settings.Theme = ThemeCatalog.Get(id).Id;
            Settings.DailyRandomTheme = false;
            Settings.Save();
            window.SetTheme(ThemeCatalog.Get(id).Create());
            RefreshViews();
            if (settingsWindow != null) settingsWindow.OnThemeChanged();
        }

        public void ApplyWidgetSettings()
        {
            window.ApplySettings();
            Settings.Save();
        }

        public void OpenSettings()
        {
            if (settingsWindow == null)
            {
                settingsWindow = new SettingsWindow(this);
                settingsWindow.Closed += (s, e) => settingsWindow = null;
                settingsWindow.Show();
            }
            if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
            settingsWindow.Activate();
        }

        MenuItem LanguageItem(string code, string label)
        {
            var mi = Toggle(label, (Settings.Language ?? "auto") == code, () => ChangeLanguage(code));
            mi.ToggleType = MenuItemToggleType.Radio;
            return mi;
        }

        /// <summary>Switches the language on the fly: texts, fonts, the widget and the tray menu are rebuilt.</summary>
        public void ChangeLanguage(string code)
        {
            Log.Info("language -> " + code);
            Settings.Language = code;
            Settings.Save();
            Program.UseLanguage(code);
            window.SetTheme(ThemeCatalog.Get(Settings.Theme).Create());
            Service.RefreshNow(null);   // provider texts (labels, notes, errors) are made in the new language
            RefreshViews();
            if (tray != null) tray.Menu = BuildTrayMenu();
            if (settingsWindow != null)
            {
                var old = settingsWindow;
                var pos = old.Position;
                double height = old.Height;
                settingsWindow = null;
                old.Close();
                OpenSettings();
                settingsWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                settingsWindow.Position = pos;
                settingsWindow.Height = height;
            }
            window.Say(null, L.T("好的！之後就用這個語言跟你聊天 ✦"));
        }

        public void ToggleWidget()
        {
            window.UserHidden = window.IsVisible;
            window.UpdateVisibility();
        }

        public void Quit() { Quit(0); }

        public void Quit(int exitCode)
        {
            try { Settings.Save(); } catch { }
            try { if (tray != null) tray.Dispose(); } catch { }
            try { if (settingsWindow != null) settingsWindow.Close(); } catch { }
            try { Service.Dispose(); } catch { }
            desktop.Shutdown(exitCode);
        }
    }
}
