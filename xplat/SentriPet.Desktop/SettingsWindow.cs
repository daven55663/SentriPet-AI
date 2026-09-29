using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;

using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SentriPet
{
    /// <summary>What the settings page needs from the app (the controller, or a stand-in for --snapshot).</summary>
    interface ISettingsHost
    {
        AppSettings Settings { get; }
        UsageService Service { get; }
        List<ProviderView> Views { get; }
        /// <summary>The pace and the report of past weekly/monthly windows (#15, #16).</summary>
        UsageHistory History { get; }
        void ApplyWidgetSettings();
        void ChangeTheme(string id);
        void ChangeLanguage(string code);
        void RefreshViews();
        /// <summary>The usage report window (#18).</summary>
        void OpenReportWindow();
    }

    /// <summary>
    /// The settings page (Avalonia port of the WPF SettingsWindow): themes with live previews, look, AI services,
    /// reminders, general. Changes apply at once and are saved shortly after.
    /// </summary>
    class SettingsWindow : Window
    {
        static readonly Color Bg = Palette.Hex("#14161C");
        static readonly Color PanelBg = Palette.Hex("#1B1E26");
        static readonly Color LineC = Palette.Hex("#2A2E39");
        static readonly Color TextC = Palette.Hex("#E8EAF0");
        static readonly Color SubC = Palette.Hex("#8F98A8");
        static readonly Color Accent = Palette.Hex("#7C9CFF");

        readonly ISettingsHost ctl;
        readonly WrapPanel gallery = new WrapPanel();
        readonly Dictionary<string, Button> cards = new Dictionary<string, Button>();
        readonly Dictionary<string, Border> previews = new Dictionary<string, Border>();
        readonly StackPanel providerList = new StackPanel();
        readonly StackPanel otherList = new StackPanel();
        readonly DispatcherTimer refresh, saveTimer;
        ToggleSwitch randomToggle;
        string providerSig;
        ScrollViewer scroller;
        Control reportAnchor;

        AppSettings S { get { return ctl.Settings; } }

        /// <summary>The page content (the snapshot renders it without a window).</summary>
        public Control Page { get; private set; }

        public SettingsWindow(ISettingsHost ctl)
        {
            this.ctl = ctl;
            Title = AppInfo.Name + " · " + L.T("設定");
            Width = 700;
            Height = 780;
            MinWidth = 560;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = G.B(Bg);
            Foreground = G.B(TextC);
            FontFamily = G.Ui;
            FontSize = 13;
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://SentriPet/Assets/app.ico"))); } catch { }

            var root = new StackPanel { Margin = new Thickness(28, 20, 28, 30), Background = G.B(Bg) };
            BuildHeader(root);
            BuildThemes(root);
            BuildLook(root);
            BuildProviders(root);
            BuildAlerts(root);
            BuildReport(root);
            BuildGeneral(root);
            Page = root;
            Content = scroller = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); S.Save(); };
            refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            refresh.Tick += (s, e) => RefreshProviders();
            refresh.Start();
            Closed += (s, e) => { refresh.Stop(); if (saveTimer.IsEnabled) { saveTimer.Stop(); S.Save(); } };
            Opened += (s, e) => Dispatcher.UIThread.Post(RenderPreviews, DispatcherPriority.Background);
        }

        /// <summary>Scrolls to the quota-use report (the widget's menu opens it there).</summary>
        public void ShowReport()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var p = reportAnchor != null && Page != null ? reportAnchor.TranslatePoint(new Point(0, 0), Page) : null;
                if (p.HasValue) scroller.Offset = new Vector(0, Math.Max(0, p.Value.Y - 12));
            }, DispatcherPriority.Background);
        }

        void SaveSoon()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        // ------------------------------------------------------------------ layout helpers

        static TextBlock Txt(string s, double size, Color c, FontWeight w)
        {
            return new TextBlock { Text = s, FontSize = size, Foreground = G.B(c), FontWeight = w, TextWrapping = TextWrapping.Wrap };
        }

        StackPanel Section(StackPanel root, string title, string subtitle)
        {
            var head = Txt(title, 17, TextC, FontWeight.Bold);
            head.Margin = new Thickness(2, 26, 0, 4);
            root.Children.Add(head);
            if (subtitle != null)
            {
                var st = Txt(subtitle, 12, SubC, FontWeight.Normal);
                st.Margin = new Thickness(2, 0, 0, 10);
                root.Children.Add(st);
            }
            var body = new StackPanel();
            root.Children.Add(new Border
            {
                Background = G.B(PanelBg),
                BorderBrush = G.B(LineC),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18, 6, 18, 6),
                Child = body,
            });
            return body;
        }

        void Row(StackPanel body, string label, string hint, Control control)
        {
            if (body.Children.Count > 0) body.Children.Add(new Border { Height = 1, Background = G.B(LineC) });
            var g = new Grid { Margin = new Thickness(0, 11, 0, 11) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(Txt(label, 13.5, TextC, FontWeight.SemiBold));
            if (hint != null)
            {
                var h = Txt(hint, 11.5, SubC, FontWeight.Normal);
                h.Margin = new Thickness(0, 2, 0, 0);
                left.Children.Add(h);
            }
            g.Children.Add(left);
            if (control != null)
            {
                control.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 1);
                g.Children.Add(control);
            }
            body.Children.Add(g);
        }

        static ToggleSwitch Toggle(bool value, Action<bool> set)
        {
            var t = new ToggleSwitch { IsChecked = value, OnContent = "", OffContent = "", MinWidth = 0 };
            t.IsCheckedChanged += (s, e) => set(t.IsChecked == true);
            return t;
        }

        static Control SliderBox(double min, double max, double value, double step, Func<double, string> fmt, Action<double> set)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var sl = new Slider { Minimum = min, Maximum = max, Value = value, Width = 190, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            var lbl = new TextBlock { Text = fmt(value), Width = 92, TextAlignment = TextAlignment.Right, Foreground = G.B(SubC), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            sl.ValueChanged += (s, e) => { lbl.Text = fmt(e.NewValue); set(e.NewValue); };
            sp.Children.Add(sl);
            sp.Children.Add(lbl);
            return sp;
        }

        static Button Btn(string text, Action click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 7) };
            b.Click += (s, e) =>
            {
                try { click(); }
                catch (Exception ex) { Log.Error("button " + text, ex); }
            };
            return b;
        }

        // ------------------------------------------------------------------ sections

        void BuildHeader(StackPanel root)
        {
            var t = new StackPanel();
            t.Children.Add(Txt(AppInfo.Name, 22, TextC, FontWeight.Bold));
            t.Children.Add(Txt("v" + AppInfo.Version + " · " + L.F("目前顯示 {0} 個 AI · 拖曳桌寵可移到任何一個螢幕", ctl.Views.Count), 12, SubC, FontWeight.Normal));
            root.Children.Add(t);
        }

        void BuildThemes(StackPanel root)
        {
            var body = Section(root, L.T("造型"), L.T("依照今天的心情挑一個吧，在桌寵上按右鍵也能隨時換"));
            gallery.Margin = new Thickness(-6, 10, -6, 8);
            foreach (var t in ThemeCatalog.All)
            {
                var info = t;
                var preview = new Border
                {
                    Height = 118,
                    CornerRadius = new CornerRadius(10),
                    Background = G.Lg(Palette.Hex("#3A6073"), Palette.Hex("#6D4C7D"), 35),
                    Padding = new Thickness(6),
                    ClipToBounds = true,
                    Child = new TextBlock { Text = L.T("繪製預覽中…"), Foreground = G.B(Colors.White, 0.6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 },
                };
                previews[info.Id] = preview;
                var sp = new StackPanel();
                sp.Children.Add(preview);
                var name = Txt(info.Name, 13.5, TextC, FontWeight.Bold);
                name.Margin = new Thickness(2, 8, 0, 0);
                sp.Children.Add(name);
                var mood = Txt(info.Mood + " · " + info.Blurb, 11, SubC, FontWeight.Normal);
                mood.Margin = new Thickness(2, 2, 0, 2);
                sp.Children.Add(mood);
                var btn = new Button
                {
                    Content = sp,
                    Width = 190,
                    Margin = new Thickness(6),
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(12),
                    Background = G.B(Palette.Hex("#20242E")),
                    BorderThickness = new Thickness(2),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                btn.Click += (s, e) => { ctl.ChangeTheme(info.Id); OnThemeChanged(); };
                cards[info.Id] = btn;
                gallery.Children.Add(btn);
            }
            body.Children.Add(gallery);
            randomToggle = Toggle(S.DailyRandomTheme, v =>
            {
                S.DailyRandomTheme = v;
                S.RandomThemeDate = null;
                SaveSoon();
            });
            Row(body, L.T("每天隨機換一個造型"), L.T("每天第一次見面時自動換成別的造型，給自己一點驚喜"), randomToggle);
            OnThemeChanged();
        }

        public void OnThemeChanged()
        {
            foreach (var kv in cards) kv.Value.BorderBrush = G.B(kv.Key == S.Theme ? Accent : Colors.Transparent);
            if (randomToggle != null && randomToggle.IsChecked != S.DailyRandomTheme) randomToggle.IsChecked = S.DailyRandomTheme;
        }

        /// <summary>Draws each theme with the current (or sample) data into its card.</summary>
        public void RenderPreviews()
        {
            var data = ctl.Views.Count > 0 ? ctl.Views : MockData.A();
            foreach (var info in ThemeCatalog.All)
            {
                try
                {
                    var t = info.Create();
                    t.Attach(new Snapshots.PreviewHost());
                    t.Update(data);
                    for (int i = 0; i < 40; i++) t.Tick(1 / 30.0);
                    var bmp = Snapshots.RenderToBitmap(t.Root, 1.0);
                    previews[info.Id].Child = new Image { Source = bmp, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
                catch (Exception ex) { Log.Error("preview " + info.Id, ex); }
            }
        }

        void BuildLook(StackPanel root)
        {
            var body = Section(root, L.T("外觀"), null);
            Row(body, L.T("大小"), null, SliderBox(50, 200, Math.Round(S.Scale * 100), 5, v => Math.Round(v) + "%", v => { S.Scale = v / 100; ctl.ApplyWidgetSettings(); SaveSoon(); }));
            Row(body, L.T("不透明度"), L.T("調低一點可以若隱若現"), SliderBox(30, 100, Math.Round(S.Opacity * 100), 5, v => Math.Round(v) + "%", v => { S.Opacity = v / 100; ctl.ApplyWidgetSettings(); SaveSoon(); }));
            Row(body, L.T("永遠在最上層"), L.T("不會被其他視窗蓋住"), Toggle(S.AlwaysOnTop, v => { S.AlwaysOnTop = v; ctl.ApplyWidgetSettings(); SaveSoon(); }));
            if (Integration.CanDetectFullscreen)
                Row(body, L.T("全螢幕時自動躲起來"), L.T("看影片、玩遊戲、簡報時不會擋畫面"), Toggle(S.HideOnFullscreen, v => { S.HideOnFullscreen = v; SaveSoon(); }));
            if (Integration.CanClickThrough)
                Row(body, L.T("滑鼠穿透"), L.T("桌寵不會擋住點擊（要關閉請從系統匣圖示按右鍵）"), Toggle(S.ClickThrough, v => { S.ClickThrough = v; ctl.ApplyWidgetSettings(); SaveSoon(); }));
            Row(body, L.T("會說話"), L.T("偶爾冒出一句話、點牠會回應"), Toggle(S.Chatty, v => { S.Chatty = v; SaveSoon(); }));
            Row(body, L.T("省電模式"), L.T("降低動畫幀率，筆電用電池時可以開"), Toggle(S.LowPower, v => { S.LowPower = v; ctl.ApplyWidgetSettings(); SaveSoon(); }));
            // (#21)
            Row(body, L.T("系統匣／選單列圖示顯示剩餘 %"), L.T("圖示直接畫出最低的剩餘額度，顏色跟著變"),
                Toggle(S.TrayNumber, v => { S.TrayNumber = v; ctl.ApplyWidgetSettings(); SaveSoon(); }));
            Row(body, L.T("只顯示在系統匣／選單列"), L.T("桌面上不放桌寵，只留顯示剩餘 % 的圖示；點圖示可以叫出桌寵"),
                Toggle(S.TrayOnly, v => { S.TrayOnly = v; ctl.ApplyWidgetSettings(); SaveSoon(); }));
        }

        void BuildProviders(StackPanel root)
        {
            var body = Section(root, L.T("AI 服務"), L.T("自動偵測電腦上的 AI 工具。資料只在本機讀取，不會上傳到任何地方。"));
            body.Children.Add(providerList);
            body.Children.Add(otherList);
            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 4) };
            buttons.Children.Add(Btn(L.T("重新偵測"), () => { ctl.Service.Redetect(); ctl.Service.RefreshNow(null); providerSig = null; }));
            buttons.Children.Add(Btn(L.T("重新載入外掛"), () => { ctl.Service.ReloadProviders(); providerSig = null; }));
            buttons.Children.Add(Btn(L.T("開啟外掛資料夾"), OpenPluginFolder));
            body.Children.Add(new Border { Height = 1, Background = G.B(LineC) });
            body.Children.Add(buttons);

            Row(body, L.T("Codex 即時查詢"), L.T("透過官方 codex app-server 讀取最新用量；設 0 只讀本機對話紀錄"),
                SliderBox(0, 30, S.CodexLiveMinutes, 1, v => v < 1 ? L.T("關閉") : L.F("每 {0} 分鐘", Math.Round(v)), v => { S.CodexLiveMinutes = (int)Math.Round(v); SaveSoon(); }));

            var box = new TextBox { Width = 150, Text = S.ClaudeWeeklyReset ?? "" };
            var hint = Txt("", 11, SubC, FontWeight.Normal);
            Action check = () =>
            {
                DateTime? next;
                if (string.IsNullOrWhiteSpace(box.Text)) hint.Text = L.T("留空 = 自動推算");
                else if (ClaudeProvider.TryParseWeekly(box.Text, out next)) hint.Text = L.F("下次重置：{0}（{1}後）", Fmt.When(next), Fmt.Countdown(next));
                else hint.Text = L.T("看不懂這個時間，例如：週四 23:00");
            };
            box.TextChanged += (s, e) =>
            {
                check();
                DateTime? next;
                if (string.IsNullOrWhiteSpace(box.Text) || ClaudeProvider.TryParseWeekly(box.Text, out next))
                {
                    S.ClaudeWeeklyReset = (box.Text ?? "").Trim();
                    SaveSoon();
                    ctl.Service.RefreshNow("claude");
                }
            };
            check();
            var right = new StackPanel();
            right.Children.Add(box);
            hint.Margin = new Thickness(0, 4, 0, 0);
            hint.MaxWidth = 180;
            right.Children.Add(hint);
            Row(body, L.T("Claude 每週重置時間（選填）"), L.T("Claude 的快取沒有重置時間，預設用歷史紀錄推算。想要精準，照 Claude 設定 → 用量 頁面上寫的時間填一次，例如「週四 23:00」"), right);
            var bridgeError = Txt("", 11, Palette.Hex("#FCA5A5"), FontWeight.Normal);
            bridgeError.IsVisible = false;
            ToggleSwitch bridge = null;
            bridge = Toggle(S.ClaudeStatusBridge && ClaudeStatusLine.IsConnected(), v =>
            {
                if (v == (S.ClaudeStatusBridge && ClaudeStatusLine.IsConnected())) return;
                try
                {
                    if (v) ClaudeStatusLine.Connect(S, Environment.ProcessPath);
                    else ClaudeStatusLine.Disconnect(S);
                    bridgeError.IsVisible = false;
                    SaveSoon();
                    ctl.Service.RefreshNow("claude");
                }
                catch (Exception ex)
                {
                    Log.Error("status line bridge", ex);
                    bridgeError.Text = L.F("沒辦法修改 Claude 的設定：{0}", ex.Message);
                    bridgeError.IsVisible = true;
                    bridge.IsChecked = !v;
                }
            });
            var bridgeBox = new StackPanel();
            bridgeBox.Children.Add(bridge);
            bridgeBox.Children.Add(bridgeError);
            bridgeError.MaxWidth = 200;
            Row(body, L.T("連接 Claude Code 狀態列（官方用量）"),
                L.T("在 ~/.claude/settings.json 加上狀態列設定：Claude Code 每次回覆都會把官方的用量與重置時間交給 SentriPet。原本的狀態列照常顯示，關掉時會還原。Linux 沒有 Claude 桌面版，要開這個才讀得到 Claude 用量"),
                bridgeBox);
            // "done / waiting for you" (#14)
            var hookError = Txt("", 11, Palette.Hex("#FCA5A5"), FontWeight.Normal);
            hookError.IsVisible = false;
            hookError.MaxWidth = 200;
            var hookNotify = Toggle(S.AgentHookNotify, v => { S.AgentHookNotify = v; SaveSoon(); });
            var hookNotifyRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), IsVisible = S.AgentHooks };
            hookNotifyRow.Children.Add(new TextBlock { Text = L.T("同時跳通知"), Foreground = G.B(SubC), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            hookNotifyRow.Children.Add(hookNotify);
            ToggleSwitch hooks = null;
            hooks = Toggle(S.AgentHooks && (AgentHooks.IsClaudeConnected() || AgentHooks.IsCodexConnected()), v =>
            {
                if (v == (S.AgentHooks && (AgentHooks.IsClaudeConnected() || AgentHooks.IsCodexConnected()))) return;   // (put back after an error)
                try
                {
                    if (v)
                    {
                        if (!AgentHooks.HasClaude && !AgentHooks.HasCodex) throw new InvalidOperationException(L.T("這台電腦上沒有找到 Claude Code 或 Codex"));
                        AgentHooks.Connect(S, Environment.ProcessPath, AgentHooks.HasClaude, AgentHooks.HasCodex);
                    }
                    else AgentHooks.Disconnect(S);
                    hookError.IsVisible = false;
                    hookNotifyRow.IsVisible = v;
                    SaveSoon();
                }
                catch (Exception ex)
                {
                    Log.Error("agent hooks", ex);
                    hookError.Text = L.F("沒辦法修改設定：{0}", ex.Message);
                    hookError.IsVisible = true;
                    S.AgentHooks = !v;
                    hooks.IsChecked = !v;
                }
            });
            hooks.HorizontalAlignment = HorizontalAlignment.Right;
            var hookBox = new StackPanel();
            hookBox.Children.Add(hooks);
            hookBox.Children.Add(hookNotifyRow);
            hookBox.Children.Add(hookError);
            Row(body, L.T("AI 做完或在等你時提醒"),
                L.T("Claude Code 做完較長的任務（超過 30 秒）、要你確認或在等你回覆時，那隻桌寵會跳起來告訴你；Codex 做完也會。會在 ~/.claude/settings.json 加上 hooks、在 ~/.codex/config.toml 設定 notify（修改前先備份，原本的設定都保留，關掉時還原）"),
                hookBox);
            Row(body, L.T("Claude 即時推算"), L.T("Claude 桌面版約每 15 分鐘才記錄一次用量。開啟後會讀 Claude Code 本機對話紀錄裡的 token 數（不讀內容），推算這段空檔的用量，數字前面會標「≈」"),
                Toggle(S.ClaudeEstimate, v => { S.ClaudeEstimate = v; SaveSoon(); ctl.Service.RefreshNow("claude"); }));
            RefreshProviders();
        }

        void OpenPluginFolder()
        {
            AppPaths.EnsureProvidersDir();
            string src = Path.Combine(AppPaths.ExeDir, "examples", "providers");
            try
            {
                if (Directory.Exists(src))
                    foreach (var f in Directory.GetFiles(src))
                    {
                        string dest = Path.Combine(AppPaths.ProvidersDir, Path.GetFileName(f));
                        if (!File.Exists(dest)) File.Copy(f, dest);
                    }
            }
            catch (Exception ex) { Log.Error("copy examples", ex); }
            Integration.OpenPath(AppPaths.ProvidersDir);
        }

        void RefreshProviders()
        {
            var svc = ctl.Service;
            if (svc == null) return;
            var rows = new List<Tuple<Provider, Detection, Snapshot>>();
            foreach (var p in svc.Providers) rows.Add(Tuple.Create(p, svc.DetectionFor(p.Id), svc.SnapshotFor(p.Id)));
            var hits = svc.CatalogHits;
            var sig = new StringBuilder();
            foreach (var r in rows) sig.Append(r.Item1.Id).Append(StatusText(r.Item2, r.Item3)).Append(S.IsEnabled(r.Item1.Id)).Append('|');
            foreach (var h in hits) sig.Append(h.Key.Id).Append(',');
            if (sig.ToString() == providerSig) return;
            providerSig = sig.ToString();

            providerList.Children.Clear();
            foreach (var r in rows.OrderBy(x => x.Item2 != null && x.Item2.Installed ? 0 : 1))
            {
                var p = r.Item1;
                var d = r.Item2;
                bool installed = d != null && d.Installed;
                if (providerList.Children.Count > 0) providerList.Children.Add(new Border { Height = 1, Background = G.B(LineC) });
                var g = new Grid { Margin = new Thickness(0, 11, 0, 11), Opacity = installed ? 1 : 0.55 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 12, Height = 12, Fill = G.B(G.Vivid(p.Color)), Margin = new Thickness(0, 4, 12, 0), VerticalAlignment = VerticalAlignment.Top });
                var info = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                Grid.SetColumn(info, 1);
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
                nameRow.Children.Add(Txt(p.Name, 14, TextC, FontWeight.Bold));
                if (!p.BuiltIn)
                {
                    var pill = G.Pill(Txt(L.T("外掛"), 10, Accent, FontWeight.SemiBold), G.B(Accent, 0.15), 6, new Thickness(6, 0, 6, 1));
                    pill.Margin = new Thickness(8, 2, 0, 0);
                    nameRow.Children.Add(pill);
                }
                info.Children.Add(nameRow);
                info.Children.Add(Txt(installed ? L.F("偵測到：{0}", d.EvidenceText) : (d != null && d.Hint != null ? d.Hint : L.T("這台電腦上沒有偵測到")), 11.5, SubC, FontWeight.Normal));
                if (installed)
                {
                    var st = Txt(StatusText(d, r.Item3), 11.5, r.Item3 != null && r.Item3.Error != null && !r.Item3.Offline ? Palette.Hex("#FCA5A5") : Palette.Hex("#86EFAC"), FontWeight.Normal);
                    st.Margin = new Thickness(0, 2, 0, 0);
                    info.Children.Add(st);
                }
                g.Children.Add(info);
                var id = p.Id;
                Control right;
                if (installed) right = Toggle(S.IsEnabled(id), v => { S.Enabled[id] = v; SaveSoon(); providerSig = null; ctl.RefreshViews(); });
                else right = Txt(L.T("未安裝"), 12, SubC, FontWeight.Normal);
                right.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(right, 2);
                g.Children.Add(right);
                providerList.Children.Add(g);
            }

            otherList.Children.Clear();
            if (hits.Count > 0)
            {
                otherList.Children.Add(new Border { Height = 1, Background = G.B(LineC) });
                var t = Txt(L.T("其他偵測到的 AI（還沒有用量來源，可以寫外掛接上）"), 12, SubC, FontWeight.SemiBold);
                t.Margin = new Thickness(0, 10, 0, 4);
                otherList.Children.Add(t);
                foreach (var h in hits)
                {
                    var line = Txt("•  " + h.Key.Name + " — " + h.Value.EvidenceText, 12, TextC, FontWeight.Normal);
                    line.Margin = new Thickness(6, 2, 0, 2);
                    otherList.Children.Add(line);
                }
                otherList.Children.Add(new Border { Height = 8 });
            }
        }

        static string StatusText(Detection d, Snapshot s)
        {
            if (d == null) return L.T("偵測中…");
            if (!d.Installed) return "";
            if (s == null) return L.T("讀取中…");
            if (s.Offline) return s.Error ?? L.T("離線");
            if (s.Error != null) return "✗ " + s.Error;
            var meters = string.Join(L.T("、"), s.Meters.Take(3).Select(m => L.F("{0} 剩 {1}", m.Label, m.Unlimited ? "∞" : Fmt.Pct(m.Remaining))));
            string when = s.ObservedAt.HasValue ? s.ObservedAt.Value.ToLocalTime().ToString("HH:mm") : "";
            return "✓ " + meters + " · " + (s.Source ?? "") + (when.Length > 0 ? " · " + when : "") + (s.Stale ? " · " + L.T("資料較舊") : "");
        }

        void BuildAlerts(StackPanel root)
        {
            var body = Section(root, L.T("提醒"), null);
            Row(body, L.T("額度提醒通知"), Integration.NotificationHint, Toggle(S.Notifications, v => { S.Notifications = v; SaveSoon(); }));
            Row(body, L.T("催我用完週額度"), L.T("每週／每月額度快重置、卻還剩不少時，桌寵會拿鬧鐘催你把它用掉：剩 2 天（還有 30% 以上）開始提醒，最後一天、最後 6 小時會越催越勤，並各跳一次通知"),
                Toggle(S.UseItReminder, v => { S.UseItReminder = v; SaveSoon(); ctl.RefreshViews(); }));
            Row(body, L.T("勿擾時段"), L.T("這段時間不跳通知、桌寵不主動說話（點牠還是會回應），數字照常顯示；右鍵選單也可以暫停提醒 1 小時"), QuietHoursBox());
            Row(body, L.T("提醒門檻"), L.T("用量超過這個比例時提醒一次"), SliderBox(50, 95, S.WarnAt, 5, v => L.F("用掉 {0}%", Math.Round(v)), v => { S.WarnAt = (int)Math.Round(v); SaveSoon(); }));
            Row(body, L.T("緊急門檻"), L.T("快用完時再提醒一次"), SliderBox(60, 100, S.CriticalAt, 1, v => L.F("用掉 {0}%", Math.Round(v)), v => { S.CriticalAt = (int)Math.Round(v); SaveSoon(); }));
        }

        /// <summary>How much of each weekly/monthly window was used (#16): the last 8 windows as bars, and the average.</summary>
        void BuildReport(StackPanel root)
        {
            int at = root.Children.Count;
            var body = Section(root, L.T("額度利用率"), L.T("每週／每月額度重置時，記下那一期用掉多少，看看有沒有浪費"));
            reportAnchor = root.Children[at];
            var results = ctl.History != null ? ctl.History.Results : new List<WindowResult>();
            if (results.Count == 0)
                Row(body, L.T("還沒有紀錄"), L.T("每週額度重置一次之後，這裡就會出現那一週用掉多少"), null);
            foreach (var g in results.GroupBy(r => r.Key))
            {
                var last = g.OrderBy(r => r.EndedAt).ToList();
                if (last.Count > 8) last = last.GetRange(last.Count - 8, 8);
                var newest = last[last.Count - 1];
                // (the window in progress, when this quota is shown right now)
                var view = ctl.Views.FirstOrDefault(v => v.Id == newest.Provider);
                var current = view != null ? view.Meters.FirstOrDefault(m => m.Key == newest.Meter && !m.Unlimited) : null;
                var bars = ReportChart.Bars(last, current, 40, 26, true);
                bars.HorizontalAlignment = HorizontalAlignment.Right;
                Row(body, newest.Name + " · " + newest.Label,
                    L.F("最近 {0} 期平均用掉 {1}，最近一期 {2}", last.Count, Fmt.Pct(last.Average(r => r.Used)), Fmt.Pct(newest.Used)),
                    bars);
            }
            Row(body, L.T("重置時跳通知總結"), L.T("週額度重置時桌寵會說那一期用掉多少；開啟後也會跳一則通知"),
                Toggle(S.WeeklyReport, v => { S.WeeklyReport = v; SaveSoon(); }));
            Row(body, L.T("用量報告"), L.T("每天用了多少 token、專案排行、模型，最近 7 或 30 天（右鍵選單 → 額度利用率 → 查看完整報告 也能打開）"),
                Btn(L.T("打開用量報告"), ctl.OpenReportWindow));
        }

        /// <summary>Quiet hours (#17): on/off, from–to and the weekdays they apply to.</summary>
        Control QuietHoursBox()
        {
            var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 250 };
            var details = new StackPanel { IsEnabled = S.QuietHours, Margin = new Thickness(0, 6, 0, 0) };
            var onOff = Toggle(S.QuietHours, v => { S.QuietHours = v; details.IsEnabled = v; SaveSoon(); });
            onOff.HorizontalAlignment = HorizontalAlignment.Right;
            box.Children.Add(onOff);

            var hint = Txt("", 11, Palette.Hex("#FCA5A5"), FontWeight.Normal);
            hint.IsVisible = false;
            var times = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Func<string, Action<string>, TextBox> timeBox = (value, set) =>
            {
                var tb = new TextBox { Text = value, Width = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
                tb.TextChanged += (s, e) =>
                {
                    TimeSpan t;
                    bool ok = Quiet.TryParseTime(tb.Text, out t);
                    if (ok)
                    {
                        set(t.Hours.ToString("00") + ":" + t.Minutes.ToString("00"));
                        SaveSoon();
                    }
                    hint.Text = L.T("時間看不懂，例如 22:00");
                    hint.IsVisible = !ok;
                };
                return tb;
            };
            times.Children.Add(timeBox(S.QuietFrom, v => S.QuietFrom = v));
            times.Children.Add(new TextBlock { Text = "–", Foreground = G.B(SubC), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
            times.Children.Add(timeBox(S.QuietTo, v => S.QuietTo = v));
            details.Children.Add(times);

            // Monday first; each day on or off
            var days = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, -4, 0) };
            foreach (var d in new[] { 1, 2, 3, 4, 5, 6, 0 })
            {
                var day = (DayOfWeek)d;
                var chip = new Avalonia.Controls.Primitives.ToggleButton
                {
                    Content = Fmt.WeekDay(day), IsChecked = Quiet.DayOn(S, day), FontSize = 11.5,
                    Padding = new Thickness(7, 3), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(7),
                };
                chip.IsCheckedChanged += (s, e) =>
                {
                    string all = S.QuietDays ?? "0123456";
                    char c = (char)('0' + d);
                    all = chip.IsChecked == true ? (all.IndexOf(c) >= 0 ? all : all + c) : all.Replace(c.ToString(), "");
                    S.QuietDays = new string(all.OrderBy(x => x).ToArray());
                    SaveSoon();
                };
                days.Children.Add(chip);
            }
            details.Children.Add(days);
            details.Children.Add(hint);
            box.Children.Add(details);
            return box;
        }

        Button LanguageChip(string code, string label)
        {
            bool on = (S.Language ?? "auto") == code;
            var b = Btn((on ? "✓ " : "") + label, () => { if ((S.Language ?? "auto") != code) ctl.ChangeLanguage(code); });
            if (on) { b.Foreground = G.B(Accent); b.FontWeight = FontWeight.SemiBold; }
            return b;
        }

        void BuildGeneral(StackPanel root)
        {
            var body = Section(root, L.T("一般"), null);
            Row(body, L.LanguageLabel, L.T("選單、設定頁和桌寵說的話都會換成這個語言"), null);
            var langs = new WrapPanel { Margin = new Thickness(0, -2, 0, 6) };
            langs.Children.Add(LanguageChip("auto", L.T("自動（跟隨系統）")));
            foreach (var li in L.Languages) langs.Children.Add(LanguageChip(li.Code, li.Native));
            body.Children.Add(langs);
            Row(body, L.T("開機自動啟動"), Integration.AutostartHint, Toggle(S.AutoStart, v => { S.AutoStart = v; Integration.SetAutostart(v); SaveSoon(); }));
            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 4) };
            buttons.Children.Add(Btn(L.T("設定資料夾"), () => Integration.OpenPath(AppPaths.DataDir)));
            buttons.Children.Add(Btn(L.T("記錄檔"), () => Integration.OpenPath(Path.Combine(AppPaths.LogDir, "app.log"))));
            buttons.Children.Add(Btn(L.T("程式資料夾"), () => Integration.OpenPath(AppPaths.ExeDir)));
            body.Children.Add(new Border { Height = 1, Background = G.B(LineC) });
            body.Children.Add(buttons);
            var about = Txt(L.T("資料來源：Claude 讀取桌面版自己寫的用量快取（每 15 分鐘更新，重置時間為推算）；Codex 透過官方 codex app-server 即時查詢，並讀取本機對話紀錄；Copilot 讀取 CLI 的額度快取。本程式不讀取、也不傳送任何登入憑證。"),
                11.5, SubC, FontWeight.Normal);
            about.Margin = new Thickness(0, 4, 0, 12);
            body.Children.Add(about);
        }
    }
}
