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
    class DesktopController
    {
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
        }

        public void Start()
        {
            Settings = AppSettings.Load();
            Program.UseLanguage(Program.LanguageOverride ?? Settings.Language);
            Log.Info("language " + L.Current);
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
            Service.Start();

            second = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => { RefreshViews(); window.Periodic(); });
            second.Start();
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
            window.Say(v.Id, level >= 2 ? Lines.Critical(v, m) : Lines.Warn(v, m));
        }

        void CelebrateReset(ProviderView v, Meter m, double previousUsed)
        {
            if (window.CurrentTheme != null) window.CurrentTheme.Celebrate(v.Id);
            window.Say(v.Id, Lines.Reset(v, m));
        }

        void CheckUseIt()
        {
            foreach (var e in useIt.Check(Settings, views, DateTime.UtcNow, window.IsVisible && !menuOpen, rng))
            {
                if (e.Announce)
                {
                    Log.Info("use-it " + e.View.Id + "|" + e.View.UseIt.Key + " level " + e.View.UseItLevel + ": " + e.Text);
                    Settings.Save();
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

        void UpdateTray()
        {
            if (tray == null) return;
            string tip = views.Count == 0 ? AppInfo.Name : string.Join(" · ", views.Select(v => v.Summary));
            if (tray.ToolTipText != tip) tray.ToolTipText = tip;
        }

        NativeMenu BuildTrayMenu()
        {
            var m = new NativeMenu();
            m.Add(Native(L.T("顯示／隱藏桌寵"), ToggleWidget));
            m.Add(Native(L.T("立即更新"), () => Service.RefreshNow(null)));
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
            items.Add(Toggle(L.T("會說話"), Settings.Chatty, () => { Settings.Chatty = !Settings.Chatty; Settings.Save(); }));
            items.Add(Toggle(L.T("催我用完週額度（重置前提醒）"), Settings.UseItReminder, () => { Settings.UseItReminder = !Settings.UseItReminder; Settings.Save(); RefreshViews(); }));
            items.Add(new Separator());
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
            window.Say(null, L.T("好的！之後就用這個語言跟你聊天 ✦"));
        }

        public void ToggleWidget()
        {
            if (window.IsVisible) window.Hide();
            else window.Show();
        }

        public void Quit()
        {
            try { Settings.Save(); } catch { }
            try { if (tray != null) tray.Dispose(); } catch { }
            try { Service.Dispose(); } catch { }
            desktop.Shutdown();
        }
    }
}
