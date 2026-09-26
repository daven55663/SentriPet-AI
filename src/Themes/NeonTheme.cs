using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentriPet
{
    /// <summary>Panel with two chamfered corners and HUD brackets, sized to its layout slot.</summary>
    class ChamferShape : FrameworkElement
    {
        public Brush Fill;
        public Pen Stroke;
        public Pen Accent;
        public double Cut = 16;

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, c = Cut;
            if (w < 2 * c || h < 2 * c) return;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c, 0), true, true);
                ctx.LineTo(new Point(w, 0), true, false);
                ctx.LineTo(new Point(w, h - c), true, false);
                ctx.LineTo(new Point(w - c, h), true, false);
                ctx.LineTo(new Point(0, h), true, false);
                ctx.LineTo(new Point(0, c), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(Fill, Stroke, g);
            if (Accent != null)
            {
                dc.DrawLine(Accent, new Point(w - 34, -4), new Point(w + 4, -4));
                dc.DrawLine(Accent, new Point(w + 4, -4), new Point(w + 4, 22));
                dc.DrawLine(Accent, new Point(-4, h + 4), new Point(34, h + 4));
                dc.DrawLine(Accent, new Point(-4, h + 4), new Point(-4, h - 22));
            }
        }
    }

    /// <summary>Cyberpunk HUD: neon tubes, segmented bars, an occasional glitch.</summary>
    class NeonTheme : Theme
    {
        static readonly Color Cyan = Palette.Hex("#22F3FF");
        static readonly Color Magenta = Palette.Hex("#FF2BD6");
        static readonly Color Yellow = Palette.Hex("#FFE84A");
        static readonly Color Red = Palette.Hex("#FF3B6B");
        const int Segs = 20;

        class Bar
        {
            public string Key;
            public Rectangle[] Cells;
            public StackPanel Host;
            public TextBlock Pct, Reset;
            public int Urgent;              // "use it before it resets" level: the tube blinks
        }

        class Row
        {
            public string Id;
            public TextBlock Big, BigR, BigC, Tag;
            public List<Bar> Bars = new List<Bar>();
            public TextBlock Error;
            public double GlitchUntil = -1, NextGlitch;
        }

        Grid root;
        StackPanel list;
        TextBlock header, clock, live;
        Rectangle scanBand;
        TranslateTransform scanMove;
        readonly List<Row> rows = new List<Row>();
        double flickerUntil = -1;

        public override string Id { get { return "neon"; } }
        public override string Name { get { return L.T("霓虹夜城"); } }
        public override string Mood { get { return L.T("深夜模式"); } }
        public override string Blurb { get { return L.T("賽博龐克霓虹燈管，偶爾故障閃爍"); } }

        protected override FrameworkElement CreateRoot()
        {
            root = new Grid { Margin = new Thickness(18), MinWidth = 280 };
            root.Children.Add(new ChamferShape
            {
                Fill = G.Vertical(Color.FromArgb(0xEE, 0x12, 0x06, 0x22), Color.FromArgb(0xF2, 0x07, 0x03, 0x12)),
                Stroke = new Pen(G.B(Cyan), 1.6),
                Accent = new Pen(G.B(Magenta), 2),
                Effect = G.Glow(Cyan, 14, 0.8),
            });
            var clip = new Grid { ClipToBounds = true, IsHitTestVisible = false };
            scanMove = new TranslateTransform();
            scanBand = new Rectangle
            {
                Height = 46,
                VerticalAlignment = VerticalAlignment.Top,
                Fill = G.Vertical(Color.FromArgb(0, 0x22, 0xF3, 0xFF), Color.FromArgb(0x1E, 0x22, 0xF3, 0xFF), Color.FromArgb(0, 0x22, 0xF3, 0xFF)),
                RenderTransform = scanMove,
            };
            clip.Children.Add(scanBand);
            root.Children.Add(clip);

            var content = new StackPanel { Margin = new Thickness(18, 12, 20, 14) };
            var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header = G.T("// AI_QUOTA.SYS", 11.5, Magenta, FontWeights.SemiBold, G.Din);
            header.Effect = G.Glow(Magenta, 10, 0.9);
            head.Children.Add(header);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            live = G.T("● LIVE", 10, Red, FontWeights.SemiBold, G.Din);
            live.Margin = new Thickness(0, 0, 8, 0);
            clock = G.T("", 10.5, Cyan, FontWeights.Normal, G.Din);
            right.Children.Add(live);
            right.Children.Add(clock);
            Grid.SetColumn(right, 1);
            head.Children.Add(right);
            content.Children.Add(head);
            list = new StackPanel();
            content.Children.Add(list);
            root.Children.Add(content);
            return root;
        }

        protected override void Rebuild()
        {
            list.Children.Clear();
            rows.Clear();
            if (Views.Count == 0)
            {
                list.Children.Add(G.T("SCANNING FOR AI NODES…", 12, Cyan, FontWeights.Normal, G.Din));
                return;
            }
            for (int i = 0; i < Views.Count; i++)
            {
                if (i > 0) list.Children.Add(new Rectangle { Height = 1, Margin = new Thickness(0, 9, 0, 9), Fill = G.Lg(Color.FromArgb(0x90, 0xFF, 0x2B, 0xD6), Color.FromArgb(0, 0xFF, 0x2B, 0xD6), 0) });
                list.Children.Add(MakeRow(Views[i]));
            }
        }

        FrameworkElement MakeRow(ProviderView v)
        {
            var r = new Row { Id = v.Id, NextGlitch = Time + 2 + Rng.NextDouble() * 6 };
            var neon = G.Vivid(v.Color);
            var sp = new StackPanel { Tag = "pv:" + v.Id, Background = G.B(Colors.Black, 0.001) };
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var name = G.T(v.Name.ToUpperInvariant(), 15, Palette.Lighten(neon, 0.25), FontWeights.Bold, G.Din);
            name.Effect = G.Glow(neon, 12, 1);
            nameRow.Children.Add(name);
            r.Tag = G.T("", 9, neon, FontWeights.SemiBold, G.Din);
            var tagBox = new Border { Child = r.Tag, BorderBrush = G.B(neon, 0.8), BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(8, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            nameRow.Children.Add(tagBox);
            top.Children.Add(nameRow);

            var big = new Grid();
            r.BigR = G.T("", 24, Red, FontWeights.SemiBold, G.Din);
            r.BigC = G.T("", 24, Cyan, FontWeights.SemiBold, G.Din);
            r.Big = G.T("", 24, Colors.White, FontWeights.SemiBold, G.Din);
            r.BigR.Opacity = r.BigC.Opacity = 0;
            r.BigR.RenderTransform = new TranslateTransform();
            r.BigC.RenderTransform = new TranslateTransform();
            r.Big.RenderTransform = new TranslateTransform();
            r.Big.Effect = G.Glow(neon, 14, 0.9);
            big.Children.Add(r.BigR);
            big.Children.Add(r.BigC);
            big.Children.Add(r.Big);
            Grid.SetColumn(big, 1);
            top.Children.Add(big);
            sp.Children.Add(top);

            if (!v.HasData)
            {
                r.Error = G.T("", 11, Red, FontWeights.Normal, G.Ui);
                r.Error.TextWrapping = TextWrapping.Wrap;
                r.Error.MaxWidth = 260;
                sp.Children.Add(r.Error);
            }
            else
            {
                foreach (var m in v.Meters.Take(3))
                {
                    var b = new Bar { Key = m.Key };
                    var g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var lbl = G.T(Label(m), 10, Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), FontWeights.SemiBold, G.Din);
                    lbl.VerticalAlignment = VerticalAlignment.Center;
                    g.Children.Add(lbl);
                    b.Host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    b.Cells = new Rectangle[Segs];
                    for (int i = 0; i < Segs; i++)
                    {
                        b.Cells[i] = new Rectangle { Width = 5, Height = 11, Margin = new Thickness(0, 0, 2, 0) };
                        b.Host.Children.Add(b.Cells[i]);
                    }
                    Grid.SetColumn(b.Host, 1);
                    g.Children.Add(b.Host);
                    b.Pct = G.T("", 11, Colors.White, FontWeights.SemiBold, G.Din);
                    b.Pct.HorizontalAlignment = HorizontalAlignment.Right;
                    b.Pct.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(b.Pct, 2);
                    g.Children.Add(b.Pct);
                    b.Reset = G.T("", 10, Color.FromArgb(0xA8, 0x22, 0xF3, 0xFF), FontWeights.Normal, G.Din);
                    b.Reset.Margin = new Thickness(10, 0, 0, 0);
                    b.Reset.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(b.Reset, 3);
                    g.Children.Add(b.Reset);
                    sp.Children.Add(g);
                    r.Bars.Add(b);
                }
            }
            rows.Add(r);
            return sp;
        }

        static string Label(Meter m)
        {
            if (m.WindowMinutes == 10080) return "WK";
            if (m.WindowMinutes == 1440) return "DAY";
            if (m.WindowMinutes >= 40000) return "MO";
            string s = m.ShortLabel ?? "Q";
            return s.All(ch => ch < 128) ? s.ToUpperInvariant() : s;
        }

        static Color Level(double rem)
        {
            if (rem >= 50) return Cyan;
            if (rem >= 20) return Yellow;
            return Red;
        }

        protected override void Refresh()
        {
            clock.Text = DateTime.Now.ToString("HH:mm:ss");
            foreach (var r in rows)
            {
                var v = View(r.Id);
                if (v == null) continue;
                r.Tag.Text = (v.Plan ?? (v.HasData ? "ONLINE" : "OFFLINE")).ToUpperInvariant() + (v.Active ? " · BUSY" : "") + (v.Stale ? " · STALE" : "");
                string big = !v.HasData ? "ERR" : v.Unlimited ? "∞" : Fmt.Pct(v.HeadlineRemaining);
                r.Big.Text = r.BigR.Text = r.BigC.Text = big;
                if (r.Error != null) r.Error.Text = "> " + (v.Error ?? "NO SIGNAL");
                foreach (var b in r.Bars)
                {
                    var m = v.Meters.FirstOrDefault(x => x.Key == b.Key);
                    if (m == null) continue;
                    double rem = m.Unlimited ? 100 : m.Remaining;
                    int lit = (int)Math.Round(rem / 100 * Segs);
                    var col = m.Unlimited ? Magenta : Level(rem);
                    for (int i = 0; i < Segs; i++)
                        b.Cells[i].Fill = G.B(i < lit ? col : Color.FromArgb(0x24, col.R, col.G, col.B));
                    if (b.Host.Effect == null || ((System.Windows.Media.Effects.DropShadowEffect)b.Host.Effect).Color != col)
                        b.Host.Effect = G.Glow(col, 9, 0.9);
                    b.Pct.Text = m.Unlimited ? "∞" : Fmt.Pct(rem);
                    b.Pct.Foreground = G.B(Palette.Lighten(col, 0.3));
                    b.Reset.Text = m.Unlimited ? "NO LIMIT" : m.ResetsAt.HasValue ? "RST " + G.ClockText(m) : "IDLE";
                    b.Urgent = m == v.UseIt ? v.UseItLevel : 0;
                    if (b.Urgent == 0) b.Host.Opacity = 1;
                }
            }
        }

        public override void Tick(double dt)
        {
            base.Tick(dt);
            // header tube flicker
            if (flickerUntil < Time && Rng.NextDouble() < 0.006) flickerUntil = Time + 0.08 + Rng.NextDouble() * 0.12;
            header.Opacity = flickerUntil > Time ? 0.25 + Rng.NextDouble() * 0.3 : 1;
            live.Opacity = (int)(Time * 1.4) % 2 == 0 ? 1 : 0.25;
            double h = root.ActualHeight;
            if (h > 0) scanMove.Y = ((Time * 60) % (h + 60)) - 50;
            foreach (var r in rows)
            {
                if (Time > r.NextGlitch)
                {
                    r.GlitchUntil = Time + 0.12 + Rng.NextDouble() * 0.15;
                    r.NextGlitch = Time + 3 + Rng.NextDouble() * 7;
                }
                bool glitch = r.GlitchUntil > Time;
                r.BigR.Opacity = r.BigC.Opacity = glitch ? 0.75 : 0;
                ((TranslateTransform)r.BigR.RenderTransform).X = glitch ? -2 - Rng.NextDouble() * 2 : 0;
                ((TranslateTransform)r.BigC.RenderTransform).X = glitch ? 2 + Rng.NextDouble() * 2 : 0;
                ((TranslateTransform)r.Big.RenderTransform).X = glitch ? (Rng.NextDouble() - 0.5) * 2 : 0;
                var v = View(r.Id);
                r.Big.Opacity = v != null && v.Mood == SentriPet.Mood.Critical ? 0.6 + 0.4 * Math.Abs(Math.Sin(Time * 5)) : 1;
                foreach (var b in r.Bars)
                    if (b.Urgent > 0) b.Host.Opacity = G.UrgentPulse(b.Urgent, Time);   // expires unused soon
            }
        }
    }
}
