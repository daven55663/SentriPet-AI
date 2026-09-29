using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SentriPet
{
    /// <summary>
    /// The shareable weekly summary (#20): a 1200 × 675 picture of the last 7 days — tokens, API-equivalent cost, the
    /// favourite model, how much of each weekly/monthly quota is used — with the pets. No folder names (they can be
    /// private). Saved as a PNG in the Pictures folder.
    /// </summary>
    static class ShareCard
    {
        const int W = 1200, H = 675;
        static readonly Color TextC = Palette.Hex("#F4F6FA");
        static readonly Color SubC = Palette.Hex("#B8C0CE");

        /// <summary>Where the pictures go: Pictures/SentriPet (or the settings folder when there is no Pictures folder).</summary>
        public static string Folder
        {
            get
            {
                string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                return Path.Combine(string.IsNullOrEmpty(pics) ? AppPaths.DataDir : pics, AppInfo.Name);
            }
        }

        /// <summary>Draws the card for the host's data and saves it; returns the file.</summary>
        public static string Save(IReportHost host, string file, DateTime nowLocal)
        {
            var card = Build(host, nowLocal);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            using (var bmp = Snapshots.Draw(card, 1)) bmp.Save(file);
            return file;
        }

        static TextBlock T(string text, double size, Color c, FontWeight w)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = G.B(c), FontWeight = w, FontFamily = G.Ui, TextWrapping = TextWrapping.Wrap };
        }

        static Control Build(IReportHost host, DateTime nowLocal)
        {
            var from = nowLocal.Date.AddDays(-6);
            var entries = (host.Ledger != null ? host.Ledger.Entries() : new List<TokenEntry>()).Where(e => e.At.ToLocalTime() >= from).ToList();
            var root = new Grid { Width = W, Height = H, Background = Wallpaper() };
            RenderOptions.SetTextRenderingMode(root, TextRenderingMode.Antialias);
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(660) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel { Margin = new Thickness(64, 56, 20, 40), Spacing = 6 };
            left.Children.Add(T(L.T("這週的 AI 用量"), 44, TextC, FontWeight.Bold));
            left.Children.Add(T(from.Month + "/" + from.Day + " – " + nowLocal.Month + "/" + nowLocal.Day, 20, SubC, FontWeight.Normal));

            // three numbers
            var tiles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 26, 0, 0) };
            double cost = entries.Select(e => ApiPrices.Cost(e)).Where(c => c.HasValue).Sum(c => c.Value);
            var model = TokenLedger.ByModel(entries).Select(m => m.Key).FirstOrDefault();
            tiles.Children.Add(Tile(L.T("token 合計"), Fmt.Tokens(entries.Sum(e => (double)e.Total))));
            tiles.Children.Add(Tile(L.T("API 等值"), Fmt.Usd(cost)));
            tiles.Children.Add(Tile(L.T("最常用的模型"), model != null ? Fmt.ModelName(model) : "—"));
            left.Children.Add(tiles);

            // the weekly / monthly quotas
            var quotas = new StackPanel { Spacing = 12, Margin = new Thickness(0, 30, 0, 0) };
            var results = host.History != null ? host.History.Results : new List<WindowResult>();
            foreach (var v in host.Views)
                foreach (var m in v.Meters.Where(x => !x.Unlimited && x.WindowMinutes >= UsageHistory.ReportWindowMinutes).Take(1))
                {
                    var last = results.LastOrDefault(r => r.Provider == v.Id && r.Meter == m.Key);
                    quotas.Children.Add(QuotaRow(v, m, last));
                }
            left.Children.Add(quotas);
            left.Children.Add(Days(entries, from, host));
            root.Children.Add(left);

            // the pets
            var theme = ThemeCatalog.Get("pet").Create();
            theme.Attach(new Snapshots.PreviewHost());
            theme.Update(host.Views);
            for (int i = 0; i < 45; i++) theme.Tick(1 / 30.0);
            var pets = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(1.25, 1.25), Child = theme.Root,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 36, 30),
            };
            Grid.SetColumn(pets, 1);
            root.Children.Add(pets);

            var brand = T(AppInfo.Name + " · github.com/daven55663/SentriPet-AI", 15, SubC, FontWeight.SemiBold);
            brand.HorizontalAlignment = HorizontalAlignment.Right;
            brand.VerticalAlignment = VerticalAlignment.Bottom;
            brand.Margin = new Thickness(0, 0, 36, 26);
            brand.Opacity = 0.8;
            Grid.SetColumnSpan(brand, 2);
            root.Children.Add(brand);
            return root;
        }

        /// <summary>The week's tokens per day, stacked by AI.</summary>
        static Control Days(List<TokenEntry> entries, DateTime from, IReportHost host)
        {
            const double Hgt = 64;
            var box = new StackPanel { Margin = new Thickness(0, 26, 0, 0), Spacing = 6 };
            box.Children.Add(T(L.T("每天的 token"), 15, SubC, FontWeight.Normal));
            var days = TokenLedger.ByDay(entries, 7, from.AddDays(6));
            long max = Math.Max(1, days.Max(d => d.Value.Values.Sum()));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            foreach (var d in days)
            {
                var col = new StackPanel { Width = 58 };
                var inner = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                foreach (var s in d.Value.Keys.OrderBy(k => k == "claude" ? 1 : 0))
                {
                    var v = host.Views.FirstOrDefault(x => x.Id == s);
                    var c = v != null ? G.Vivid(v.Color.ToColor()) : Palette.FromId(s);
                    inner.Children.Add(new Border { Height = Math.Max(2, Hgt * d.Value[s] / max), Background = G.B(c, 0.9), CornerRadius = new CornerRadius(3) });
                }
                var frame = new Grid { Height = Hgt };
                frame.Children.Add(inner);
                col.Children.Add(frame);
                var label = T(d.Key.Month + "/" + d.Key.Day, 13, SubC, FontWeight.Normal);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.Margin = new Thickness(0, 4, 0, 0);
                col.Children.Add(label);
                row.Children.Add(col);
            }
            box.Children.Add(row);
            return box;
        }

        static Control Tile(string label, string value)
        {
            var sp = new StackPanel { Spacing = 2 };
            sp.Children.Add(T(label, 15, SubC, FontWeight.Normal));
            var v = T(value, 28, TextC, FontWeight.Bold);
            v.TextWrapping = TextWrapping.NoWrap;
            v.TextTrimming = TextTrimming.CharacterEllipsis;
            v.MaxWidth = 170;
            sp.Children.Add(v);
            return new Border
            {
                Background = G.B(Palette.Hex("#0B0E14"), 0.42), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 12, 18, 14),
                Width = 180, Child = sp,
            };
        }

        static Control QuotaRow(ProviderView v, Meter m, WindowResult last)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = G.B(G.Vivid(v.Color.ToColor())), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            name.Children.Add(T(v.Name + " · " + m.Label, 19, TextC, FontWeight.SemiBold));
            g.Children.Add(name);
            var right = new StackPanel { Spacing = 5 };
            double f = Math.Max(0.01, Math.Min(1, m.Used / 100));
            var track = new Grid { Height = 12, Background = G.B(Colors.White, 0.12) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(f, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - f + 1e-6, GridUnitType.Star) });
            track.Children.Add(new Border { Background = G.B(G.Vivid(v.Color.ToColor())), CornerRadius = new CornerRadius(6) });
            right.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = track });
            right.Children.Add(T(last != null
                ? L.F("這期用了 {0} · 上期 {1}", Fmt.Pct(m.Used), Fmt.Pct(last.Used))
                : L.F("這期用了 {0}", Fmt.Pct(m.Used)), 14.5, SubC, FontWeight.Normal));
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            return g;
        }

        static IBrush Wallpaper()
        {
            var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
            b.GradientStops.Add(new GradientStop(Palette.Hex("#34566A"), 0));
            b.GradientStops.Add(new GradientStop(Palette.Hex("#17232C"), 0.55));
            b.GradientStops.Add(new GradientStop(Palette.Hex("#5A4169"), 1));
            return b;
        }
    }
}
