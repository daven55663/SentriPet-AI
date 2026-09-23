using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentriPet
{
    /// <summary>A sticky note with hand-drawn, pencil-shaded bars.</summary>
    class NoteTheme : Theme
    {
        static readonly Color InkBlue = Palette.Hex("#1D3E91");
        static readonly Color InkSoft = Palette.Hex("#51607F");
        const double BarW = 92, BarH = 12;

        class Line
        {
            public string Key;
            public Canvas Bar;
            public TextBlock Pct;
            public double Drawn = -1;
            public int Seed;
        }

        class Item
        {
            public string Id;
            public TextBlock Doodle, Reset, Error;
            public List<Line> Lines = new List<Line>();
            public string DoodleKind;
        }

        Grid root;
        StackPanel list;
        TextBlock date;
        readonly List<Item> items = new List<Item>();

        public override string Id { get { return "note"; } }
        public override string Name { get { return "手寫便利貼"; } }
        public override string Mood { get { return "慢慢來"; } }
        public override string Blurb { get { return "貼在螢幕角落的手寫小紙條"; } }

        protected override FrameworkElement CreateRoot()
        {
            root = new Grid { Margin = new Thickness(20, 16, 20, 20), LayoutTransform = new RotateTransform(-1.6) };
            var paper = new Border
            {
                Background = G.Vertical(Palette.Hex("#FFF7B0"), Palette.Hex("#FFEE85")),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(18, 22, 20, 18),
                Effect = G.Shadow(14, 5, 0.38, Colors.Black),
                MinWidth = 240,
            };
            var content = new StackPanel();
            var title = new Grid();
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title.Children.Add(G.T("今日 AI 額度", 17, InkBlue, FontWeights.Bold, G.Kai));
            date = G.T("", 14, InkSoft, FontWeights.Normal, G.Hand);
            date.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(date, 1);
            title.Children.Add(date);
            content.Children.Add(title);
            content.Children.Add(new Path
            {
                Data = Geometry.Parse("M 0,4 Q 12,0 24,4 T 48,4 T 72,4 T 96,4 T 120,4"),
                Stroke = G.B(InkBlue, 0.7),
                StrokeThickness = 1.4,
                Margin = new Thickness(0, 1, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            list = new StackPanel();
            content.Children.Add(list);
            paper.Child = content;
            root.Children.Add(paper);

            // folded corner
            var fold = new Path
            {
                Data = Geometry.Parse("M 0,22 L 22,0 L 22,22 Z"),
                Fill = G.Lg(Palette.Hex("#E8D66A"), Palette.Hex("#F9EC9C"), 135),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Effect = G.Shadow(4, 1, 0.25, Colors.Black),
            };
            root.Children.Add(fold);
            // masking tape
            var tape = new Rectangle
            {
                Width = 74, Height = 22,
                Fill = G.Vertical(Color.FromArgb(0xB8, 0xF4, 0xF1, 0xE8), Color.FromArgb(0xA0, 0xE8, 0xE2, 0xD2)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -11, 0, 0),
                RenderTransform = new RotateTransform(4),
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            root.Children.Add(tape);
            return root;
        }

        protected override void Rebuild()
        {
            list.Children.Clear();
            items.Clear();
            if (Views.Count == 0)
            {
                list.Children.Add(G.T("正在找電腦裡的 AI…", 13, InkBlue, FontWeights.Normal, G.Kai));
                return;
            }
            foreach (var v in Views) list.Children.Add(MakeItem(v));
        }

        static int Hash(string s)
        {
            int h = 17;
            foreach (char c in s ?? "") h = h * 31 + c;
            return h & 0x7FFFFFFF;
        }

        FrameworkElement MakeItem(ProviderView v)
        {
            var it = new Item { Id = v.Id };
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 6), Tag = "pv:" + v.Id, Background = G.B(Colors.White, 0.001) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(G.T("• ", 16, InkBlue, FontWeights.Bold, G.Hand));
            head.Children.Add(G.T(v.Name, 17, InkBlue, FontWeights.Bold, G.Hand));
            if (!string.IsNullOrEmpty(v.Plan))
            {
                var plan = G.T("(" + v.Plan + ")", 13, InkSoft, FontWeights.Normal, G.Hand);
                plan.Margin = new Thickness(5, 0, 0, 0);
                plan.VerticalAlignment = VerticalAlignment.Bottom;
                head.Children.Add(plan);
            }
            it.Doodle = G.T("", 15, Palette.Hex("#E0A100"), FontWeights.Bold, G.Hand);
            it.Doodle.Margin = new Thickness(8, 0, 0, 0);
            it.Doodle.RenderTransform = new RotateTransform(-8);
            head.Children.Add(it.Doodle);
            sp.Children.Add(head);

            if (!v.HasData)
            {
                it.Error = G.T("", 12, InkSoft, FontWeights.Normal, G.Kai);
                it.Error.TextWrapping = TextWrapping.Wrap;
                it.Error.MaxWidth = 220;
                it.Error.Margin = new Thickness(14, 0, 0, 0);
                sp.Children.Add(it.Error);
            }
            else
            {
                foreach (var m in v.Meters.Take(3))
                {
                    var ln = new Line { Key = m.Key, Seed = Hash(v.Id + m.Key) };
                    var g = new Grid { Margin = new Thickness(14, 2, 0, 0) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarW + 8) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var lbl = G.T(m.Label.Replace(" ", ""), 12.5, InkBlue, FontWeights.Normal, G.Kai);
                    lbl.VerticalAlignment = VerticalAlignment.Center;
                    g.Children.Add(lbl);
                    ln.Bar = new Canvas { Width = BarW, Height = BarH, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(ln.Bar, 1);
                    g.Children.Add(ln.Bar);
                    ln.Pct = G.T("", 14, InkBlue, FontWeights.Bold, G.Hand);
                    ln.Pct.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(ln.Pct, 2);
                    g.Children.Add(ln.Pct);
                    sp.Children.Add(g);
                    it.Lines.Add(ln);
                }
                it.Reset = G.T("", 11.5, InkSoft, FontWeights.Normal, G.Kai);
                it.Reset.Margin = new Thickness(14, 3, 0, 0);
                sp.Children.Add(it.Reset);
            }
            items.Add(it);
            return sp;
        }

        static Color Pencil(double rem)
        {
            if (rem >= 50) return Palette.Hex("#2E8B57");
            if (rem >= 20) return Palette.Hex("#D2691E");
            return Palette.Hex("#C0392B");
        }

        void DrawBar(Line ln, double rem)
        {
            ln.Bar.Children.Clear();
            var rng = new Random(ln.Seed);
            Func<double> j = () => (rng.NextDouble() - 0.5) * 1.6;
            // pencil hatching for the remaining part
            double fillW = Math.Max(0, Math.Min(BarW, BarW * rem / 100));
            if (fillW > 0.5)
            {
                var hatch = new GeometryGroup();
                for (double x = -BarH; x < fillW; x += 3.2)
                {
                    double x0 = Math.Max(0, x), y0 = BarH - (x0 - x);
                    double x1 = Math.Min(fillW, x + BarH), y1 = BarH - (x1 - x);
                    if (x1 > x0) hatch.Children.Add(new LineGeometry(new Point(x0 + j() * 0.3, y0), new Point(x1 + j() * 0.3, y1)));
                }
                ln.Bar.Children.Add(new Path { Data = hatch, Stroke = G.B(Pencil(rem), 0.85), StrokeThickness = 1.3, Clip = new RectangleGeometry(new Rect(0, 0, fillW, BarH)) });
                ln.Bar.Children.Add(new Path { Data = new LineGeometry(new Point(fillW, 1 + j()), new Point(fillW + j() * 0.5, BarH - 1)), Stroke = G.B(Pencil(rem)), StrokeThickness = 1.4 });
            }
            // sketchy outline, drawn twice for a hand-made feel
            for (int pass = 0; pass < 2; pass++)
            {
                var pts = new[] { new Point(j(), j()), new Point(BarW + j(), j()), new Point(BarW + j(), BarH + j()), new Point(j(), BarH + j()) };
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(pts[0], false, false);
                    ctx.LineTo(pts[1], true, true);
                    ctx.LineTo(pts[2], true, true);
                    ctx.LineTo(pts[3], true, true);
                    ctx.LineTo(new Point(pts[0].X + 1.5, pts[0].Y - 0.8), true, true);
                }
                g.Freeze();
                ln.Bar.Children.Add(new Path { Data = g, Stroke = G.B(InkBlue, pass == 0 ? 0.9 : 0.35), StrokeThickness = pass == 0 ? 1.3 : 0.9, StrokeLineJoin = PenLineJoin.Round });
            }
        }

        protected override void Refresh()
        {
            var now = DateTime.Now;
            date.Text = now.Month + "/" + now.Day + " (" + "日一二三四五六"[(int)now.DayOfWeek] + ")";
            foreach (var it in items)
            {
                var v = View(it.Id);
                if (v == null) continue;
                string kind = !v.HasData ? "?" : v.Active ? "…" : v.Mood == SentriPet.Mood.Great ? "★" : v.Mood == SentriPet.Mood.Good ? "☺" :
                              v.Mood == SentriPet.Mood.Worried ? "!" : v.Mood == SentriPet.Mood.Critical ? "!!" : "zzz";
                if (kind != it.DoodleKind)
                {
                    it.DoodleKind = kind;
                    it.Doodle.Text = kind;
                    it.Doodle.Foreground = G.B(kind == "★" ? Palette.Hex("#E0A100") : kind == "☺" ? Palette.Hex("#2E8B57") : kind == "zzz" || kind == "…" || kind == "?" ? InkSoft : Palette.Hex("#C0392B"));
                }
                if (it.Error != null) it.Error.Text = v.Error ?? "沒有資料";
                foreach (var ln in it.Lines)
                {
                    var m = v.Meters.FirstOrDefault(x => x.Key == ln.Key);
                    if (m == null) continue;
                    double rem = m.Unlimited ? 100 : Math.Round(m.Remaining);
                    if (Math.Abs(rem - ln.Drawn) >= 0.5) { ln.Drawn = rem; DrawBar(ln, rem); }
                    ln.Pct.Text = m.Unlimited ? "無限" : "剩 " + Fmt.Pct(m.Remaining);
                    ln.Pct.Foreground = G.B(m.Unlimited ? InkBlue : Pencil(m.Remaining));
                }
                if (it.Reset != null)
                {
                    var p = v.Primary;
                    string txt = v.Unlimited ? "沒有額度限制" : p != null && p.ResetsAt.HasValue ? "↻ " + G.ResetText(p) + "後重置" : "閒置中";
                    if (v.Stale) txt += "（資料舊了）";
                    it.Reset.Text = txt;
                }
            }
        }
    }
}
