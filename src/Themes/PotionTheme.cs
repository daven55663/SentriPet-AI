using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SentriPet
{
    /// <summary>Potion shelf: one bubbling flask per quota window, standing on a wooden shelf.</summary>
    class PotionTheme : Theme
    {
        const double FlaskW = 50, FlaskH = 86, CellW = 58;

        class FlaskShape
        {
            public Geometry Glass;
            public double FillTop, FillBottom, Left, Right;
            public Geometry Shine;
        }

        static readonly FlaskShape Round = Shape("M 19,6 L 19,36.76 A 24,24 0 1 0 31,36.76 L 31,6 Z", 38, 84, 3, 47, "M 8,52 A 17,17 0 0 1 16,40");
        static readonly FlaskShape Bottle = Shape("M 20,6 L 20,16 C 20,22 8,22 8,30 L 8,79 Q 8,85 14,85 L 36,85 Q 42,85 42,79 L 42,30 C 42,22 30,22 30,16 L 30,6 Z", 30, 85, 8, 42, "M 13,36 L 13,74");
        static readonly FlaskShape Cone = Shape("M 20,6 L 20,32 L 5,76 Q 2.5,85 12,85 L 38,85 Q 47.5,85 45,76 L 30,32 L 30,6 Z", 34, 85, 4, 46, "M 18,42 L 10,70");
        static readonly FlaskShape Vial = Shape("M 17,6 L 17,74 A 8,8 0 0 0 33,74 L 33,6 Z", 12, 82, 17, 33, "M 21,16 L 21,70");

        static FlaskShape Shape(string glass, double top, double bottom, double left, double right, string shine)
        {
            var g = Geometry.Parse(glass);
            g.Freeze();
            var s = Geometry.Parse(shine);
            s.Freeze();
            return new FlaskShape { Glass = g, FillTop = top, FillBottom = bottom, Left = left, Right = right, Shine = s };
        }

        static FlaskShape ShapeFor(Meter m)
        {
            if (m.WindowMinutes > 0 && m.WindowMinutes < 1440) return Round;
            if (m.WindowMinutes == 10080) return Bottle;
            if (m.WindowMinutes >= 40000) return Cone;
            return Vial;
        }

        class Flask
        {
            public string ProviderId, Key;
            public FlaskShape Shape;
            public Path Liquid, Surface;
            public DropShadowEffect Glow;
            public Ellipse[] Bubbles;
            public double[] BubbleSeed;
            public Ellipse[] Smoke;
            public TextBlock Pct;
            public Anim Level = new Anim(0);
            public double Phase;
        }

        class Group
        {
            public string Id;
            public TextBlock Name, Reset;
            public List<Flask> Flasks = new List<Flask>();
        }

        Grid root;
        readonly List<Group> groups = new List<Group>();
        readonly List<Flask> flasks = new List<Flask>();

        public override string Id { get { return "potion"; } }
        public override string Name { get { return L.T("魔法藥水"); } }
        public override string Mood { get { return L.T("有點夢幻"); } }
        public override string Blurb { get { return L.T("每個額度一瓶藥水，會冒泡泡"); } }

        protected override FrameworkElement CreateRoot()
        {
            root = new Grid { Margin = new Thickness(12, 16, 12, 12) };
            return root;
        }

        protected override void Rebuild()
        {
            root.Children.Clear();
            root.ColumnDefinitions.Clear();
            root.RowDefinitions.Clear();
            groups.Clear();
            flasks.Clear();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var list = Views.Count > 0 ? Views : new List<ProviderView> { new ProviderView { Id = "_", Name = L.T("偵測中…"), Color = Rgba.Hex("#94A3B8"), Error = L.T("正在尋找 AI") } };
            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                int n = Math.Max(1, Math.Min(3, v.Meters.Count));
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(118, n * CellW + 16)) });
                var grp = new Group { Id = v.Id };
                var shelfRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Tag = "pv:" + v.Id };
                if (v.HasData)
                    foreach (var m in v.Meters.Take(3)) shelfRow.Children.Add(MakeFlask(v, m, grp));
                else
                    shelfRow.Children.Add(MakeFlask(v, null, grp));
                Grid.SetColumn(shelfRow, i);
                root.Children.Add(shelfRow);

                var tag = MakeTag(v, grp);
                Grid.SetColumn(tag, i);
                Grid.SetRow(tag, 2);
                root.Children.Add(tag);
                groups.Add(grp);
            }

            var shelf = new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = G.Vertical(Palette.Hex("#B07A45"), Palette.Hex("#7A4E28"), Palette.Hex("#5E3A1C")),
                BorderBrush = G.B(Palette.Hex("#3E2410")),
                BorderThickness = new Thickness(1),
                Effect = G.Shadow(10, 4, 0.45, Colors.Black),
            };
            var grain = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            for (int k = 0; k < 3; k++)
                grain.Children.Add(new Rectangle { Height = 0.8, Width = 2000, Fill = G.B(Palette.Hex("#4A2C12"), 0.35), Margin = new Thickness(-10, 3 + k * 3.2, 0, 0) });
            shelf.Child = grain;
            Grid.SetRow(shelf, 1);
            Grid.SetColumnSpan(shelf, Math.Max(1, list.Count));
            root.Children.Add(shelf);
        }

        FrameworkElement MakeFlask(ProviderView v, Meter m, Group grp)
        {
            var shape = m != null ? ShapeFor(m) : Vial;
            var f = new Flask { ProviderId = v.Id, Key = m != null ? m.Key : "", Shape = shape, Phase = Rng.NextDouble() * 6 };
            var color = v.HasData ? v.Color.ToWpf() : G.Desaturate(v.Color, 0.7);
            var cell = new Canvas { Width = CellW, Height = FlaskH + 4 };
            var body = new Canvas { Width = FlaskW, Height = FlaskH };
            G.Place(body, (CellW - FlaskW) / 2, 4);
            cell.Children.Add(body);

            // back glass
            body.Children.Add(new Path { Data = shape.Glass, Fill = G.Vertical(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)) });
            // liquid
            var vivid = G.Vivid(color);
            f.Glow = new DropShadowEffect { Color = vivid, BlurRadius = 16, ShadowDepth = 0, Opacity = 0 };
            f.Liquid = new Path { Fill = G.Vertical(Palette.Lighten(vivid, 0.35), vivid, Palette.Darken(color, 0.3)), Clip = shape.Glass, Effect = f.Glow };
            f.Surface = new Path { Stroke = G.B(Palette.Lighten(vivid, 0.6), 0.9), StrokeThickness = 1.4, Clip = shape.Glass };
            body.Children.Add(f.Liquid);
            body.Children.Add(f.Surface);
            f.Bubbles = new Ellipse[5];
            f.BubbleSeed = new double[5];
            var bubbleLayer = new Canvas { Clip = shape.Glass };
            for (int i = 0; i < f.Bubbles.Length; i++)
            {
                double r = 1.2 + Rng.NextDouble() * 1.6;
                f.Bubbles[i] = new Ellipse { Width = r * 2, Height = r * 2, Fill = G.B(Colors.White, 0.55), Stroke = G.B(Colors.White, 0.8), StrokeThickness = 0.5 };
                f.BubbleSeed[i] = Rng.NextDouble();
                bubbleLayer.Children.Add(f.Bubbles[i]);
            }
            body.Children.Add(bubbleLayer);
            // glass outline + shine
            body.Children.Add(new Path { Data = shape.Glass, Stroke = G.B(Colors.White, 0.85), StrokeThickness = 2 });
            body.Children.Add(new Path { Data = shape.Shine, Stroke = G.B(Colors.White, 0.55), StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            // cork
            var cork = new Border
            {
                Width = 16, Height = 10,
                CornerRadius = new CornerRadius(3, 3, 2, 2),
                Background = G.Vertical(Palette.Hex("#D8AE7E"), Palette.Hex("#94643C")),
                BorderBrush = G.B(Palette.Hex("#5C3A1C")),
                BorderThickness = new Thickness(1),
            };
            G.Place(cork, 17, 0);
            body.Children.Add(cork);
            // smoke (empty flask)
            f.Smoke = new Ellipse[3];
            for (int i = 0; i < 3; i++)
            {
                f.Smoke[i] = new Ellipse { Width = 7, Height = 7, Fill = G.B(Palette.Hex("#CBD5E1"), 0.6), Visibility = Visibility.Collapsed };
                body.Children.Add(f.Smoke[i]);
            }
            // label
            f.Pct = new TextBlock { FontFamily = new FontFamily("Georgia"), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = G.B(Palette.Hex("#4A2E12")), HorizontalAlignment = HorizontalAlignment.Center };
            var lblStack = new StackPanel();
            lblStack.Children.Add(f.Pct);
            var sl = G.T(m != null ? (m.ShortLabel ?? "") : "?", 8.5, Palette.Hex("#7A5530"), FontWeights.SemiBold, G.Ui);
            sl.HorizontalAlignment = HorizontalAlignment.Center;
            lblStack.Children.Add(sl);
            var label = new Border
            {
                Child = lblStack,
                Width = 34,
                Background = G.Vertical(Palette.Hex("#FBF1DA"), Palette.Hex("#EAD5A8")),
                BorderBrush = G.B(Palette.Hex("#A57E47")),
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(0, 1, 0, 1),
                RenderTransform = new RotateTransform(-4),
            };
            G.Place(label, 8, shape.FillBottom - 36);
            body.Children.Add(label);

            f.Level.Value = f.Level.Target = m != null ? m.Remaining : 0;
            grp.Flasks.Add(f);
            flasks.Add(f);
            return cell;
        }

        FrameworkElement MakeTag(ProviderView v, Group grp)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Tag = "pv:" + v.Id };
            sp.Children.Add(new Rectangle { Width = 1.4, Height = 8, Fill = G.B(Palette.Hex("#6B4A2A")), HorizontalAlignment = HorizontalAlignment.Center });
            var inner = new StackPanel();
            grp.Name = G.T(v.Name, 12, Palette.Hex("#3E2410"), FontWeights.Bold, G.Ui);
            grp.Name.HorizontalAlignment = HorizontalAlignment.Center;
            grp.Reset = G.T("", 9.5, Palette.Hex("#6B4A2A"), FontWeights.Normal, G.Ui);
            grp.Reset.HorizontalAlignment = HorizontalAlignment.Center;
            grp.Reset.TextWrapping = TextWrapping.Wrap;
            grp.Reset.TextAlignment = TextAlignment.Center;
            grp.Reset.MaxWidth = 150;
            inner.Children.Add(grp.Name);
            inner.Children.Add(grp.Reset);
            sp.Children.Add(new Border
            {
                Child = inner,
                Background = G.Vertical(Palette.Hex("#FBF1DA"), Palette.Hex("#E9D2A0")),
                BorderBrush = G.B(Palette.Hex("#A57E47")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 4),
                Effect = G.Shadow(6, 2, 0.35, Colors.Black),
                RenderTransform = new RotateTransform(Rng.NextDouble() * 4 - 2),
                RenderTransformOrigin = new Point(0.5, 0),
            });
            return sp;
        }

        protected override void Refresh()
        {
            foreach (var grp in groups)
            {
                var v = View(grp.Id);
                if (v == null) continue;
                grp.Name.Text = v.Name;
                if (!v.HasData) { grp.Reset.Text = v.Error ?? L.T("沒有資料"); }
                else
                {
                    var parts = v.Meters.Take(3).Select(m => m.ShortLabel + " " + (m.Unlimited ? "∞" : m.ResetsAt.HasValue ? "↻" + G.ResetText(m) : L.T("閒置")));
                    grp.Reset.Text = string.Join("\n", parts) + (v.Stale ? "\n" + L.T("（資料較舊）") : "");
                }
                foreach (var f in grp.Flasks)
                {
                    var m = v.Meters.FirstOrDefault(x => x.Key == f.Key);
                    f.Level.Target = m == null ? 0 : m.Remaining;
                    f.Pct.Text = m == null ? "?" : m.Unlimited ? "∞" : Fmt.Pct(m.Remaining);
                    f.Glow.Opacity = v.Active ? 0.9 : 0;
                }
            }
        }

        public override void Tick(double dt)
        {
            base.Tick(dt);
            foreach (var f in flasks)
            {
                f.Level.Step(dt, 2.2);
                var s = f.Shape;
                double level = Math.Max(0, Math.Min(100, f.Level.Value));
                double y0 = s.FillBottom - level / 100 * (s.FillBottom - s.FillTop);
                var v = View(f.ProviderId);
                bool active = v != null && v.Active;
                double amp = level < 0.5 ? 0 : (active ? 2.2 : 1.2);
                var fill = new StreamGeometry();
                var surf = new StreamGeometry();
                using (var fc = fill.Open())
                using (var sc = surf.Open())
                {
                    fc.BeginFigure(new Point(0, FlaskH + 2), true, true);
                    for (int i = 0; i <= 16; i++)
                    {
                        double x = FlaskW * i / 16.0;
                        double y = y0 + amp * Math.Sin(x / 6.5 + Time * 3 + f.Phase);
                        fc.LineTo(new Point(x, y), true, true);
                        if (i == 0) sc.BeginFigure(new Point(x, y), false, false); else sc.LineTo(new Point(x, y), true, true);
                    }
                    fc.LineTo(new Point(FlaskW, FlaskH + 2), true, true);
                }
                fill.Freeze();
                surf.Freeze();
                f.Liquid.Data = fill;
                f.Surface.Data = surf;
                if (active) f.Glow.BlurRadius = 14 + 6 * Math.Sin(Time * 5 + f.Phase);

                // bubbles rise from the bottom to the surface
                double speed = active ? 0.55 : 0.22;
                for (int i = 0; i < f.Bubbles.Length; i++)
                {
                    var b = f.Bubbles[i];
                    if (level < 3) { b.Visibility = Visibility.Collapsed; continue; }
                    b.Visibility = Visibility.Visible;
                    double u = (f.BubbleSeed[i] + Time * speed * (0.7 + f.BubbleSeed[i] * 0.6)) % 1.0;
                    double yb = s.FillBottom - 4 - u * (s.FillBottom - 4 - y0);
                    double xb = s.Left + 6 + ((f.BubbleSeed[i] * 97) % 1.0) * (s.Right - s.Left - 12) + Math.Sin(Time * 3 + i) * 1.5;
                    G.Place(b, xb, yb);
                    b.Opacity = u > 0.85 ? (1 - u) * 6.6 : 1;
                }
                bool empty = level < 1;
                for (int i = 0; i < f.Smoke.Length; i++)
                {
                    var sm = f.Smoke[i];
                    sm.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                    if (!empty) continue;
                    double u = (Time * 0.35 + i / 3.0) % 1.0;
                    G.Place(sm, 21 + Math.Sin(u * 7 + i) * 5, -2 - u * 22);
                    sm.Width = sm.Height = 5 + u * 8;
                    sm.Opacity = (1 - u) * 0.7;
                }
                // a potion that expires unused soon fades in and out
                int urgent = v != null && v.UseIt != null && v.UseIt.Key == f.Key ? v.UseItLevel : 0;
                if (urgent > 0) f.Liquid.Opacity = G.UrgentPulse(urgent, Time);
                else if (v != null && v.Mood == SentriPet.Mood.Critical) f.Liquid.Opacity = 0.65 + 0.35 * Math.Abs(Math.Sin(Time * 4));
                else f.Liquid.Opacity = 1;
            }
        }
    }
}
