using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SentriPet
{
    /// <summary>Green-phosphor CRT terminal. Click the title bar to change the phosphor colour.</summary>
    class TerminalTheme : Theme
    {
        static readonly string[] ColorNames = { "green", "amber", "cyan", "white" };
        static readonly string[] Bright = { "#39FF7A", "#FFB43A", "#46F0FF", "#E8EEF8" };
        static readonly string[] Dim = { "#1E8A45", "#8A5C12", "#1C7C88", "#7C8698" };

        Border root;
        Grid titleBar;
        TextBlock title, screen;
        Rectangle scan;
        TranslateTransform scanMove;
        readonly List<Ellipse> lights = new List<Ellipse>();
        int colorIdx;
        Color bright, dim;
        struct Seg { public string Text; public Color Color; public int Blink; }
        List<List<Seg>> lines = new List<List<Seg>>();
        int revealTotal, revealShown = int.MaxValue;
        double revealClock;
        readonly List<Run> spinners = new List<Run>();
        // bars of a quota that expires unused soon: run, level, lit brush, dim brush
        readonly List<Tuple<Run, int, Brush, Brush>> blinkers = new List<Tuple<Run, int, Brush, Brush>>();
        Run cursor;
        string lastSig;

        public override string Id { get { return "terminal"; } }
        public override string Name { get { return L.T("駭客終端"); } }
        public override string Mood { get { return L.T("進入心流"); } }
        public override string Blurb { get { return L.T("綠色磷光 CRT，點標題列換顏色"); } }

        protected override FrameworkElement CreateRoot()
        {
            colorIdx = Math.Max(0, Array.IndexOf(ColorNames, Host != null ? Host.Settings.TerminalColor : "green"));
            root = new Border
            {
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(14),
                BorderThickness = new Thickness(1.3),
                Effect = G.Shadow(24, 6, 0.5, Colors.Black),
                MinWidth = 300,
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            titleBar = new Grid { Height = 24, Tag = "terminal-title" };
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var hex in new[] { "#FF5F57", "#FEBC2E", "#28C840" })
            {
                var e = new Ellipse { Width = 8, Height = 8, Fill = G.B(Palette.Hex(hex), 0.75), Margin = new Thickness(0, 0, 6, 0) };
                lights.Add(e);
                dots.Children.Add(e);
            }
            titleBar.Children.Add(dots);
            title = new TextBlock { FontFamily = G.Mono, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Text = "ai-usage — watch" };
            titleBar.Children.Add(title);
            grid.Children.Add(titleBar);

            screen = new TextBlock
            {
                FontFamily = G.Mono,
                FontSize = 12.5,
                Margin = new Thickness(14, 6, 16, 12),
                LineHeight = 18,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            Grid.SetRow(screen, 1);
            grid.Children.Add(screen);

            // scanlines + vignette
            var lineBrush = new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 4, 3),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 4, 3),
                ViewboxUnits = BrushMappingMode.Absolute,
                Drawing = new GeometryDrawing(G.B(Colors.Black, 0.55), null, new RectangleGeometry(new Rect(0, 2, 4, 1))),
            };
            scanMove = new TranslateTransform();
            lineBrush.Transform = scanMove;
            scan = new Rectangle { Fill = lineBrush, IsHitTestVisible = false, Opacity = 0.35 };
            Grid.SetRowSpan(scan, 2);
            grid.Children.Add(scan);
            var vignette = new Border
            {
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = false,
                Background = new RadialGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0x70, 0, 0, 0)) { RadiusX = 0.75, RadiusY = 0.85 },
            };
            Grid.SetRowSpan(vignette, 2);
            grid.Children.Add(vignette);
            root.Child = grid;
            ApplyColors();
            return root;
        }

        void ApplyColors()
        {
            bright = Palette.Hex(Bright[colorIdx]);
            dim = Palette.Hex(Dim[colorIdx]);
            root.Background = G.Vertical(Color.FromArgb(0xF4, 0x0B, 0x10, 0x0D), Color.FromArgb(0xF7, 0x05, 0x08, 0x06));
            root.BorderBrush = G.B(dim, 0.8);
            titleBar.Background = G.B(bright, 0.07);
            title.Foreground = G.B(dim);
            screen.Effect = G.Glow(bright, 9, 0.7);
            lastSig = null;
            Refresh();
        }

        protected override void Rebuild() { lastSig = null; }

        static string Col(string s, int w)
        {
            if (s.Length > w) return s.Substring(0, w);
            return s.PadRight(w);
        }

        static string AsciiLabel(Meter m)
        {
            string s = m.ShortLabel ?? "";
            if (s.Length > 0 && s.All(ch => ch < 128)) return s.ToLowerInvariant();
            if (m.WindowMinutes == 10080) return "wk";
            if (m.WindowMinutes == 1440) return "day";
            if (m.WindowMinutes >= 40000) return "mo";
            return "q";
        }

        Color LevelColor(double rem)
        {
            if (rem >= 50) return bright;
            if (rem >= 20) return Palette.Hex("#FFB43A");
            return Palette.Hex("#FF5A5A");
        }

        protected override void Refresh()
        {
            if (screen == null) return;
            var nl = new List<List<Seg>>();
            Action<List<Seg>, string, Color> add = (l, t, c) => l.Add(new Seg { Text = t, Color = c });
            var head = new List<Seg>();
            add(head, "$ ", dim);
            add(head, "ai-usage --watch", bright);
            nl.Add(head);
            if (Views.Count == 0)
            {
                var l = new List<Seg>();
                add(l, "scanning for AI tools...", dim);
                nl.Add(l);
            }
            foreach (var v in Views)
            {
                string name = Col(v.Name.ToLowerInvariant().Replace(' ', '-'), 8);
                if (!v.HasData)
                {
                    var l = new List<Seg>();
                    add(l, name, bright);
                    add(l, "  !! ", Palette.Hex("#FF5A5A"));
                    add(l, v.Error ?? "no data", dim);
                    nl.Add(l);
                    continue;
                }
                bool first = true;
                foreach (var m in v.Meters.Take(3))
                {
                    var l = new List<Seg>();
                    add(l, first ? name : new string(' ', 8), bright);
                    add(l, first && v.Active ? "*" : " ", bright);
                    add(l, Col(AsciiLabel(m), 4), dim);
                    if (m.Unlimited)
                    {
                        add(l, "[", dim);
                        add(l, "~~~~ unlimited ~~~", bright);
                        add(l, "]", dim);
                    }
                    else
                    {
                        int cells = 16;
                        int full = (int)Math.Round(m.Remaining / 100 * cells);
                        var lc = LevelColor(m.Remaining);
                        add(l, "[", dim);
                        if (m == v.UseIt && v.UseItLevel > 0)
                            l.Add(new Seg { Text = new string('█', full), Color = v.UseItLevel >= 3 ? Palette.Hex("#FF5A5A") : Palette.Hex("#FFB43A"), Blink = v.UseItLevel });
                        else add(l, new string('█', full), lc);
                        add(l, new string('░', cells - full), G.Desaturate(dim, 0.2));
                        add(l, "]", dim);
                        add(l, Fmt.Pct(m.Remaining).PadLeft(5), lc);
                        add(l, "  ↻ ", dim);
                        add(l, m.ResetsAt.HasValue ? G.ClockText(m) : (m.Used <= 0 ? "idle" : "--"), bright);
                    }
                    nl.Add(l);
                    first = false;
                }
                if (v.Stale)
                {
                    var l = new List<Seg>();
                    add(l, new string(' ', 9) + "# stale: " + Fmt.Ago(v.Snap.ObservedAt), dim);
                    nl.Add(l);
                }
            }
            if (sayText != null && Time < sayUntil)
            {
                var l = new List<Seg>();
                add(l, sayText, dim);
                nl.Add(l);
            }
            var foot = new List<Seg>();
            add(foot, "-- " + DateTime.Now.ToString("HH:mm:ss") + " · " + Views.Count + " AI" + (Views.Count == 1 ? "" : "s") + " online --", dim);
            nl.Add(foot);
            var prompt = new List<Seg>();
            add(prompt, "$ ", dim);
            nl.Add(prompt);

            string sig = string.Join("|", Views.Select(v => v.Id + (v.HasData ? "+" : "-")));
            if (sig != lastSig)
            {
                lastSig = sig;
                revealShown = 0;
                revealTotal = nl.Sum(l => l.Sum(s => s.Text.Length));
            }
            lines = nl;
            Render();
        }

        void Render()
        {
            screen.Inlines.Clear();
            spinners.Clear();
            blinkers.Clear();
            int budget = revealShown;
            for (int i = 0; i < lines.Count && budget > 0; i++)
            {
                if (i > 0) screen.Inlines.Add(new LineBreak());
                foreach (var s in lines[i])
                {
                    if (budget <= 0) break;
                    string t = s.Text.Length <= budget ? s.Text : s.Text.Substring(0, budget);
                    budget -= s.Text.Length;
                    var run = new Run(t) { Foreground = G.B(s.Color) };
                    if (t == "*") spinners.Add(run);
                    if (s.Blink > 0) blinkers.Add(Tuple.Create(run, s.Blink, (Brush)G.B(s.Color), (Brush)G.B(s.Color, 0.22)));
                    screen.Inlines.Add(run);
                }
            }
            cursor = new Run("█") { Foreground = G.B(bright) };
            screen.Inlines.Add(cursor);
        }

        public override void Tick(double dt)
        {
            base.Tick(dt);
            if (revealShown < revealTotal)
            {
                revealClock += dt;
                revealShown = Math.Min(revealTotal, (int)(revealClock * 900));
                Render();
                if (revealShown >= revealTotal) revealShown = int.MaxValue;
            }
            else revealClock = 0;
            if (cursor != null) cursor.Foreground = ((int)(Time * 2) % 2 == 0) ? G.B(bright) : Brushes.Transparent;
            string spin = "|/-\\".Substring((int)(Time * 8) % 4, 1);
            foreach (var r in spinners) r.Text = spin;
            foreach (var b in blinkers)
            {
                var brush = G.UrgentPulse(b.Item2, Time) > 0.6 ? b.Item3 : b.Item4;
                if (b.Item1.Foreground != brush) b.Item1.Foreground = brush;
            }
            scanMove.Y = (Time * 6) % 3;
            if (Rng.NextDouble() < 0.08) root.Opacity = 0.93 + Rng.NextDouble() * 0.07;
        }

        public override bool Click(Point rootPoint)
        {
            var p = root.TranslatePoint(rootPoint, titleBar);
            if (p.Y >= 0 && p.Y <= titleBar.ActualHeight && p.X >= 0 && p.X <= titleBar.ActualWidth)
            {
                colorIdx = (colorIdx + 1) % ColorNames.Length;
                if (Host != null)
                {
                    Host.Settings.TerminalColor = ColorNames[colorIdx];
                    Host.SaveSettings();
                }
                ApplyColors();
                return true;
            }
            return false;
        }

        string sayText;
        double sayUntil;

        public override bool Say(string providerId, string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            // printed as a comment line above the prompt for a few seconds
            sayText = "# " + (providerId != null ? providerId + ": " : "") + text;
            sayUntil = Time + 6 + text.Length * 0.1;
            Refresh();
            return true;
        }
    }
}
