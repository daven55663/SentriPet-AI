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
    class DesktopController : ISettingsHost, IReportHost
    {
        /// <summary>The running controller (the single-instance pipe asks it to show the widget).</summary>
        public static DesktopController Instance { get; private set; }
        SettingsWindow settingsWindow;
        ReportWindow reportWindow;
        TokenLedger ledger;

        /// <summary>The token counts of the last month (#18), read when the report is first opened.</summary>
        public TokenLedger Ledger { get { return ledger ?? (ledger = TokenLedger.ForThisComputer()); } }
        readonly Application app;
        readonly IClassicDesktopStyleApplicationLifetime desktop;
        readonly AlertTracker alerts = new AlertTracker();
        readonly UseItTracker useIt = new UseItTracker();
        readonly HashSet<string> runsOutAnnounced = new HashSet<string>();
        readonly List<WindowResult> pendingSummaries = new List<WindowResult>();
        readonly List<string> pendingGrowth = new List<string>();   // level-ups and achievements to announce (#23)
        DateTime nextGrowthNews;
        Progress progress;
        readonly AgentHooks.Reader claudeEvents = new AgentHooks.Reader(AgentHooks.ClaudeEventsFile);
        readonly AgentHooks.Reader codexEvents = new AgentHooks.Reader(AgentHooks.CodexEventsFile);
        readonly Random rng = new Random();
        List<ProviderView> views = new List<ProviderView>();
        readonly UsageExporter exporter = new UsageExporter(UsageExport.File);
        UsageServer server;
        readonly object petGate = new object();
        byte[] petPicture;
        DateTime petPictureAt;
        PetWindow window;
        TrayIcon tray;
        DispatcherTimer second;
        bool greeted, menuOpen;
        DateTime greetedAt;

        public AppSettings Settings { get; private set; }
        public UsageService Service { get; private set; }
        public List<ProviderView> Views { get { return views; } }
        public UsageHistory History { get; private set; }
        /// <summary>True while a menu is open (the widget keeps its hover card out of the way).</summary>
        public bool MenuOpen { get { return menuOpen; } }

        /// <summary>Quiet time (#17): no notifications and nothing said unprompted.</summary>
        public bool IsQuiet { get { return Settings != null && Quiet.IsQuiet(Settings, DateTime.UtcNow); } }

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
            if (!AppPaths.Dev)
            {
                ClaudeStatusLine.Repair(Settings, Environment.ProcessPath);
                AgentHooks.Repair(Settings, Environment.ProcessPath);
            }
            // --connect-claude-statusline / --disconnect-claude-statusline: the settings switch, for scripts and helpers
            try
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--connect-claude-statusline") >= 0)
                {
                    ClaudeStatusLine.Connect(Settings, Environment.ProcessPath);
                    Settings.Save();
                    Log.Info("status line bridge connected: " + ClaudeStatusLine.CommandFor(Environment.ProcessPath));
                }
                else if (Array.IndexOf(Environment.GetCommandLineArgs(), "--disconnect-claude-statusline") >= 0)
                {
                    ClaudeStatusLine.Disconnect(Settings);
                    Settings.Save();
                    Log.Info("status line bridge disconnected");
                }
            }
            catch (Exception ex) { Log.Error("status line bridge", ex); }
            // --connect-agent-hooks / --disconnect-agent-hooks: the "done / waiting for you" switch (#14), likewise
            try
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--connect-agent-hooks") >= 0)
                {
                    AgentHooks.Connect(Settings, Environment.ProcessPath, AgentHooks.HasClaude, AgentHooks.HasCodex);
                    Settings.Save();
                    Log.Info("agent hooks connected: claude " + AgentHooks.IsClaudeConnected() + ", codex " + AgentHooks.IsCodexConnected() +
                             (Settings.CodexNotifyChain != null ? " (runs the notify program Codex had)" : ""));
                }
                else if (Array.IndexOf(Environment.GetCommandLineArgs(), "--disconnect-agent-hooks") >= 0)
                {
                    AgentHooks.Disconnect(Settings);
                    Settings.Save();
                    Log.Info("agent hooks disconnected");
                }
            }
            catch (Exception ex) { Log.Error("agent hooks", ex); }
            History = new UsageHistory(UsageHistory.DefaultFile);
            progress = new Progress(Progress.DefaultFile);
            if (!progress.Existed)
            {
                // the first start with growing pets (#23): the windows already in the report count
                int lv = progress.Backfill(History.Results);
                progress.Save();
                Log.Info("growing pets: level " + lv + " from " + History.Results.Count + " past window(s)");
                if (lv >= 2 && Settings.Growth) pendingGrowth.Add(L.F("照過去的紀錄，大家已經是 Lv {0} 了！", lv));
            }
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
            ApplyTrayMode();
            ApplyExportSettings();
            ApplyGrowth();
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
                // the local web page for OBS (#22) on a free port, asked like a browser would: the JSON and the widget's picture
                var web = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
                web.Tick += (s, e) =>
                {
                    web.Stop();
                    if (server == null) server = new UsageServer(() => exporter.Latest, PetPng);
                    if (!server.Running) server.Start(0);
                    Export(DateTime.UtcNow);
                    int port = server.Port;
                    System.Threading.Tasks.Task.Run(() => { smokeJson = Fetch(port, "/usage.json"); smokePng = Fetch(port, "/pet.png"); });
                };
                web.Start();
            }
            // --perf-test FILE [--perf-seconds N]: CPU per theme and frame rate (#8)
            int pt = Array.IndexOf(args, "--perf-test");
            if (pt >= 0 && pt + 1 < args.Length)
            {
                double secs;
                int ps = Array.IndexOf(args, "--perf-seconds");
                if (ps < 0 || ps + 1 >= args.Length || !double.TryParse(args[ps + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out secs)) secs = 5;
                double scale;
                int pz = Array.IndexOf(args, "--perf-scale");
                if (pz >= 0 && pz + 1 < args.Length && double.TryParse(args[pz + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale))
                {
                    Settings.Scale = scale;
                    window.ApplySettings();
                }
                int pth = Array.IndexOf(args, "--perf-themes");
                new PerfTest(this, window, args[pt + 1], secs, pth >= 0 && pth + 1 < args.Length ? args[pth + 1] : null).Start();
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
            var json = smokeJson != null && smokeJson.StartsWith("200 ") ? Json.Obj(Json.TryParse(smokeJson.Substring(smokeJson.IndexOf('{') >= 0 ? smokeJson.IndexOf('{') : 0))) : null;
            check("OBS page: usage.json", json != null && Json.Num(Json.Get(json, "version")) == UsageExport.Version && Json.Arr(Json.Get(json, "providers")) != null,
                  smokeJson == null ? "no answer" : System.Text.RegularExpressions.Regex.Replace(smokeJson.Length > 120 ? smokeJson.Substring(0, 120) + "…" : smokeJson, @"\s+", " "));
            check("OBS page: picture of the widget", smokePng != null && smokePng.StartsWith("200 image/png "), smokePng ?? "no answer");
            check("no errors logged", Log.Errors == 0, Log.Errors + " error(s), see " + System.IO.Path.Combine(AppPaths.LogDir, "app.log"));
            lines.Insert(0, AppInfo.Name + " " + AppInfo.Version + " smoke test on " + Os.Name + ": " + views.Count + " AI(s), language " + L.Current);
            lines.Add(failed == 0 ? "ALL PASS" : failed + " FAILED");
            try { System.IO.File.WriteAllLines(file, lines); } catch (Exception ex) { Log.Error("smoke report", ex); }
            Quit(failed);
        }

        string smokeJson, smokePng;

        /// <summary>--smoke-test: "status content-type length" (and the body of a JSON answer) of a GET on the local web page.</summary>
        static string Fetch(int port, string path)
        {
            try
            {
                using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                {
                    var r = http.GetAsync("http://127.0.0.1:" + port + path).Result;
                    var body = r.Content.ReadAsByteArrayAsync().Result;
                    return (int)r.StatusCode + " " + r.Content.Headers.ContentType.MediaType + " " + body.Length +
                           (path.EndsWith(".json") ? " " + System.Text.Encoding.UTF8.GetString(body) : "");
                }
            }
            catch (Exception ex) { return "error " + (ex.InnerException ?? ex).Message; }
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
                if (!IsQuiet) window.Say(null, L.F("新的一天，今天換成「{0}」！", pick.Name));
                if (settingsWindow != null) settingsWindow.OnThemeChanged();
            }
        }

        string numbersSig;

        public void RefreshViews()
        {
            views = Service.BuildViews(Settings, false);
            var now = DateTime.UtcNow;
            if (History != null)
            {
                try
                {
                    foreach (var r in History.Observe(views, Settings, now))
                    {
                        Log.Info("window ended: " + r.Key + " used " + r.Used + "%" + (r.SeenToEnd ? "" : " (last seen earlier)"));
                        pendingSummaries.Add(r);
                        if (progress != null) Grow(progress.Window(r, History.Results));
                    }
                    History.Annotate(views, now);
                }
                catch (Exception ex) { Log.Error("usage history", ex); }
            }
            if (window.CurrentTheme != null) window.CurrentTheme.Update(views);
            // new numbers: the themes animate to them (rings fill up, needles swing), so animate smoothly for a moment
            string sig = string.Join("|", views.Select(v => v.Id + ":" + string.Join(",", v.Meters.Select(m => Math.Round(m.Remaining, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)))));
            if (sig != numbersSig)
            {
                numbersSig = sig;
                window.Lively(2.5);
            }
            UpdateTray();
            Export(now);
            alerts.Check(Settings, views, Alert, CelebrateReset);
            if (!greeted && views.Count > 0 && Service.Providers.All(p => Service.SnapshotFor(p.Id) != null || !IsInstalled(p.Id)))
            {
                greeted = true;
                greetedAt = DateTime.UtcNow;
                Greet();
            }
            CheckAgentEvents();
            if (progress != null && views.Any(v => v.HasData)) Grow(progress.Day(DateTime.Now.Date));
            if (greeted && (DateTime.UtcNow - greetedAt).TotalSeconds > 8)
            {
                CheckUseIt();
                CheckRunsOut(now);
                SayWindowSummary();
                SayGrowth();
            }
        }

        /// <summary>Claude Code / Codex finished a longer task or waits for you (#14): its pet hops and says so.</summary>
        void CheckAgentEvents()
        {
            var events = claudeEvents.ReadNew();
            events.AddRange(codexEvents.ReadNew());
            if (!Settings.AgentHooks) return;
            foreach (var e in events)
            {
                if (e.Kind == AgentEvent.Done && e.Seconds >= 0 && e.Seconds < AgentHooks.LongTurnSeconds) continue;   // a quick reply
                if ((DateTime.UtcNow - e.At).TotalMinutes > 5) continue;   // written while the widget was busy or stopped
                Log.Info("agent " + e.Source + " " + e.Kind + (e.Seconds >= 0 ? " after " + Math.Round(e.Seconds) + " s" : ""));
                if (IsQuiet) continue;
                var v = views.FirstOrDefault(x => x.Id == e.Source);
                string name = v != null ? v.Name : (e.Source == "codex" ? "Codex" : "Claude");
                string text = Lines.Agent(e, name);
                if (v != null && window.CurrentTheme != null) window.CurrentTheme.Poke(v.Id);   // a little hop
                window.Say(v != null ? v.Id : null, text);
                if (Settings.AgentHookNotify && Settings.Notifications) Integration.Notify(AppInfo.Name, text);
            }
        }

        /// <summary>At the recent pace a quota runs out within 45 minutes, before its reset: say so once (#15).</summary>
        void CheckRunsOut(DateTime now)
        {
            if (IsQuiet) return;
            foreach (var v in views)
                foreach (var m in v.Meters)
                {
                    if (!m.RunsOutAt.HasValue || (m.RunsOutAt.Value - now).TotalMinutes > 45 || m.Remaining < 5) continue;
                    string key = v.Id + "|" + m.Key + "|" + (m.ResetsAt.HasValue ? m.ResetsAt.Value.ToString("yyyyMMddHH") : "");
                    if (!runsOutAnnounced.Add(key)) continue;
                    string text = Lines.RunsOut(v, m);
                    Log.Info("runs out: " + key + " at " + m.RunsOutAt.Value.ToString("o"));
                    window.Say(v.Id, text);
                    if (Settings.Notifications) Integration.Notify(AppInfo.Name, text);
                    return;   // one at a time
                }
        }

        /// <summary>A weekly/monthly window ended: how much of it was used (#16), once quiet time is over.</summary>
        void SayWindowSummary()
        {
            if (pendingSummaries.Count == 0 || IsQuiet || !window.IsVisible || menuOpen) return;
            var r = pendingSummaries[0];
            pendingSummaries.RemoveAt(0);
            string text = Lines.WindowSummary(r);
            window.Say(views.Any(v => v.Id == r.Provider) ? r.Provider : null, text);
            if (Settings.Notifications && Settings.WeeklyReport) Integration.Notify(AppInfo.Name + " · " + L.T("額度利用率"), text);
        }

        /// <summary>The pets' level on the widget and in the hover card (growing pets, #23), 0 when turned off.</summary>
        public int GrowthLevel { get { return Settings != null && Settings.Growth && progress != null ? progress.Level : 0; } }

        public Progress Progress { get { return progress; } }

        /// <summary>Shows the level (or hides it when growing pets are turned off).</summary>
        public void ApplyGrowth()
        {
            DetailCardView.GrowthLevel = GrowthLevel;
            if (window != null && window.CurrentTheme != null) window.CurrentTheme.SetGrowth(GrowthLevel);
            if (settingsWindow != null) settingsWindow.OnGrowthChanged();
        }

        /// <summary>Experience was earned: save, and queue what is worth saying (a level, an achievement).</summary>
        void Grow(List<ProgressEvent> events)
        {
            if (progress.Dirty) progress.Save();
            if (events.Count == 0) return;
            foreach (var e in events)
            {
                if (e.Achievement != null)
                {
                    Log.Info("achievement " + e.Achievement.Id);
                    pendingGrowth.Add(L.F("解鎖成就「{0}」（+{1} XP）：{2}", L.T(e.Achievement.Name), e.Achievement.Xp, L.T(e.Achievement.Description)));
                }
                else
                {
                    Log.Info("level " + e.Level);
                    string acc;
                    pendingGrowth.Add(Progress.Accessories.TryGetValue(e.Level, out acc)
                        ? L.F("升級了！現在是 Lv {0}，果凍桌寵解鎖了「{1}」", e.Level, L.T(acc))
                        : L.F("升級了！現在是 Lv {0}", e.Level));
                }
            }
            ApplyGrowth();
        }

        /// <summary>One piece of growing-pets news at a time, after the window summaries, a few seconds apart.</summary>
        void SayGrowth()
        {
            if (!Settings.Growth) { pendingGrowth.Clear(); return; }
            if (pendingGrowth.Count == 0 || pendingSummaries.Count > 0 || DateTime.UtcNow < nextGrowthNews) return;
            if (IsQuiet || !window.IsVisible || menuOpen) return;
            string text = pendingGrowth[0];
            pendingGrowth.RemoveAt(0);
            nextGrowthNews = DateTime.UtcNow.AddSeconds(9);
            if (window.CurrentTheme != null) foreach (var v in views) window.CurrentTheme.Celebrate(v.Id);
            window.Say(null, text);
            if (Settings.Notifications) Integration.Notify(AppInfo.Name + " · " + L.T("成長與成就"), text);
        }

        bool IsInstalled(string id)
        {
            var d = Service.DetectionFor(id);
            return d != null && d.Installed && Settings.IsEnabled(id);
        }

        void Greet()
        {
            if (IsQuiet) return;   // (the first-run greeting waits for the next start)
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
            if (IsQuiet) { Log.Info("quiet: " + text); return; }
            window.Say(v.Id, text);
            if (Settings.Notifications) Integration.Notify(AppInfo.Name, text);
        }

        void CelebrateReset(ProviderView v, Meter m, double previousUsed)
        {
            string text = Lines.Reset(v, m);
            if (window.CurrentTheme != null) window.CurrentTheme.Celebrate(v.Id);
            if (IsQuiet) return;
            if (m.WindowMinutes >= UsageHistory.ReportWindowMinutes) return;   // the window's summary says it (#16)
            window.Say(v.Id, text);
            if (Settings.Notifications && previousUsed >= 50) Integration.Notify(AppInfo.Name, text);
        }

        void CheckUseIt()
        {
            if (IsQuiet) return;   // new levels are announced when the quiet time is over
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
            bool number = Settings.TrayNumber || Settings.TrayOnly;   // (#21)
            string sig = (int)Math.Round(min) + (active ? "a" : "") + (number ? "n" : "");
            if (sig != traySig)
            {
                traySig = sig;
                try { tray.Icon = new WindowIcon(number ? TrayArt.DrawNumber(min, active, 64) : TrayArt.Draw(min, active, 64)); }
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
            var menu = new ContextMenu { ItemsSource = BuildMenuItems() };
            menu.Opened += (s, e) => menuOpen = true;
            menu.Closed += (s, e) => menuOpen = false;
            menu.Open(target);
        }

        /// <summary>A controller for the off-screen snapshots and the self-test (no window, tray or service).</summary>
        internal static DesktopController ForSnapshot(AppSettings settings, List<ProviderView> sample)
        {
            var c = new DesktopController(null, null);
            c.Settings = settings;
            c.views = sample;
            c.History = MockData.History();
            return c;
        }

        /// <summary>A theme in the menu: its name, and its mood in grey on the right.</summary>
        static Control ThemeHeader(ThemeInfo t)
        {
            var g = new Grid { MinWidth = 190 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = t.Name });
            var mood = new TextBlock { Text = t.Mood, Opacity = 0.55, Margin = new Thickness(24, 0, 0, 0) };
            Grid.SetColumn(mood, 1);
            g.Children.Add(mood);
            return g;
        }

        /// <summary>The widget's right-click menu.</summary>
        internal List<object> BuildMenuItems()
        {
            var items = new List<object>();
            // what each AI has left, one line each (one long line got cut off)
            var summary = new StackPanel();
            if (views.Count == 0) summary.Children.Add(new TextBlock { Text = L.T("正在偵測 AI…") });
            foreach (var v in views) summary.Children.Add(new TextBlock { Text = v.Summary });
            items.Add(new MenuItem { Header = summary, IsEnabled = false });
            items.Add(new Separator());
            // one menu for the looks: each theme with its mood (the WPF version had a second, "mood" menu with the same themes)
            var themes = new MenuItem { Header = L.T("換造型") };
            var themeItems = new List<object>();
            foreach (var t in ThemeCatalog.All)
            {
                var info = t;
                var mi = Toggle(info.Name, Settings.Theme == t.Id, () =>
                {
                    ChangeTheme(info.Id);
                    window.Say(null, L.F("收到！今天是「{0}」模式 ✦", info.Mood));
                });
                mi.Header = ThemeHeader(info);
                mi.ToggleType = MenuItemToggleType.Radio;
                themeItems.Add(mi);
            }
            themeItems.Add(new Separator());
            themeItems.Add(Item(L.T("交給命運吧（隨機）"), () =>
            {
                var choices = ThemeCatalog.All.Where(x => x.Id != Settings.Theme).ToList();
                var pick = choices[rng.Next(choices.Count)];
                ChangeTheme(pick.Id);
                window.Say(null, L.F("命運選擇了「{0}」！", pick.Name));
            }));
            themeItems.Add(Toggle(L.T("每天隨機換一個"), Settings.DailyRandomTheme, () =>
            {
                Settings.DailyRandomTheme = !Settings.DailyRandomTheme;
                Settings.RandomThemeDate = null;
                Settings.Save();
                if (Settings.DailyRandomTheme) PickDailyTheme(true);
            }));
            themes.ItemsSource = themeItems;
            items.Add(themes);
            items.Add(ReportMenu());
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

            var screens = window != null ? window.Screens.All.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToList() : new List<Screen>();
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
            // (the switches that are rarely changed — on top, click-through, talking, notifications, "use it",
            // autostart — are on the settings page only; the tray menu keeps click-through, the way back)
            var now = DateTime.UtcNow;
            if (Quiet.Paused(Settings, now))
                items.Add(Item(L.F("恢復提醒（暫停到 {0}）", Settings.PausedUntil.Value.ToLocalTime().ToString("HH:mm")), () =>
                {
                    Settings.PausedUntil = null;
                    Settings.Save();
                    window.Say(null, L.T("提醒恢復了！"));
                }));
            else
            {
                if (Quiet.InHours(Settings, now.ToLocalTime()))
                {
                    var end = Quiet.EndsAt(Settings, now);
                    items.Add(new MenuItem { Header = L.F("勿擾時段中（到 {0}）", end.HasValue ? end.Value.ToString("HH:mm") : "—"), IsEnabled = false });
                }
                else items.Add(Item(L.T("暫停提醒 1 小時"), () =>
                {
                    Settings.PausedUntil = DateTime.UtcNow + Quiet.PauseLength;
                    Settings.Save();
                    window.Say(null, L.T("好，接下來 1 小時我先安靜"));
                }));
            }
            items.Add(new Separator());
            items.Add(Item(L.T("設定…"), OpenSettings));
            items.Add(Item(L.T("先藏起來（點系統匣叫回）"), ToggleWidget));
            items.Add(Item(L.T("結束"), Quit));
            return items;
        }

        /// <summary>
        /// The weekly/monthly quotas at a glance (#16): for each, the bars of its last 8 windows and the one in progress,
        /// with the numbers — right in the menu; "Full report…" opens the settings page there.
        /// </summary>
        MenuItem ReportMenu()
        {
            var menu = new MenuItem { Header = L.T("額度利用率（週報／月報）") };
            var list = new List<object>();
            var results = History != null ? History.Results : new List<WindowResult>();
            foreach (var v in views)
                foreach (var m in v.Meters.Where(x => !x.Unlimited && x.WindowMinutes >= UsageHistory.ReportWindowMinutes))
                {
                    var past = results.Where(r => r.Provider == v.Id && r.Meter == m.Key).ToList();
                    if (past.Count > 8) past = past.GetRange(past.Count - 8, 8);
                    var entry = Item(v.Name + " · " + m.Label, OpenReport);
                    entry.Header = ReportChart.MenuEntry(v.Name, m, past);
                    list.Add(entry);
                }
            if (list.Count == 0) list.Add(new MenuItem { Header = L.T("目前沒有每週或每月的額度"), IsEnabled = false });
            list.Add(new Separator());
            list.Add(Item(L.T("產生這週的週報圖"), MakeShareCard));
            list.Add(Item(L.T("查看完整報告…"), OpenReport));
            menu.ItemsSource = list;
            return menu;
        }

        /// <summary>The settings page, scrolled to the quota-use report.</summary>
        public void OpenReport()
        {
            OpenReportWindow();
        }

        /// <summary>
        /// The shareable weekly summary (#20): reads the last days' tokens (in the background), draws the picture into
        /// Pictures/SentriPet and opens it.
        /// </summary>
        public void MakeShareCard()
        {
            var ledger = Ledger;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { ledger.Update(DateTime.UtcNow); }
                catch (Exception ex) { Log.Error("token ledger", ex); }
            }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    string file = System.IO.Path.Combine(ShareCard.Folder, "weekly-" + DateTime.Now.ToString("yyyy-MM-dd") + ".png");
                    ShareCard.Save(this, file, DateTime.Now);
                    Log.Info("share card: " + file);
                    Integration.OpenPath(file);
                    window.Say(null, L.T("這週的週報圖做好了，可以直接分享！"));
                }
                catch (Exception ex)
                {
                    Log.Error("share card", ex);
                    window.Say(null, L.F("週報圖做不出來：{0}", ex.Message));
                }
            }));
        }

        /// <summary>The usage report window (#18).</summary>
        public void OpenReportWindow()
        {
            if (reportWindow == null)
            {
                reportWindow = new ReportWindow(this, true);
                reportWindow.Closed += (s, e) => reportWindow = null;
                reportWindow.Show();
            }
            if (reportWindow.WindowState == WindowState.Minimized) reportWindow.WindowState = WindowState.Normal;
            reportWindow.Activate();
        }

        static MenuItem Item(string header, Action click)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (s, e) =>
            {
                try { click(); }
                catch (Exception ex) { Log.Error("menu " + header, ex); }   // (the text given here, also when the header is replaced by a control)
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
            ApplyTrayMode();
            Settings.Save();
        }

        bool? trayOnlyApplied;

        /// <summary>
        /// The tray icon as a number, and "only in the tray" (#21): the pet stays hidden until the tray icon is clicked.
        /// Only when there is a tray icon — without one (some Linux desktops) the pet is the only way in.
        /// </summary>
        void ApplyTrayMode()
        {
            traySig = null;
            UpdateTray();
            bool only = Settings.TrayOnly && tray != null;
            if (trayOnlyApplied == only) return;
            trayOnlyApplied = only;
            window.UserHidden = only;
            window.UpdateVisibility();
        }

        /// <summary>The local web page's address while it runs (#22), null otherwise.</summary>
        public string UsageServerUrl { get { return server != null && server.Running ? server.Url : null; } }

        /// <summary>
        /// usage.json and the local web page for OBS and scripts (#22), after start-up or a change on the settings page.
        /// Turning usage.json off removes the file, so no script keeps reading old numbers.
        /// </summary>
        public void ApplyExportSettings()
        {
            if (!Settings.ExportUsage) exporter.Remove();
            if (Settings.UsageServer)
            {
                if (server == null) server = new UsageServer(() => exporter.Latest, PetPng);
                if (!server.Running || server.Port != Settings.UsagePort) server.Start(Settings.UsagePort);
            }
            else if (server != null) server.Stop();
            Export(DateTime.UtcNow);
        }

        void Export(DateTime now)
        {
            bool serving = server != null && server.Running;
            if (!Settings.ExportUsage && !serving) return;
            try { exporter.Update(views, now, Settings.ExportUsage); }
            catch (Exception ex) { Log.Error("usage export", ex); }
        }

        /// <summary>The widget as a PNG for the OBS page (asked from the server's threads; shared for 90 ms between viewers).</summary>
        byte[] PetPng()
        {
            lock (petGate)
                if (petPicture != null && (DateTime.UtcNow - petPictureAt).TotalMilliseconds < 90) return petPicture;
            var done = new System.Threading.Tasks.TaskCompletionSource<byte[]>();
            Dispatcher.UIThread.Post(() =>
            {
                try { done.SetResult(window.Picture()); }
                catch (Exception ex) { done.SetException(ex); }
            });
            if (!done.Task.Wait(3000)) return null;
            lock (petGate)
            {
                petPicture = done.Task.Result;
                petPictureAt = DateTime.UtcNow;
                return petPicture;
            }
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
            CustomLines.Reload();       // its messages are written in the language
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
            try { if (server != null) server.Stop(); } catch { }
            try { if (tray != null) tray.Dispose(); } catch { }
            try { if (settingsWindow != null) settingsWindow.Close(); } catch { }
            try { Service.Dispose(); } catch { }
            desktop.Shutdown(exitCode);
        }
    }
}
