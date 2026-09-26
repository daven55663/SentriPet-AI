using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentriPet
{
    /// <summary>Frosted dark glass card with progress rings — calm and tidy.</summary>
    class GlassTheme : Theme
    {
        const double RingSize = 58, RingR = 23, RingT = 5.5;

        class Ring
        {
            public string Key;
            public Path Arc;
            public TextBlock Value, Unit, Reset;
            public Anim A = new Anim(0);
            public double Drawn = -1;
            public Brush NormalStroke;
            public Color NormalGlow;
            public int Urgent;                 // "use it before it resets" level: the ring blinks
        }

        class Row
        {
            public string Id;
            public Ellipse Dot;
            public TextBlock Status;
            public List<Ring> Rings = new List<Ring>();
            public TextBlock Error;
        }

        Border card;
        StackPanel list;
        TextBlock clock;
        Ellipse liveDot;
        readonly List<Row> rows = new List<Row>();

        public override string Id { get { return "glass"; } }
        public override string Name { get { return L.T("極簡玻璃"); } }
        public override string Mood { get { return L.T("平靜專注"); } }
        public override string Blurb { get { return L.T("毛玻璃卡片＋圓環，乾淨俐落"); } }

        protected override FrameworkElement CreateRoot()
        {
            card = new Border
            {
                CornerRadius = new CornerRadius(20),
                Margin = new Thickness(16),
                Background = G.Vertical(Color.FromArgb(0xEC, 0x2A, 0x2F, 0x3D), Color.FromArgb(0xF2, 0x14, 0x17, 0x20)),
                BorderBrush = G.Vertical(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1.2),
                Effect = G.Shadow(28, 6, 0.42, Colors.Black),
                MinWidth = 230,
            };
            var layers = new Grid();
            // soft sheen on the upper half
            layers.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(19, 19, 60, 60),
                Background = G.Vertical(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Top,
                Height = 70,
                IsHitTestVisible = false,
            });
            var content = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(G.T(L.T("AI 用量"), 12.5, Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), FontWeights.Bold, G.Ui));
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            liveDot = new Ellipse { Width = 7, Height = 7, Fill = G.B(Palette.Hex("#4ADE80")), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            clock = G.T("", 11.5, Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF), FontWeights.SemiBold, G.Num);
            right.Children.Add(liveDot);
            right.Children.Add(clock);
            Grid.SetColumn(right, 1);
            header.Children.Add(right);
            content.Children.Add(header);
            list = new StackPanel();
            content.Children.Add(list);
            layers.Children.Add(content);
            card.Child = layers;
            return card;
        }

        protected override void Rebuild()
        {
            list.Children.Clear();
            rows.Clear();
            if (Views.Count == 0)
            {
                list.Children.Add(G.T(L.T("正在偵測電腦上的 AI…"), 11.5, Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));
                return;
            }
            for (int i = 0; i < Views.Count; i++)
            {
                if (i > 0) list.Children.Add(new Border { Height = 1, Background = G.B(Colors.White, 0.08), Margin = new Thickness(0, 10, 0, 10) });
                list.Children.Add(MakeRow(Views[i]));
            }
        }

        FrameworkElement MakeRow(ProviderView v)
        {
            var r = new Row { Id = v.Id };
            var grid = new Grid { Tag = "pv:" + v.Id, Background = G.B(Colors.White, 0.001) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            r.Dot = new Ellipse { Width = 9, Height = 9, Fill = G.B(G.Vivid(v.Color)), Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center, Effect = G.Glow(G.Vivid(v.Color), 8, 0.9) };
            nameRow.Children.Add(r.Dot);
            nameRow.Children.Add(G.T(v.Name, 14, Colors.White, FontWeights.Bold, G.Ui));
            info.Children.Add(nameRow);
            if (!string.IsNullOrEmpty(v.Plan))
            {
                var chip = G.Pill(G.T(v.Plan, 9.5, Palette.Lighten(G.Vivid(v.Color), 0.3), FontWeights.SemiBold, G.Ui), G.B(G.Vivid(v.Color), 0.16), 7, new Thickness(7, 1, 7, 2));
                chip.HorizontalAlignment = HorizontalAlignment.Left;
                chip.Margin = new Thickness(15, 3, 0, 0);
                info.Children.Add(chip);
            }
            r.Status = G.T("", 9.5, Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
            r.Status.TextWrapping = TextWrapping.Wrap;
            r.Status.Margin = new Thickness(15, 4, 0, 0);
            info.Children.Add(r.Status);
            grid.Children.Add(info);

            var rings = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(rings, 1);
            if (v.HasData)
            {
                foreach (var m in v.Meters.Take(3))
                {
                    var ring = new Ring { Key = m.Key };
                    var cell = new StackPanel { Width = 70, Margin = new Thickness(2, 0, 2, 0) };
                    var canvas = new Grid { Width = RingSize, Height = RingSize, HorizontalAlignment = HorizontalAlignment.Center };
                    canvas.Children.Add(new Ellipse { Width = RingR * 2 + RingT, Height = RingR * 2 + RingT, Stroke = G.B(Colors.White, 0.09), StrokeThickness = RingT, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    var c1 = Palette.Lighten(G.Vivid(v.Color), 0.35);
                    var c2 = G.Vivid(v.Color);
                    ring.Arc = new Path
                    {
                        Stroke = G.Lg(c1, c2, 90),
                        StrokeThickness = RingT,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Effect = G.Glow(c2, 10, 0.55),
                    };
                    ring.NormalStroke = ring.Arc.Stroke;
                    ring.NormalGlow = c2;
                    var arcHost = new Canvas { Width = RingSize, Height = RingSize };
                    arcHost.Children.Add(ring.Arc);
                    canvas.Children.Add(arcHost);
                    var val = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    ring.Value = G.T("", 16, Colors.White, FontWeights.SemiBold, G.Num);
                    ring.Unit = G.T("%", 9, Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF), FontWeights.SemiBold, G.Num);
                    ring.Unit.VerticalAlignment = VerticalAlignment.Bottom;
                    ring.Unit.Margin = new Thickness(1, 0, 0, 3);
                    val.Children.Add(ring.Value);
                    val.Children.Add(ring.Unit);
                    canvas.Children.Add(val);
                    cell.Children.Add(canvas);
                    var lbl = G.T(m.Label, 10, Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF), FontWeights.SemiBold, G.Ui);
                    lbl.HorizontalAlignment = HorizontalAlignment.Center;
                    lbl.Margin = new Thickness(0, 3, 0, 0);
                    cell.Children.Add(lbl);
                    ring.Reset = G.T("", 9.5, Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF), FontWeights.Normal, G.Ui);
                    ring.Reset.HorizontalAlignment = HorizontalAlignment.Center;
                    cell.Children.Add(ring.Reset);
                    rings.Children.Add(cell);
                    ring.A.Value = 0;
                    r.Rings.Add(ring);
                }
            }
            else
            {
                r.Error = G.T(v.Error ?? L.T("沒有資料"), 10.5, Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF));
                r.Error.TextWrapping = TextWrapping.Wrap;
                r.Error.MaxWidth = 150;
                r.Error.VerticalAlignment = VerticalAlignment.Center;
                rings.Children.Add(r.Error);
            }
            grid.Children.Add(rings);
            rows.Add(r);
            return grid;
        }

        protected override void Refresh()
        {
            clock.Text = DateTime.Now.ToString("HH:mm");
            foreach (var r in rows)
            {
                var v = View(r.Id);
                if (v == null) continue;
                r.Status.Text = v.HasData ? (v.Stale ? L.T("資料較舊") + " · " + Fmt.Ago(v.Snap.ObservedAt) : v.StatusText.Split('·')[0].Trim()) : "";
                if (r.Error != null) r.Error.Text = v.Error ?? L.T("沒有資料");
                foreach (var ring in r.Rings)
                {
                    var m = v.Meters.FirstOrDefault(x => x.Key == ring.Key);
                    if (m == null) continue;
                    ring.A.Target = m.Unlimited ? 100 : m.Remaining;
                    ring.Value.Text = m.Unlimited ? "∞" : Math.Round(m.Remaining).ToString("0");
                    ring.Unit.Visibility = m.Unlimited ? Visibility.Collapsed : Visibility.Visible;
                    ring.Value.Foreground = G.B(m.Unlimited || m.Remaining >= 20 ? Colors.White : Palette.Hex("#FF8A8A"));
                    ring.Reset.Text = m.Unlimited ? L.T("無限制") : m.ResetsAt.HasValue ? G.ResetText(m) : (m.Used <= 0 ? L.T("閒置中") : "—");
                    // this window resets soon with quota left over: its ring blinks
                    int urgent = m == v.UseIt ? v.UseItLevel : 0;
                    if (urgent != ring.Urgent)
                    {
                        ring.Urgent = urgent;
                        var accent = G.UseItAccent(urgent);
                        ring.Arc.Stroke = urgent > 0 ? G.Lg(Palette.Lighten(accent, 0.35), accent, 90) : ring.NormalStroke;
                        ring.Arc.Effect = G.Glow(urgent > 0 ? accent : ring.NormalGlow, 10, urgent > 0 ? 0.8 : 0.55);
                        ring.Arc.Opacity = 1;
                    }
                }
            }
        }

        public override void Tick(double dt)
        {
            base.Tick(dt);
            bool anyActive = Views.Any(v => v.Active);
            liveDot.Opacity = anyActive ? 0.55 + 0.45 * Math.Sin(Time * 6) : 0.9;
            liveDot.Fill = G.B(anyActive ? Palette.Hex("#60A5FA") : Palette.Hex("#4ADE80"));
            foreach (var r in rows)
            {
                var v = View(r.Id);
                if (v != null && r.Dot != null) r.Dot.Opacity = v.Active ? 0.5 + 0.5 * Math.Abs(Math.Sin(Time * 4)) : 1;
                foreach (var ring in r.Rings)
                {
                    if (ring.Urgent > 0) ring.Arc.Opacity = G.UrgentPulse(ring.Urgent, Time);
                    ring.A.Step(dt, 3.2);
                    if (Math.Abs(ring.A.Value - ring.Drawn) < 0.05) continue;
                    ring.Drawn = ring.A.Value;
                    double sweep = 360 * Math.Max(0, Math.Min(100, ring.A.Value)) / 100;
                    ring.Arc.Data = sweep < 0.5 ? Geometry.Empty : G.Arc(new Point(RingSize / 2, RingSize / 2), RingR, 0, sweep);
                    ring.Arc.Visibility = sweep < 0.5 ? Visibility.Hidden : Visibility.Visible;
                }
            }
        }
    }
}
