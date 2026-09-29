using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SentriPet
{
    /// <summary>What the report window needs from the app (the snapshot and the self-test give sample data).</summary>
    interface IReportHost
    {
        AppSettings Settings { get; }
        List<ProviderView> Views { get; }
        UsageHistory History { get; }
        TokenLedger Ledger { get; }
    }

    /// <summary>
    /// The usage report (#18): how much of each weekly/monthly quota was used (#16), the tokens per day of the last 7 or
    /// 30 days by AI, the busiest folders and the models. Numbers only — the logs' contents are never read.
    /// </summary>
    class ReportWindow : Window
    {
        static readonly Color Bg = Palette.Hex("#14161C");
        static readonly Color PanelBg = Palette.Hex("#1B1E26");
        static readonly Color LineC = Palette.Hex("#2A2E39");
        static readonly Color TextC = Palette.Hex("#E8EAF0");
        static readonly Color SubC = Palette.Hex("#8F98A8");
        static readonly Color Accent = Palette.Hex("#7C9CFF");

        readonly IReportHost host;
        readonly StackPanel tokens = new StackPanel();
        readonly StackPanel cost = new StackPanel();
        readonly Dictionary<int, Button> rangeButtons = new Dictionary<int, Button>();
        int days = 7;
        bool loaded;

        /// <summary>The page content (the snapshot renders it).</summary>
        public Control Page { get; private set; }

        // for the self-test
        internal int DayColumns { get; private set; }
        internal readonly List<string> ProjectNames = new List<string>();
        internal void ShowDays(int d) { days = d; Fill(); }

        public ReportWindow(IReportHost host, bool load)
        {
            this.host = host;
            Title = AppInfo.Name + " · " + L.T("用量報告");
            Width = 760;
            Height = 820;
            MinWidth = 600;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = G.B(Bg);
            Foreground = G.B(TextC);
            FontFamily = G.Ui;
            FontSize = 13;
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://SentriPet/Assets/app.ico"))); } catch { }

            var root = new StackPanel { Margin = new Thickness(28, 20, 28, 30), Background = G.B(Bg) };
            root.Children.Add(Txt(L.T("用量報告"), 22, TextC, FontWeight.Bold));
            root.Children.Add(Txt(L.T("只讀 Claude Code 與 Codex 本機紀錄裡的數字（token 數、模型、資料夾名稱），不讀對話內容"), 12, SubC, FontWeight.Normal));
            BuildQuotaUse(root);
            BuildTokens(root);
            BuildCost(root);
            Page = root;
            Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            if (load) Opened += (s, e) => Load();
            else { loaded = true; Fill(); }
        }

        // ------------------------------------------------------------------ layout helpers (like the settings page)

        static TextBlock Txt(string s, double size, Color c, FontWeight w)
        {
            return new TextBlock { Text = s, FontSize = size, Foreground = G.B(c), FontWeight = w, TextWrapping = TextWrapping.Wrap };
        }

        static StackPanel Section(StackPanel root, string title, string subtitle, Control right)
        {
            var headRow = new Grid { Margin = new Thickness(2, 26, 0, 4) };
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.Children.Add(Txt(title, 17, TextC, FontWeight.Bold));
            if (right != null) { Grid.SetColumn(right, 1); headRow.Children.Add(right); }
            root.Children.Add(headRow);
            if (subtitle != null)
            {
                var st = Txt(subtitle, 12, SubC, FontWeight.Normal);
                st.Margin = new Thickness(2, 0, 0, 10);
                root.Children.Add(st);
            }
            var body = new StackPanel();
            root.Children.Add(new Border { Background = G.B(PanelBg), BorderBrush = G.B(LineC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 12, 18, 14), Child = body });
            return body;
        }

        Color ColorOf(string source)
        {
            var v = host.Views.FirstOrDefault(x => x.Id == source);
            if (v != null) return G.Vivid(v.Color.ToColor());
            if (source == "claude") return G.Vivid(new ClaudeProvider().Color.ToColor());
            if (source == "codex") return G.Vivid(new CodexProvider().Color.ToColor());
            return Palette.FromId(source ?? "");
        }

        static string SourceName(string source) { return source == "claude" ? "Claude Code" : source == "codex" ? "Codex" : source; }

        // ------------------------------------------------------------------ quota use (#16)

        void BuildQuotaUse(StackPanel root)
        {
            var body = Section(root, L.T("額度利用率"), L.T("每週／每月額度重置時，記下那一期用掉多少，看看有沒有浪費"), null);
            var results = host.History != null ? host.History.Results : new List<WindowResult>();
            int rows = 0;
            foreach (var v in host.Views)
                foreach (var m in v.Meters.Where(x => !x.Unlimited && x.WindowMinutes >= UsageHistory.ReportWindowMinutes))
                {
                    var past = results.Where(r => r.Provider == v.Id && r.Meter == m.Key).ToList();
                    if (past.Count > 8) past = past.GetRange(past.Count - 8, 8);
                    if (rows++ > 0) body.Children.Add(new Border { Height = 1, Background = G.B(LineC), Margin = new Thickness(0, 10, 0, 10) });
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    left.Children.Add(Txt(v.Name + " · " + m.Label, 13.5, TextC, FontWeight.SemiBold));
                    var last = past.Count > 0 ? past[past.Count - 1] : null;
                    left.Children.Add(Txt(last != null
                        ? L.F("上期 {0} · 平均 {1} · 這期 {2}", Fmt.Pct(last.Used), Fmt.Pct(past.Average(r => r.Used)), Fmt.Pct(m.Used))
                        : L.F("這期到目前用了 {0}", Fmt.Pct(m.Used)), 11.5, SubC, FontWeight.Normal));
                    g.Children.Add(left);
                    var bars = ReportChart.Bars(past, m, 40, 24, true);
                    Grid.SetColumn(bars, 1);
                    g.Children.Add(bars);
                    body.Children.Add(g);
                }
            if (rows == 0) body.Children.Add(Txt(L.T("目前沒有每週或每月的額度"), 12, SubC, FontWeight.Normal));
        }

        // ------------------------------------------------------------------ tokens (#18)

        void BuildTokens(StackPanel root)
        {
            var range = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var d in new[] { 7, 30 })
            {
                int dd = d;
                var b = new Button { Content = L.F("最近 {0} 天", d), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 5), CornerRadius = new CornerRadius(8) };
                b.Click += (s, e) => { days = dd; Fill(); };
                rangeButtons[d] = b;
                range.Children.Add(b);
            }
            var body = Section(root, L.T("每天用了多少 token"), L.T("Claude Code 與 Codex 每天的 token 數（輸入、輸出、快取讀寫合計）"), range);
            body.Children.Add(tokens);
            tokens.Children.Add(Txt(L.T("正在統計最近 30 天的紀錄…"), 12.5, SubC, FontWeight.Normal));
        }

        // ------------------------------------------------------------------ API-equivalent cost (#19)

        void BuildCost(StackPanel root)
        {
            var body = Section(root, L.T("API 等值費用"),
                L.F("把上面的 token 數乘上官方 API 價格（{0} 查的價格），估算如果改用 API 付費大約要花多少。訂閱方案和 API 的計價方式不同，只供參考",
                    ApiPrices.CheckedOn == DateTime.MinValue ? "?" : ApiPrices.CheckedOn.ToString("yyyy-MM-dd")), null);
            body.Children.Add(cost);
        }

        /// <summary>The cost section for the chosen range.</summary>
        void FillCost(List<TokenEntry> inRange)
        {
            cost.Children.Clear();
            CostTotal = null;
            if (inRange.Count == 0) { cost.Children.Add(Txt(L.T("這段期間沒有 Claude Code 或 Codex 的紀錄"), 12.5, SubC, FontWeight.Normal)); return; }
            var priced = inRange.Select(e => new { E = e, Cost = ApiPrices.Cost(e) }).ToList();
            double total = priced.Where(p => p.Cost.HasValue).Sum(p => p.Cost.Value);
            CostTotal = total;
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(Txt(L.F("最近 {0} 天約 {1}", days, Fmt.Usd(total)), 15, TextC, FontWeight.Bold));
            foreach (var s in priced.Select(p => p.E.Source).Distinct().OrderBy(s => s == "claude" ? 0 : 1))
            {
                head.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = G.B(ColorOf(s)), Margin = new Thickness(18, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                var t = Txt(SourceName(s) + " " + Fmt.Usd(priced.Where(p => p.E.Source == s && p.Cost.HasValue).Sum(p => p.Cost.Value)), 12.5, SubC, FontWeight.Normal);
                t.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(t);
            }
            cost.Children.Add(head);
            // per model: what it costs, and which models have no known price
            var models = priced.GroupBy(p => p.E.Model ?? "?")
                               .Select(g => new { Model = g.Key, Source = g.First().E.Source, Known = g.First().Cost.HasValue, Cost = g.Where(p => p.Cost.HasValue).Sum(p => p.Cost.Value), Tokens = g.Sum(p => p.E.Total) })
                               .OrderByDescending(m => m.Cost).ThenByDescending(m => m.Tokens).ToList();
            double top = Math.Max(0.01, models.Count > 0 ? models.Max(m => m.Cost) : 0.01);
            foreach (var m in models.Take(8))
            {
                if (m.Known) cost.Children.Add(BarRow(ColorOf(m.Source), m.Model, null, (long)Math.Round(m.Cost * 100), (long)Math.Round(top * 100), null, Fmt.Usd(m.Cost) + " · " + Fmt.Pct(100 * m.Cost / Math.Max(0.01, total))));
                else cost.Children.Add(Txt(L.F("{0}：官方價格表上沒有這個模型，不計入（{1} token）", m.Model, Fmt.Tokens(m.Tokens)), 12, SubC, FontWeight.Normal));
            }
            var note = Txt(L.F("價格來源：{0}", string.Join("、", ApiPrices.Sources)), 11, SubC, FontWeight.Normal);
            note.Margin = new Thickness(0, 10, 0, 0);
            note.Opacity = 0.8;
            cost.Children.Add(note);
        }

        /// <summary>For the self-test: the total of the cost section (USD), or null.</summary>
        internal double? CostTotal { get; private set; }

        void Load()
        {
            var ledger = host.Ledger;
            if (ledger == null) return;
            Task.Run(() =>
            {
                try { ledger.Update(DateTime.UtcNow); }
                catch (Exception ex) { Log.Error("token ledger", ex); }
            }).ContinueWith(t => Dispatcher.UIThread.Post(() => { loaded = true; Fill(); }));
        }

        /// <summary>The token sections for the chosen range.</summary>
        void Fill()
        {
            foreach (var kv in rangeButtons)
            {
                kv.Value.Foreground = G.B(kv.Key == days ? Accent : SubC);
                kv.Value.FontWeight = kv.Key == days ? FontWeight.SemiBold : FontWeight.Normal;
            }
            if (!loaded) return;
            tokens.Children.Clear();
            DayColumns = 0;
            ProjectNames.Clear();
            var today = DateTime.Now.Date;
            var all = host.Ledger != null ? host.Ledger.Entries() : new List<TokenEntry>();
            var from = today.AddDays(1 - days).ToUniversalTime();
            var inRange = all.Where(e => e.At >= from).ToList();
            FillCost(inRange);
            if (inRange.Count == 0)
            {
                tokens.Children.Add(Txt(L.T("這段期間沒有 Claude Code 或 Codex 的紀錄"), 12.5, SubC, FontWeight.Normal));
                return;
            }
            var sources = inRange.Select(e => e.Source).Distinct().OrderBy(s => s == "claude" ? 0 : 1).ToList();
            // totals
            var sum = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            sum.Children.Add(Txt(L.F("合計 {0}", Fmt.Tokens(inRange.Sum(e => (double)e.Total))), 15, TextC, FontWeight.Bold));
            foreach (var s in sources)
            {
                var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = G.B(ColorOf(s)), Margin = new Thickness(18, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                sum.Children.Add(dot);
                var t = Txt(SourceName(s) + " " + Fmt.Tokens(inRange.Where(e => e.Source == s).Sum(e => (double)e.Total)), 12.5, SubC, FontWeight.Normal);
                t.VerticalAlignment = VerticalAlignment.Center;
                sum.Children.Add(t);
            }
            tokens.Children.Add(sum);
            tokens.Children.Add(DayChart(TokenLedger.ByDay(inRange, days, today), sources));

            // busiest folders
            var projects = TokenLedger.ByProject(inRange, 8);
            tokens.Children.Add(Txt(L.T("專案排行"), 14, TextC, FontWeight.Bold));
            ((TextBlock)tokens.Children[tokens.Children.Count - 1]).Margin = new Thickness(0, 18, 0, 6);
            long top = projects.Count > 0 ? projects[0].Value : 1;
            foreach (var p in projects)
            {
                var parts = p.Key.Split(new[] { '|' }, 2);
                ProjectNames.Add(parts[1]);
                tokens.Children.Add(BarRow(ColorOf(parts[0]), parts[1] == "?" ? L.T("（不明）") : parts[1], SourceName(parts[0]), p.Value, top, null));
            }

            // models
            var models = TokenLedger.ByModel(inRange);
            tokens.Children.Add(Txt(L.T("模型"), 14, TextC, FontWeight.Bold));
            ((TextBlock)tokens.Children[tokens.Children.Count - 1]).Margin = new Thickness(0, 18, 0, 6);
            double total = Math.Max(1, inRange.Sum(e => (double)e.Total));
            long topModel = models.Count > 0 ? models[0].Value : 1;
            foreach (var m in models.Take(8))
            {
                var src = inRange.First(e => (e.Model ?? "?") == m.Key).Source;
                tokens.Children.Add(BarRow(ColorOf(src), m.Key, null, m.Value, topModel, Fmt.Pct(100 * m.Value / total)));
            }
        }

        /// <summary>One bar per day, stacked by AI; a tooltip with the numbers.</summary>
        Control DayChart(List<KeyValuePair<DateTime, Dictionary<string, long>>> perDay, List<string> sources)
        {
            const double H = 150;
            long max = Math.Max(1, perDay.Max(d => d.Value.Values.Sum()));
            var chart = new Grid { Height = H + 22 };
            // guide lines at the top and the middle
            foreach (var f in new[] { 1.0, 0.5 })
                chart.Children.Add(new Border { Height = 1, Background = G.B(LineC), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, H * (1 - f), 0, 0) });
            var maxLabel = Txt(Fmt.Tokens(max), 10.5, SubC, FontWeight.Normal);
            maxLabel.HorizontalAlignment = HorizontalAlignment.Right;
            maxLabel.Margin = new Thickness(0, -15, 0, 0);
            maxLabel.VerticalAlignment = VerticalAlignment.Top;
            chart.Children.Add(maxLabel);
            var cols = new Grid();
            int n = perDay.Count;
            DayColumns = n;
            for (int i = 0; i < n; i++) cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < n; i++)
            {
                var day = perDay[i];
                var col = new Grid { Background = Brushes.Transparent };   // (the whole column shows the tooltip)
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(n > 14 ? 1.5 : 5, 0, n > 14 ? 1.5 : 5, 22) };
                foreach (var s in sources.AsEnumerable().Reverse())
                {
                    long v;
                    if (!day.Value.TryGetValue(s, out v) || v <= 0) continue;
                    stack.Children.Add(new Border { Height = Math.Max(1.5, H * v / max), Background = G.B(ColorOf(s), 0.9), CornerRadius = new CornerRadius(2) });
                }
                col.Children.Add(stack);
                bool label = n <= 14 || i % 5 == (n - 1) % 5;   // 7 days: every day; 30 days: every 5th, ending today
                if (label)
                    col.Children.Add(new TextBlock
                    {
                        Text = day.Key.Month + "/" + day.Key.Day, FontSize = 10, Foreground = G.B(SubC),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                    });
                var tip = new List<string> { Fmt.When(day.Key.ToUniversalTime()).Split(' ')[0] + " · " + L.F("合計 {0}", Fmt.Tokens(day.Value.Values.Sum())) };
                foreach (var s in sources) { long v; day.Value.TryGetValue(s, out v); tip.Add(SourceName(s) + " " + Fmt.Tokens(v)); }
                ToolTip.SetTip(col, string.Join("\n", tip));
                Grid.SetColumn(col, i);
                cols.Children.Add(col);
            }
            chart.Children.Add(cols);
            return chart;
        }

        /// <summary>A name, a proportional bar and the number (the folders and the models).</summary>
        Control BarRow(Color color, string name, string note, long value, long top, string share, string text = null)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = G.B(color), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            label.Children.Add(new TextBlock { Text = name, Foreground = G.B(TextC), FontSize = 12.5, MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            ToolTip.SetTip(label, note != null ? name + " · " + note : name);
            g.Children.Add(label);
            // the bar: two star columns in the proportion of the value to the largest one (layout only, no size events)
            double f = Math.Max(0.015, Math.Min(1, (double)value / Math.Max(1, top)));
            var track = new Grid { Height = 8, Background = G.B(Colors.White, 0.06) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(f, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - f + 1e-6, GridUnitType.Star) });
            track.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = G.B(color, 0.85) });
            var trackBox = new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = track, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(trackBox, 1);
            g.Children.Add(trackBox);
            var num = new TextBlock
            {
                Text = text ?? Fmt.Tokens(value) + (share != null ? " · " + share : ""), Foreground = G.B(SubC), FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(num, 2);
            g.Children.Add(num);
            return g;
        }
    }
}
