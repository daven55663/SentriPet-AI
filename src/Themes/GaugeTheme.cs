using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentriPet
{
    /// <summary>Seven-segment LCD read-out (digits, ':', d, h, -, space).</summary>
    class SevenSeg : StackPanel
    {
        static readonly Dictionary<char, string> Map = new Dictionary<char, string>
        {
            { '0', "abcdef" }, { '1', "bc" }, { '2', "abged" }, { '3', "abgcd" }, { '4', "fgbc" },
            { '5', "afgcd" }, { '6', "afgedc" }, { '7', "abc" }, { '8', "abcdefg" }, { '9', "abcdfg" },
            { '-', "g" }, { 'd', "bcdeg" }, { 'h', "cefg" }, { ' ', "" }, { 'E', "adefg" }, { 'r', "eg" },
        };
        static readonly string Segs = "abcdefg";
        static readonly Rect[] Boxes =
        {
            new Rect(2, 0, 7, 2.4), new Rect(8.6, 1.6, 2.4, 7.6), new Rect(8.6, 10.8, 2.4, 7.6), new Rect(2, 17.6, 7, 2.4),
            new Rect(0, 10.8, 2.4, 7.6), new Rect(0, 1.6, 2.4, 7.6), new Rect(2, 8.8, 7, 2.4),
        };
        string text = "";
        public Color On = Palette.Hex("#7CFFCF");

        public SevenSeg() { Orientation = Orientation.Horizontal; }

        public void SetText(string t)
        {
            if (t == text) return;
            text = t;
            Children.Clear();
            var on = G.B(On);
            var off = G.B(On, 0.08);
            foreach (char ch in t)
            {
                if (ch == ':' || ch == '.')
                {
                    var cc = new Canvas { Width = 5, Height = 20 };
                    var d1 = new Rectangle { Width = 2.4, Height = 2.4, Fill = on, RadiusX = 0.6, RadiusY = 0.6 };
                    G.Place(d1, 1.3, ch == ':' ? 5.5 : 17.6);
                    cc.Children.Add(d1);
                    if (ch == ':')
                    {
                        var d2 = new Rectangle { Width = 2.4, Height = 2.4, Fill = on, RadiusX = 0.6, RadiusY = 0.6 };
                        G.Place(d2, 1.3, 12.5);
                        cc.Children.Add(d2);
                    }
                    Children.Add(cc);
                    continue;
                }
                string lit;
                if (!Map.TryGetValue(ch, out lit)) lit = "g";
                var c = new Canvas { Width = 11, Height = 20, Margin = new Thickness(0.8, 0, 0.8, 0), RenderTransform = new SkewTransform(-6, 0, 5.5, 10) };
                for (int i = 0; i < 7; i++)
                {
                    var b = Boxes[i];
                    var r = new Rectangle { Width = b.Width, Height = b.Height, RadiusX = 1, RadiusY = 1, Fill = lit.IndexOf(Segs[i]) >= 0 ? on : off };
                    G.Place(r, b.X, b.Y);
                    c.Children.Add(r);
                }
                Children.Add(c);
            }
        }
    }

    /// <summary>Car instrument cluster: a fuel gauge per AI, warning lamps and an LCD countdown.</summary>
    class GaugeTheme : Theme
    {
        const double S = 150;
        static readonly Point C = new Point(75, 75);

        class Gauge
        {
            public string Id;
            public RotateTransform Needle;
            public Spring Spring = new Spring(0);
            public Path ValueArc;
            public TextBlock Pct, Label;
            public SevenSeg Lcd;
            public TextBlock Approx;
            public Path Fuel, Beam, Warn;
            public Rectangle[] Leds;
            public TextBlock SubLabel, SubPct;
            public string SubKey;
            public double Sweep, TargetValue;
            public Color Accent;
            public int UrgentLeds, UrgentArc, Lit;     // "use it before it resets": what blinks, at which level
        }

        Border root;
        StackPanel row;
        readonly List<Gauge> gauges = new List<Gauge>();

        public override string Id { get { return "gauge"; } }
        public override string Name { get { return "賽車儀表"; } }
        public override string Mood { get { return "全速前進"; } }
        public override string Blurb { get { return "油表指針＋警示燈，AI 工作時遠光燈會亮"; } }

        protected override FrameworkElement CreateRoot()
        {
            root = new Border
            {
                CornerRadius = new CornerRadius(26),
                Margin = new Thickness(14),
                Padding = new Thickness(12, 10, 12, 12),
                Background = G.Vertical(Palette.Hex("#FF20242C"), Palette.Hex("#FF0C0D11")),
                BorderBrush = G.Vertical(Palette.Hex("#FF6B727E"), Palette.Hex("#FF1C1F25")),
                BorderThickness = new Thickness(2),
                Effect = G.Shadow(26, 6, 0.5, Colors.Black),
            };
            row = new StackPanel { Orientation = Orientation.Horizontal };
            root.Child = row;
            return root;
        }

        protected override void Rebuild()
        {
            row.Children.Clear();
            gauges.Clear();
            if (Views.Count == 0)
            {
                row.Children.Add(G.T("引擎發動中… 正在偵測 AI", 12, Palette.Hex("#9AA3AF")));
                return;
            }
            foreach (var v in Views) row.Children.Add(MakeGauge(v));
        }

        FrameworkElement MakeGauge(ProviderView v)
        {
            var g = new Gauge { Id = v.Id, Accent = G.Vivid(v.Color) };
            var panel = new StackPanel { Width = S + 6, Tag = "pv:" + v.Id, Margin = new Thickness(3, 0, 3, 0) };
            var cv = new Canvas { Width = S, Height = S, HorizontalAlignment = HorizontalAlignment.Center };

            var metal = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            metal.GradientStops.Add(new GradientStop(Palette.Hex("#E3E7EC"), 0));
            metal.GradientStops.Add(new GradientStop(Palette.Hex("#4A505A"), 0.45));
            metal.GradientStops.Add(new GradientStop(Palette.Hex("#B8BFC9"), 0.75));
            metal.GradientStops.Add(new GradientStop(Palette.Hex("#30343B"), 1));
            cv.Children.Add(G.Circle(C.X, C.Y, 68, null, metal, 5));
            var face = new RadialGradientBrush(Palette.Hex("#2A2F39"), Palette.Hex("#0A0B0E")) { GradientOrigin = new Point(0.5, 0.35) };
            cv.Children.Add(G.Circle(C.X, C.Y, 65, face, null, 0));

            // red zone + ticks
            cv.Children.Add(new Path { Data = G.Arc(C, 57, -135, -135 + 270 * 0.15), Stroke = G.B(Palette.Hex("#FF3B30"), 0.6), StrokeThickness = 6 });
            var major = new GeometryGroup();
            var minor = new GeometryGroup();
            for (int i = 0; i <= 50; i++)
            {
                double a = -135 + 270.0 * i / 50;
                bool isMajor = i % 5 == 0;
                var p1 = G.Polar(C, isMajor ? 50 : 54, a);
                var p2 = G.Polar(C, 60, a);
                (isMajor ? major : minor).Children.Add(new LineGeometry(p1, p2));
            }
            cv.Children.Add(new Path { Data = minor, Stroke = G.B(Colors.White, 0.35), StrokeThickness = 1 });
            cv.Children.Add(new Path { Data = major, Stroke = G.B(Colors.White, 0.9), StrokeThickness = 2.2 });
            AddLabel(cv, "E", G.Polar(C, 40, -128), Palette.Hex("#FF6B5E"));
            AddLabel(cv, "½", G.Polar(C, 40, 0), Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
            AddLabel(cv, "F", G.Polar(C, 40, 128), Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));

            g.ValueArc = new Path { Stroke = G.B(g.Accent), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Effect = G.Glow(g.Accent, 10, 0.9) };
            cv.Children.Add(g.ValueArc);

            var name = new TextBlock { Text = v.Name.ToUpperInvariant(), Width = 110, TextAlignment = TextAlignment.Center, FontFamily = G.Din, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = G.B(g.Accent) };
            G.Place(name, C.X - 55, 44);
            cv.Children.Add(name);

            var pctRow = new StackPanel { Orientation = Orientation.Horizontal, Width = 110 };
            g.Pct = new TextBlock { FontFamily = G.Din, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = G.B(Colors.White), Width = 110, TextAlignment = TextAlignment.Center };
            G.Place(g.Pct, C.X - 55, 84);
            cv.Children.Add(g.Pct);

            // lamps
            g.Fuel = G.P("M 1,12 L 1,2 Q 1,1 2,1 L 7,1 Q 8,1 8,2 L 8,12 Z M 2.5,3 L 6.5,3 L 6.5,6 L 2.5,6 Z M 8,4 L 10.5,6 L 10.5,10.5 Q 10.5,12 12,11 L 12,5", null, null, 1.2);
            g.Beam = G.P("M 8,2 Q 14,2 14,6.5 Q 14,11 8,11 Z M 0.5,3 L 6,3 M 0.5,5.3 L 6,5.3 M 0.5,7.7 L 6,7.7 M 0.5,10 L 6,10", null, null, 1.3);
            g.Warn = G.P("M 6.5,1 L 12.5,12 L 0.5,12 Z M 6.5,5 L 6.5,8.5 M 6.5,10.2 L 6.5,10.4", null, null, 1.3);
            G.Place(g.Fuel, C.X - 30, 114);
            G.Place(g.Beam, C.X - 7, 114);
            G.Place(g.Warn, C.X + 17, 114);
            cv.Children.Add(g.Fuel);
            cv.Children.Add(g.Beam);
            cv.Children.Add(g.Warn);

            // needle + cap
            var needle = new Path
            {
                Data = Geometry.Parse("M 72.2,86 L 74,19 L 76,19 L 77.8,86 Z"),
                Fill = G.Vertical(Palette.Hex("#FF8A55"), Palette.Hex("#FF2D2D")),
                Effect = G.Glow(Palette.Hex("#FF4D2E"), 10, 0.8),
            };
            g.Needle = new RotateTransform(-135, C.X, C.Y);
            needle.RenderTransform = g.Needle;
            cv.Children.Add(needle);
            cv.Children.Add(G.Circle(C.X, C.Y, 8, new RadialGradientBrush(Palette.Hex("#6B7280"), Palette.Hex("#15171B")) { GradientOrigin = new Point(0.35, 0.3) }, G.B(Palette.Hex("#050506")), 1));
            panel.Children.Add(cv);

            // LCD countdown
            g.Label = G.T("", 9.5, Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF), FontWeights.SemiBold, G.Ui);
            g.Label.HorizontalAlignment = HorizontalAlignment.Center;
            g.Label.Margin = new Thickness(0, 2, 0, 3);
            panel.Children.Add(g.Label);
            var lcdRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            g.Approx = G.T("≈", 12, Palette.Hex("#7CFFCF"), FontWeights.Bold, G.Din);
            g.Approx.Margin = new Thickness(0, 0, 3, 0);
            g.Approx.VerticalAlignment = VerticalAlignment.Center;
            g.Lcd = new SevenSeg { Effect = G.Glow(Palette.Hex("#7CFFCF"), 8, 0.7) };
            lcdRow.Children.Add(g.Approx);
            lcdRow.Children.Add(g.Lcd);
            panel.Children.Add(new Border
            {
                Child = lcdRow,
                Background = G.Vertical(Palette.Hex("#07140F"), Palette.Hex("#0B1E17")),
                BorderBrush = G.B(Palette.Hex("#23443A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            // secondary meter as an LED bar
            var sub = v.Secondary;
            if (v.HasData && sub != null)
            {
                g.SubKey = sub.Key;
                var sr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 0) };
                g.SubLabel = G.T(sub.ShortLabel ?? "", 9.5, Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF), FontWeights.SemiBold, G.Ui);
                g.SubLabel.Margin = new Thickness(0, 0, 5, 0);
                g.SubLabel.VerticalAlignment = VerticalAlignment.Center;
                sr.Children.Add(g.SubLabel);
                g.Leds = new Rectangle[10];
                for (int i = 0; i < 10; i++)
                {
                    g.Leds[i] = new Rectangle { Width = 6, Height = 9, RadiusX = 1.2, RadiusY = 1.2, Margin = new Thickness(1, 0, 1, 0) };
                    sr.Children.Add(g.Leds[i]);
                }
                g.SubPct = G.T("", 10, Colors.White, FontWeights.SemiBold, G.Din);
                g.SubPct.Margin = new Thickness(5, 0, 0, 0);
                g.SubPct.VerticalAlignment = VerticalAlignment.Center;
                sr.Children.Add(g.SubPct);
                panel.Children.Add(sr);
            }
            g.Sweep = 0;
            g.Spring.X = 0;
            gauges.Add(g);
            return panel;
        }

        static void AddLabel(Canvas cv, string text, Point at, Color color)
        {
            var t = new TextBlock { Text = text, Width = 20, TextAlignment = TextAlignment.Center, FontFamily = G.Din, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = G.B(color) };
            G.Place(t, at.X - 10, at.Y - 8);
            cv.Children.Add(t);
        }

        static void Lamp(Path p, bool on, Color color)
        {
            p.Stroke = G.B(on ? color : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            p.Effect = on ? G.Glow(color, 8, 0.9) : null;
        }

        protected override void Refresh()
        {
            foreach (var g in gauges)
            {
                var v = View(g.Id);
                if (v == null) continue;
                // needle and number: the headline (5-hour) window; the LCD counts down to the reset that matters most
                var p = v.ResetMeter;
                double head = v.HeadlineRemaining;
                g.TargetValue = v.HasData ? (v.Unlimited ? 100 : head) : 0;
                g.Pct.Text = !v.HasData ? "--" : v.Unlimited ? "∞" : Math.Round(head) + "%";
                g.Pct.Foreground = G.B(!v.HasData ? Palette.Hex("#6B7280") : head < 20 ? Palette.Hex("#FF6B5E") : Colors.White);
                // a window about to expire unused blinks (its LED bar, or the dial when it is the headline one)
                int urgent = v.HasData && v.UseIt != null ? v.UseItLevel : 0;
                g.UrgentLeds = urgent > 0 && v.UseIt.Key == g.SubKey ? urgent : 0;
                g.UrgentArc = urgent > 0 && v.UseIt == v.Headline ? urgent : 0;
                if (!v.HasData)
                {
                    g.Label.Text = v.Error ?? "沒有資料";
                    g.Label.MaxWidth = 140;
                    g.Label.TextTrimming = TextTrimming.CharacterEllipsis;
                    g.Lcd.SetText("--:--:--");
                    g.Approx.Visibility = Visibility.Collapsed;
                }
                else if (v.Unlimited || p == null || !p.ResetsAt.HasValue)
                {
                    g.Label.Text = v.Unlimited ? "無限制" : (p != null ? p.Label + " · 閒置中" : "");
                    g.Lcd.SetText("--:--:--");
                    g.Approx.Visibility = Visibility.Collapsed;
                }
                else
                {
                    g.Label.Text = p.Label + " · 重置倒數";
                    g.Lcd.SetText(LcdText(p.ResetsAt.Value));
                    g.Approx.Visibility = p.ResetApprox ? Visibility.Visible : Visibility.Collapsed;
                }
                Lamp(g.Fuel, v.HasData && !v.Unlimited && v.Remaining < 20, Palette.Hex("#FFB020"));
                Lamp(g.Beam, v.Active, Palette.Hex("#3B9BFF"));
                Lamp(g.Warn, !v.HasData || v.Stale, Palette.Hex("#FF4D4D"));
                if (g.Leds != null)
                {
                    var sub = v.Meters.FirstOrDefault(m => m.Key == g.SubKey);
                    double rem = sub == null ? 0 : sub.Remaining;
                    int lit = (int)Math.Round(rem / 10);
                    var col = g.UrgentLeds > 0 ? G.UseItAccent(g.UrgentLeds) : rem >= 50 ? Palette.Hex("#34D399") : rem >= 20 ? Palette.Hex("#FBBF24") : Palette.Hex("#F87171");
                    for (int i = 0; i < g.Leds.Length; i++)
                    {
                        g.Leds[i].Fill = G.B(i < lit ? col : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                        if (g.UrgentLeds == 0) g.Leds[i].Opacity = 1;
                    }
                    g.Lit = lit;
                    g.SubPct.Text = sub == null ? "" : Fmt.Pct(rem);
                }
                if (g.UrgentArc == 0) g.ValueArc.Opacity = 1;
            }
        }

        static string LcdText(DateTime resetUtc)
        {
            var d = resetUtc - DateTime.UtcNow;
            if (d.TotalSeconds <= 0) return "00:00:00";
            if (d.TotalDays >= 1)
                return ((int)d.TotalDays) + "d " + d.Hours.ToString("00") + ":" + d.Minutes.ToString("00");
            return ((int)d.TotalHours).ToString("00") + ":" + d.Minutes.ToString("00") + ":" + d.Seconds.ToString("00");
        }

        public override void Tick(double dt)
        {
            base.Tick(dt);
            foreach (var g in gauges)
            {
                g.Sweep += dt;
                g.Spring.Target = g.Sweep < 0.7 ? 100 : g.TargetValue;   // start-up sweep like a real cluster
                g.Spring.Step(dt, 55, 7.5);
                double val = Math.Max(-2, Math.Min(102, g.Spring.X));
                var v = View(g.Id);
                if (v != null && v.Active) val += Math.Sin(Time * 30) * 0.4;   // engine vibration
                g.Needle.Angle = -135 + 270 * val / 100;
                double shown = Math.Max(0, Math.Min(100, val));
                g.ValueArc.Data = shown < 0.3 ? Geometry.Empty : G.Arc(C, 45, -135, -135 + 270 * shown / 100);
                if (g.UrgentLeds > 0 && g.Leds != null)
                {
                    double o = G.UrgentPulse(g.UrgentLeds, Time);
                    for (int i = 0; i < g.Leds.Length && i < g.Lit; i++) g.Leds[i].Opacity = o;
                }
                if (g.UrgentArc > 0) g.ValueArc.Opacity = G.UrgentPulse(g.UrgentArc, Time);
            }
        }
    }
}
