using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SentriPet
{
    class SettingsWindow : Window
    {
        static readonly Color Bg = Palette.Hex("#14161C");
        static readonly Color PanelBg = Palette.Hex("#1B1E26");
        static readonly Color LineC = Palette.Hex("#2A2E39");
        static readonly Color TextC = Palette.Hex("#E8EAF0");
        static readonly Color SubC = Palette.Hex("#8F98A8");
        static readonly Color Accent = Palette.Hex("#7C9CFF");

        readonly Controller ctl;
        readonly WrapPanel gallery = new WrapPanel();
        readonly Dictionary<string, Button> cards = new Dictionary<string, Button>();
        readonly StackPanel providerList = new StackPanel();
        readonly StackPanel otherList = new StackPanel();
        readonly DispatcherTimer refresh, saveTimer;
        CheckBox randomToggle;
        string providerSig;

        AppSettings S { get { return ctl.Settings; } }

        public SettingsWindow(Controller ctl)
        {
            this.ctl = ctl;
            Title = App.DisplayName + " · 設定";
            Width = 700;
            Height = 780;
            MinWidth = 560;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = G.B(Bg);
            Foreground = G.B(TextC);
            FontFamily = G.Ui;
            FontSize = 13;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            try { Icon = AppIcon(); } catch { }
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int on = 1;
                Native.DwmSetWindowAttribute(h, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4);
            };

            var root = new StackPanel { Margin = new Thickness(28, 20, 28, 30) };
            var scroll = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            scroll.Resources.Add(typeof(ScrollBar), FindResource("SlimScroll"));
            Content = scroll;

            BuildHeader(root);
            BuildThemes(root);
            BuildLook(root);
            BuildProviders(root);
            BuildAlerts(root);
            BuildGeneral(root);

            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); S.Save(); };
            refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            refresh.Tick += (s, e) => RefreshProviders();
            refresh.Start();
            Closed += (s, e) => { refresh.Stop(); if (saveTimer.IsEnabled) { saveTimer.Stop(); S.Save(); } };
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(RenderPreviews), DispatcherPriority.Background);
        }

        void SaveSoon()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        static ImageSource AppIcon()
        {
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe))
                return Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        // ------------------------------------------------------------------ layout helpers

        static TextBlock Txt(string s, double size, Color c, FontWeight w)
        {
            return new TextBlock { Text = s, FontSize = size, Foreground = G.B(c), FontWeight = w, TextWrapping = TextWrapping.Wrap };
        }

        StackPanel Section(StackPanel root, string title, string glyph, string subtitle)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 26, 0, 4) };
            head.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(Styles.IconFont), FontSize = 16, Foreground = G.B(Accent), Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(Txt(title, 17, TextC, FontWeights.Bold));
            root.Children.Add(head);
            if (subtitle != null)
            {
                var st = Txt(subtitle, 12, SubC, FontWeights.Normal);
                st.Margin = new Thickness(28, 0, 0, 10);
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

        void Row(StackPanel body, string label, string hint, FrameworkElement control)
        {
            if (body.Children.Count > 0) body.Children.Add(new Border { Height = 1, Background = G.B(LineC) });
            var g = new Grid { Margin = new Thickness(0, 11, 0, 11) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(Txt(label, 13.5, TextC, FontWeights.SemiBold));
            if (hint != null)
            {
                var h = Txt(hint, 11.5, SubC, FontWeights.Normal);
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

        CheckBox Toggle(bool value, Action<bool> set)
        {
            var cb = new CheckBox { Style = (Style)FindResource("Toggle"), IsChecked = value };
            cb.Checked += (s, e) => set(true);
            cb.Unchecked += (s, e) => set(false);
            return cb;
        }

        FrameworkElement SliderBox(double min, double max, double value, double step, Func<double, string> fmt, Action<double> set)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var sl = new Slider { Style = (Style)FindResource("Slim"), Minimum = min, Maximum = max, Value = value, Width = 190, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            var lbl = new TextBlock { Text = fmt(value), Width = 92, TextAlignment = TextAlignment.Right, Foreground = G.B(SubC), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            sl.ValueChanged += (s, e) => { lbl.Text = fmt(e.NewValue); set(e.NewValue); };
            sp.Children.Add(sl);
            sp.Children.Add(lbl);
            return sp;
        }

        Button Btn(string text, string glyph, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null) sp.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(Styles.IconFont), FontSize = 13, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Style = (Style)FindResource("Btn"), Content = sp, Margin = new Thickness(0, 0, 8, 0) };
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
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var logo = new Image { Width = 52, Height = 52, Margin = new Thickness(0, 0, 14, 0) };
            try { logo.Source = IconMaker.Render(104); } catch { }
            g.Children.Add(logo);
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 1);
            t.Children.Add(Txt(App.DisplayName, 22, TextC, FontWeights.Bold));
            var detected = ctl.Views.Count;
            t.Children.Add(Txt("v" + App.Version + " · 目前顯示 " + detected + " 個 AI · 拖曳桌寵可移到任何一個螢幕", 12, SubC, FontWeights.Normal));
            g.Children.Add(t);
            root.Children.Add(g);
        }

        void BuildThemes(StackPanel root)
        {
            var body = Section(root, "造型", "", "依照今天的心情挑一個吧，在桌寵上按右鍵也能隨時換");
            gallery.Margin = new Thickness(-6, 10, -6, 8);
            foreach (var t in ThemeCatalog.All)
            {
                var info = t;
                var img = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var wall = new LinearGradientBrush(Palette.Hex("#3A6073"), Palette.Hex("#6D4C7D"), 35);
                var preview = new Border { Height = 118, CornerRadius = new CornerRadius(10), Background = wall, Child = img, Padding = new Thickness(6), ClipToBounds = true };
                preview.Child = new TextBlock { Text = "繪製預覽中…", Foreground = G.B(Colors.White, 0.6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
                var sp = new StackPanel();
                sp.Children.Add(preview);
                var name = Txt(info.Name, 13.5, TextC, FontWeights.Bold);
                name.Margin = new Thickness(2, 8, 0, 0);
                sp.Children.Add(name);
                var mood = Txt(info.Mood + " · " + info.Blurb, 11, SubC, FontWeights.Normal);
                mood.Margin = new Thickness(2, 2, 0, 2);
                sp.Children.Add(mood);
                var btn = new Button { Style = (Style)FindResource("Card"), Content = sp, Width = 190, Margin = new Thickness(6), Tag = new object[] { preview, img } };
                btn.Click += (s, e) => ctl.ChangeTheme(info.Id, true);
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
            Row(body, "每天隨機換一個造型", "每天第一次見面時自動換成別的造型，給自己一點驚喜", randomToggle);
            OnThemeChanged();
        }

        public void OnThemeChanged()
        {
            foreach (var kv in cards) kv.Value.BorderBrush = G.B(kv.Key == S.Theme ? Accent : Colors.Transparent);
            if (randomToggle != null && randomToggle.IsChecked != S.DailyRandomTheme) randomToggle.IsChecked = S.DailyRandomTheme;
        }

        public void RenderPreviewsNow() { RenderPreviews(); }

        void RenderPreviews()
        {
            var data = ctl.Views.Count > 0 ? ctl.Views : Snapshots.MockA();
            foreach (var info in ThemeCatalog.All)
            {
                try
                {
                    var t = info.Create();
                    t.Attach(new Snapshots.PreviewHost());
                    t.Update(data);
                    for (int i = 0; i < 40; i++) t.Tick(1 / 30.0);
                    var bmp = Snapshots.RenderToBitmap(t.Root, 1.0);
                    var parts = (object[])cards[info.Id].Tag;
                    var img = (Image)parts[1];
                    img.Source = bmp;
                    ((Border)parts[0]).Child = img;
                }
                catch (Exception ex) { Log.Error("preview " + info.Id, ex); }
            }
        }

        void BuildLook(StackPanel root)
        {
            var body = Section(root, "外觀", "", null);
            Row(body, "大小", null, SliderBox(50, 200, Math.Round(S.Scale * 100), 5, v => Math.Round(v) + "%", v => { S.Scale = v / 100; ctl.Widget.ApplySettings(); SaveSoon(); }));
            Row(body, "不透明度", "調低一點可以若隱若現", SliderBox(30, 100, Math.Round(S.Opacity * 100), 5, v => Math.Round(v) + "%", v => { S.Opacity = v / 100; ctl.Widget.ApplySettings(); SaveSoon(); }));
            Row(body, "永遠在最上層", "不會被其他視窗蓋住", Toggle(S.AlwaysOnTop, v => { S.AlwaysOnTop = v; ctl.Widget.ApplySettings(); SaveSoon(); }));
            Row(body, "全螢幕時自動躲起來", "看影片、玩遊戲、簡報時不會擋畫面", Toggle(S.HideOnFullscreen, v => { S.HideOnFullscreen = v; SaveSoon(); }));
            Row(body, "滑鼠穿透", "桌寵不會擋住點擊（要關閉請從系統匣圖示按右鍵）", Toggle(S.ClickThrough, v => { S.ClickThrough = v; ctl.Widget.ApplySettings(); SaveSoon(); }));
            Row(body, "會說話", "偶爾冒出一句話、點牠會回應", Toggle(S.Chatty, v => { S.Chatty = v; SaveSoon(); }));
            Row(body, "省電模式", "降低動畫幀率，筆電用電池時可以開", Toggle(S.LowPower, v => { S.LowPower = v; ctl.Widget.ApplySettings(); SaveSoon(); }));
        }

        void BuildProviders(StackPanel root)
        {
            var body = Section(root, "AI 服務", "", "自動偵測電腦上的 AI 工具。資料只在本機讀取，不會上傳到任何地方。");
            body.Children.Add(providerList);
            body.Children.Add(otherList);
            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
            buttons.Children.Add(Btn("重新偵測", "", () => { ctl.Service.Redetect(); ctl.Service.RefreshNow(null); providerSig = null; }));
            buttons.Children.Add(Btn("重新載入外掛", "", () => { ctl.Service.ReloadProviders(); providerSig = null; }));
            buttons.Children.Add(Btn("開啟外掛資料夾", "", OpenPluginFolder));
            body.Children.Add(new Border { Height = 1, Background = G.B(LineC) });
            body.Children.Add(buttons);

            Row(body, "Codex 即時查詢", "透過官方 codex app-server 讀取最新用量；設 0 只讀本機對話紀錄",
                SliderBox(0, 30, S.CodexLiveMinutes, 1, v => v < 1 ? "關閉" : "每 " + Math.Round(v) + " 分鐘", v => { S.CodexLiveMinutes = (int)Math.Round(v); SaveSoon(); }));

            var box = new TextBox { Style = (Style)FindResource("Input"), Width = 150, Text = S.ClaudeWeeklyReset ?? "" };
            var hint = Txt("", 11, SubC, FontWeights.Normal);
            Action check = () =>
            {
                DateTime? next;
                if (string.IsNullOrWhiteSpace(box.Text)) hint.Text = "留空 = 自動推算";
                else if (ClaudeProvider.TryParseWeekly(box.Text, out next)) hint.Text = "下次重置：" + Fmt.When(next) + "（" + Fmt.Countdown(next) + "後）";
                else hint.Text = "看不懂這個時間，例如：週四 23:00";
            };
            box.TextChanged += (s, e) =>
            {
                check();
                DateTime? next;
                if (string.IsNullOrWhiteSpace(box.Text) || ClaudeProvider.TryParseWeekly(box.Text, out next))
                {
                    S.ClaudeWeeklyReset = box.Text.Trim();
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
            Row(body, "Claude 每週重置時間（選填）", "Claude 的快取沒有重置時間，預設用歷史紀錄推算。想要精準，照 Claude 設定 → 用量 頁面上寫的時間填一次，例如「週四 23:00」", right);
            Row(body, "Claude 即時推算", "Claude 桌面版約每 15 分鐘才記錄一次用量。開啟後會讀 Claude Code 本機對話紀錄裡的 token 數（不讀內容），推算這段空檔的用量，數字前面會標「≈」",
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
            Controller.OpenPath(AppPaths.ProvidersDir);
        }

        void RefreshProviders()
        {
            var svc = ctl.Service;
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
                g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = G.B(G.Vivid(p.Color)), Margin = new Thickness(0, 4, 12, 0), VerticalAlignment = VerticalAlignment.Top });
                var info = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                Grid.SetColumn(info, 1);
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
                nameRow.Children.Add(Txt(p.Name, 14, TextC, FontWeights.Bold));
                if (!p.BuiltIn)
                    nameRow.Children.Add(G.Pill(Txt("外掛", 10, Accent, FontWeights.SemiBold), G.B(Accent, 0.15), 6, new Thickness(6, 0, 6, 1)));
                if (nameRow.Children.Count > 1) ((FrameworkElement)nameRow.Children[1]).Margin = new Thickness(8, 2, 0, 0);
                info.Children.Add(nameRow);
                info.Children.Add(Txt(installed ? "偵測到：" + d.EvidenceText : (d != null && d.Hint != null ? d.Hint : "這台電腦上沒有偵測到"), 11.5, SubC, FontWeights.Normal));
                if (installed)
                {
                    var st = Txt(StatusText(d, r.Item3), 11.5, r.Item3 != null && r.Item3.Error != null && !r.Item3.Offline ? Palette.Hex("#FCA5A5") : Palette.Hex("#86EFAC"), FontWeights.Normal);
                    st.Margin = new Thickness(0, 2, 0, 0);
                    info.Children.Add(st);
                }
                g.Children.Add(info);
                var id = p.Id;
                FrameworkElement right;
                if (installed) right = Toggle(S.IsEnabled(id), v => { S.Enabled[id] = v; SaveSoon(); providerSig = null; });
                else right = Txt("未安裝", 12, SubC, FontWeights.Normal);
                right.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(right, 2);
                g.Children.Add(right);
                providerList.Children.Add(g);
            }

            otherList.Children.Clear();
            if (hits.Count > 0)
            {
                otherList.Children.Add(new Border { Height = 1, Background = G.B(LineC) });
                var t = Txt("其他偵測到的 AI（還沒有用量來源，可以寫外掛接上）", 12, SubC, FontWeights.SemiBold);
                t.Margin = new Thickness(0, 10, 0, 4);
                otherList.Children.Add(t);
                foreach (var h in hits)
                {
                    var line = Txt("•  " + h.Key.Name + " — " + h.Value.EvidenceText, 12, TextC, FontWeights.Normal);
                    line.Margin = new Thickness(6, 2, 0, 2);
                    otherList.Children.Add(line);
                }
                otherList.Children.Add(new Border { Height = 8 });
            }
        }

        static string StatusText(Detection d, Snapshot s)
        {
            if (d == null) return "偵測中…";
            if (!d.Installed) return "";
            if (s == null) return "讀取中…";
            if (s.Offline) return s.Error ?? "離線";
            if (s.Error != null) return "✗ " + s.Error;
            var meters = string.Join("、", s.Meters.Take(3).Select(m => m.Label + " 剩 " + (m.Unlimited ? "∞" : Fmt.Pct(m.Remaining))));
            string when = s.ObservedAt.HasValue ? s.ObservedAt.Value.ToLocalTime().ToString("HH:mm") : "";
            return "✓ " + meters + " · " + (s.Source ?? "") + (when.Length > 0 ? " · " + when : "") + (s.Stale ? " · 資料較舊" : "");
        }

        void BuildAlerts(StackPanel root)
        {
            var body = Section(root, "提醒", "", null);
            Row(body, "額度提醒通知", "用量越過門檻、或額度重置時跳出 Windows 通知", Toggle(S.Notifications, v => { S.Notifications = v; SaveSoon(); }));
            Row(body, "催我用完週額度", "每週／每月額度快重置、卻還剩不少時，桌寵會拿鬧鐘催你把它用掉：剩 2 天（還有 30% 以上）開始提醒，最後一天、最後 6 小時會越催越勤，並各跳一次通知",
                Toggle(S.UseItReminder, v => { S.UseItReminder = v; SaveSoon(); ctl.RefreshViews(); }));
            Row(body, "提醒門檻", "用量超過這個比例時提醒一次", SliderBox(50, 95, S.WarnAt, 5, v => "用掉 " + Math.Round(v) + "%", v => { S.WarnAt = (int)Math.Round(v); SaveSoon(); }));
            Row(body, "緊急門檻", "快用完時再提醒一次", SliderBox(60, 100, S.CriticalAt, 1, v => "用掉 " + Math.Round(v) + "%", v => { S.CriticalAt = (int)Math.Round(v); SaveSoon(); }));
        }

        void BuildGeneral(StackPanel root)
        {
            var body = Section(root, "一般", "", null);
            Row(body, "開機自動啟動", "登入 Windows 後自動出現在桌面上", Toggle(S.AutoStart, v => { S.AutoStart = v; Autostart.Set(v); SaveSoon(); }));
            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
            buttons.Children.Add(Btn("設定資料夾", "", () => Controller.OpenPath(AppPaths.DataDir)));
            buttons.Children.Add(Btn("記錄檔", "", () => Controller.OpenPath(Path.Combine(AppPaths.LogDir, "app.log"))));
            buttons.Children.Add(Btn("程式資料夾", "", () => Controller.OpenPath(AppPaths.ExeDir)));
            body.Children.Add(new Border { Height = 1, Background = G.B(LineC) });
            body.Children.Add(buttons);
            var about = Txt(
                "資料來源：Claude 讀取桌面版自己寫的用量快取（每 15 分鐘更新，重置時間為推算）；" +
                "Codex 透過官方 codex app-server 即時查詢，並讀取本機對話紀錄；Copilot 讀取 CLI 的額度快取。" +
                "本程式不讀取、也不傳送任何登入憑證。", 11.5, SubC, FontWeights.Normal);
            about.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(about);
        }
    }
}
