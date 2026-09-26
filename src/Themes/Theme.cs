using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SentriPet
{
    interface IThemeHost
    {
        AppSettings Settings { get; }
        Random Rng { get; }
        /// <summary>Mouse position (global, even outside the widget) in the coordinates of <paramref name="element"/>.</summary>
        Point? CursorIn(FrameworkElement element);
        void SaveSettings();
    }

    /// <summary>
    /// A visual style for the widget. Themes build their own visual tree from <see cref="ProviderView"/>s.
    /// Elements that represent a provider set Tag = "pv:{id}" so the window can hit-test hovers and clicks.
    /// </summary>
    abstract class Theme
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Mood { get; }
        public abstract string Blurb { get; }

        public FrameworkElement Root { get; private set; }
        protected IThemeHost Host;
        protected List<ProviderView> Views = new List<ProviderView>();
        protected double Time;
        protected Random Rng = new Random();
        string signature;

        public void Attach(IThemeHost host)
        {
            Host = host;
            Rng = host != null ? host.Rng : new Random();
            Root = CreateRoot();
        }

        public void Update(List<ProviderView> views)
        {
            Views = views ?? new List<ProviderView>();
            string sig = Signature(Views);
            if (sig != signature)
            {
                signature = sig;
                Rebuild();
            }
            Refresh();
        }

        protected virtual string Signature(List<ProviderView> views)
        {
            var sb = new StringBuilder();
            foreach (var v in views)
            {
                sb.Append(v.Id).Append(v.HasData ? '+' : '-');
                foreach (var m in v.Meters) sb.Append(m.Key).Append(',');
                sb.Append('|');
            }
            return sb.ToString();
        }

        protected abstract FrameworkElement CreateRoot();
        /// <summary>Recreate the provider visuals (provider list or meter layout changed).</summary>
        protected abstract void Rebuild();
        /// <summary>Update numbers and texts. Called on data change and once per second.</summary>
        protected abstract void Refresh();
        /// <summary>Animation frame.</summary>
        public virtual void Tick(double dt) { Time += dt; }
        /// <summary>
        /// Show a speech line for a provider (null = whole widget). Return false when the theme has no speech of
        /// its own — the window then shows the line in a floating bubble.
        /// </summary>
        public virtual bool Say(string providerId, string text) { return false; }
        /// <summary>User clicked a provider. Return false when the theme has no reaction of its own.</summary>
        public virtual bool Poke(string providerId) { return false; }
        /// <summary>Clicked somewhere in the widget; return true when the theme handled it.</summary>
        public virtual bool Click(Point rootPoint) { return false; }
        /// <summary>"reset" | "warn" | "critical" events from the controller.</summary>
        public virtual void Celebrate(string providerId) { }
        public virtual void Detach() { }

        /// <summary>
        /// The visible part of the widget, in the coordinates of <paramref name="relativeTo"/>. The hover card is
        /// placed outside this area. Themes with transparent spare room (e.g. a speech-bubble zone) override it.
        /// </summary>
        public virtual Rect ContentBounds(Visual relativeTo)
        {
            return BoundsOf(Root, relativeTo);
        }

        protected static Rect BoundsOf(FrameworkElement e, Visual relativeTo)
        {
            try
            {
                if (e == null || e.ActualWidth <= 0 || !relativeTo.IsAncestorOf(e)) return Rect.Empty;
                return e.TransformToAncestor(relativeTo).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
            }
            catch { return Rect.Empty; }
        }

        protected ProviderView View(string id) { return Views.FirstOrDefault(v => v.Id == id); }

        protected static string PvTag(ProviderView v) { return "pv:" + v.Id; }
    }

    class ThemeInfo
    {
        public string Id, Glyph;
        public Func<Theme> Create;
        string name, mood, blurb;
        public string Name { get { return L.T(name); } set { name = value; } }
        public string Mood { get { return L.T(mood); } set { mood = value; } }
        public string Blurb { get { return L.T(blurb); } set { blurb = value; } }
    }

    static class ThemeCatalog
    {
        public static readonly List<ThemeInfo> All = new List<ThemeInfo>
        {
            new ThemeInfo { Id = "pet", Name = L.N("果凍桌寵"), Mood = L.N("元氣滿滿"), Glyph = "●", Blurb = L.N("果凍小怪獸，額度越多肚子越滿"), Create = () => new PetTheme() },
            new ThemeInfo { Id = "glass", Name = L.N("極簡玻璃"), Mood = L.N("平靜專注"), Glyph = "◎", Blurb = L.N("毛玻璃卡片＋圓環，乾淨俐落"), Create = () => new GlassTheme() },
            new ThemeInfo { Id = "pixel", Name = L.N("像素勇者"), Mood = L.N("想打電動"), Glyph = "▦", Blurb = L.N("RPG 狀態列，HP/MP 就是你的額度"), Create = () => new PixelTheme() },
            new ThemeInfo { Id = "terminal", Name = L.N("駭客終端"), Mood = L.N("進入心流"), Glyph = "▮", Blurb = L.N("綠色磷光 CRT，點標題列換顏色"), Create = () => new TerminalTheme() },
            new ThemeInfo { Id = "gauge", Name = L.N("賽車儀表"), Mood = L.N("全速前進"), Glyph = "◔", Blurb = L.N("油表指針＋警示燈，AI 工作時遠光燈會亮"), Create = () => new GaugeTheme() },
            new ThemeInfo { Id = "potion", Name = L.N("魔法藥水"), Mood = L.N("有點夢幻"), Glyph = "⚗", Blurb = L.N("每個額度一瓶藥水，會冒泡泡"), Create = () => new PotionTheme() },
            new ThemeInfo { Id = "neon", Name = L.N("霓虹夜城"), Mood = L.N("深夜模式"), Glyph = "◆", Blurb = L.N("賽博龐克霓虹燈管，偶爾故障閃爍"), Create = () => new NeonTheme() },
            new ThemeInfo { Id = "note", Name = L.N("手寫便利貼"), Mood = L.N("慢慢來"), Glyph = "✎", Blurb = L.N("貼在螢幕角落的手寫小紙條"), Create = () => new NoteTheme() },
        };

        public static ThemeInfo Get(string id)
        {
            return All.FirstOrDefault(t => t.Id == id) ?? All[0];
        }
    }

    /// <summary>Drawing helpers shared by the themes.</summary>
    static class G
    {
        public static FontFamily Ui, Num, Mono, Din, Hand, Kai;
        /// <summary>The Windows UI font for the current language (also used by the menu styles).</summary>
        public static string UiName;

        static G() { UseFonts(); }

        /// <summary>Picks the fonts for the language in use (<see cref="L.Script"/>); themes built afterwards use them.</summary>
        public static void UseFonts()
        {
            string ui, kai;
            switch (L.Script)
            {
                case "sc": ui = "Microsoft YaHei UI"; kai = "KaiTi, 楷体, Microsoft YaHei UI"; break; // i18n-ignore
                case "ja": ui = "Yu Gothic UI, Meiryo UI"; kai = "UD Digi Kyokasho N-R, Yu Mincho, Yu Gothic UI"; break;
                case "ko": ui = "Malgun Gothic"; kai = "Gungsuh, Batang, Malgun Gothic"; break;
                case "latin": ui = "Segoe UI, Microsoft JhengHei UI"; kai = "Segoe Print, Ink Free, Segoe UI"; break;
                default: ui = "Microsoft JhengHei UI"; kai = "DFKai-SB, 標楷體, Microsoft JhengHei UI"; break; // i18n-ignore
            }
            string first = ui.Split(',')[0].Trim();
            UiName = ui;
            Ui = new FontFamily(ui + ", Segoe UI");
            Num = new FontFamily("Segoe UI Variable Display, Segoe UI, " + ui);
            Mono = new FontFamily("Cascadia Mono, Consolas, " + ui);
            Din = new FontFamily("Bahnschrift, Segoe UI, " + ui);
            Hand = new FontFamily("Ink Free, Segoe Print, " + kai.Split(',')[0].Trim() + ", " + first);
            Kai = new FontFamily(kai);
        }

        public static SolidColorBrush B(Color c) { return Palette.Brush(c); }
        public static SolidColorBrush B(string hex) { return Palette.Brush(hex); }
        public static SolidColorBrush B(Color c, double alpha) { return Palette.Brush(Palette.A(c, alpha)); }

        public static TextBlock T(string text, double size, Color color, FontWeight weight, FontFamily font)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = B(color),
                FontWeight = weight,
                FontFamily = font ?? Ui,
                TextTrimming = TextTrimming.None,
                SnapsToDevicePixels = true,
            };
        }

        public static TextBlock T(string text, double size, Color color)
        {
            return T(text, size, color, FontWeights.Normal, Ui);
        }

        public static LinearGradientBrush Lg(Color a, Color b, double angleDeg)
        {
            var rad = angleDeg * Math.PI / 180;
            var br = new LinearGradientBrush
            {
                StartPoint = new Point(0.5 - Math.Cos(rad) / 2, 0.5 - Math.Sin(rad) / 2),
                EndPoint = new Point(0.5 + Math.Cos(rad) / 2, 0.5 + Math.Sin(rad) / 2),
            };
            br.GradientStops.Add(new GradientStop(a, 0));
            br.GradientStops.Add(new GradientStop(b, 1));
            br.Freeze();
            return br;
        }

        public static LinearGradientBrush Vertical(params Color[] colors)
        {
            var br = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            for (int i = 0; i < colors.Length; i++)
                br.GradientStops.Add(new GradientStop(colors[i], colors.Length == 1 ? 0 : (double)i / (colors.Length - 1)));
            br.Freeze();
            return br;
        }

        public static DropShadowEffect Shadow(double blur, double depth, double opacity, Color color)
        {
            return new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Opacity = opacity, Color = color, Direction = 270, RenderingBias = RenderingBias.Performance };
        }

        public static DropShadowEffect Glow(Color color, double blur, double opacity)
        {
            return new DropShadowEffect { BlurRadius = blur, ShadowDepth = 0, Opacity = opacity, Color = color, RenderingBias = RenderingBias.Performance };
        }

        public static void Place(UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
        }

        public static Ellipse Circle(double cx, double cy, double r, Brush fill, Brush stroke, double thickness)
        {
            var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
            Place(e, cx - r, cy - r);
            return e;
        }

        public static Ellipse Oval(double cx, double cy, double w, double h, Brush fill)
        {
            var e = new Ellipse { Width = w, Height = h, Fill = fill };
            Place(e, cx - w / 2, cy - h / 2);
            return e;
        }

        public static Path P(string data, Brush fill, Brush stroke, double thickness)
        {
            return new Path
            {
                Data = Geometry.Parse(data),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
        }

        /// <summary>Point on a circle; 0° = 12 o'clock, clockwise.</summary>
        public static Point Polar(Point c, double r, double deg)
        {
            double rad = (deg - 90) * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }

        /// <summary>Open arc geometry from a0 to a1 degrees (0° = up, clockwise).</summary>
        public static Geometry Arc(Point c, double r, double a0, double a1)
        {
            if (a1 < a0) { var t = a0; a0 = a1; a1 = t; }
            double sweep = a1 - a0;
            if (sweep >= 359.99) return new EllipseGeometry(c, r, r);
            if (sweep < 0.01) sweep = 0.01;
            var start = Polar(c, r, a0);
            var end = Polar(c, r, a0 + sweep);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Filled star/sparkle polygon.</summary>
        public static Geometry Star(Point c, double rOuter, double rInner, int points, double rotDeg)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (int i = 0; i < points * 2; i++)
                {
                    double r = i % 2 == 0 ? rOuter : rInner;
                    var p = Polar(c, r, rotDeg + i * 180.0 / points);
                    if (i == 0) ctx.BeginFigure(p, true, true);
                    else ctx.LineTo(p, true, true);
                }
            }
            g.Freeze();
            return g;
        }

        /// <summary>A little alarm clock (a 24×24 drawing scaled to <paramref name="size"/>): "use it before it resets".</summary>
        public static Viewbox AlarmClock(double size, Color ring)
        {
            var c = new Canvas { Width = 24, Height = 24 };
            var ink = B(Palette.Hex("#2B211E"));
            var bell = B(ring);
            c.Children.Add(P("M 2.6,9.2 A 5.2,5.2 0 0 1 9.4,2.9 Z", bell, ink, 1.1));
            c.Children.Add(P("M 21.4,9.2 A 5.2,5.2 0 0 0 14.6,2.9 Z", bell, ink, 1.1));
            c.Children.Add(P("M 6.8,19.6 L 4.6,22.6 M 17.2,19.6 L 19.4,22.6", null, ink, 1.8));
            c.Children.Add(P("M 12,5.2 L 12,3.2 M 10.4,3.2 L 13.6,3.2", null, ink, 1.6));
            c.Children.Add(Circle(12, 13.4, 8, B(Colors.White), bell, 2.2));
            c.Children.Add(P("M 12,13.4 L 12,8.6 M 12,13.4 L 15.4,15", null, ink, 1.6));
            c.Children.Add(Circle(12, 13.4, 1.1, ink, null, 0));
            return new Viewbox { Width = size, Height = size, Child = c };
        }

        /// <summary>Background, border and text colours for the "use it" urgency levels 1–3.</summary>
        public static void UseItColors(int level, out Color bg, out Color edge, out Color ink)
        {
            if (level >= 3) { bg = Palette.Hex("#FEE2E2"); edge = Palette.Hex("#F87171"); ink = Palette.Hex("#991B1B"); }
            else if (level == 2) { bg = Palette.Hex("#FFEDD5"); edge = Palette.Hex("#FB923C"); ink = Palette.Hex("#9A3412"); }
            else { bg = Palette.Hex("#FEF3C7"); edge = Palette.Hex("#FBBF24"); ink = Palette.Hex("#92400E"); }
        }

        public static Color UseItAccent(int level)
        {
            return level >= 3 ? Palette.Hex("#EF4444") : level == 2 ? Palette.Hex("#F97316") : Palette.Hex("#F59E0B");
        }

        /// <summary>
        /// Opacity of a bar whose quota is about to expire unused: a slow breath at level 1, quicker on the last day,
        /// a fast blink in the last hours.
        /// </summary>
        public static double UrgentPulse(int level, double t)
        {
            double period = level >= 3 ? 0.7 : level == 2 ? 1.2 : 2.4;
            double low = level >= 3 ? 0.15 : level == 2 ? 0.3 : 0.5;
            double s = 0.5 + 0.5 * Math.Cos(2 * Math.PI * t / period);
            return Math.Round(low + (1 - low) * s, 2);
        }

        public static Border Pill(UIElement child, Brush bg, double radius, Thickness pad)
        {
            return new Border { Child = child, Background = bg, CornerRadius = new CornerRadius(radius), Padding = pad };
        }

        public static FrameworkElement Tagged(FrameworkElement e, ProviderView v)
        {
            e.Tag = "pv:" + v.Id;
            return e;
        }

        /// <summary>Simple horizontal progress bar (remaining) with rounded ends.</summary>
        public static Grid Bar(double width, double height, Brush track, out Border fill)
        {
            var g = new Grid { Width = width, Height = height };
            g.Children.Add(new Border { Background = track, CornerRadius = new CornerRadius(height / 2) });
            fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            g.Children.Add(fill);
            return g;
        }

        public static string ResetText(Meter m)
        {
            if (m == null) return "";
            if (m.Unlimited) return L.T("無限制");
            if (!m.ResetsAt.HasValue) return m.Used <= 0 ? L.T("閒置中") : L.T("重置時間未知");
            return (m.ResetApprox ? "≈" : "") + Fmt.Countdown(m.ResetsAt);
        }

        public static string ClockText(Meter m)
        {
            if (m == null || m.Unlimited) return "--:--:--";
            if (!m.ResetsAt.HasValue) return "--:--:--";
            return (m.ResetApprox ? "≈" : "") + Fmt.Clock(m.ResetsAt);
        }

        /// <summary>A brighter, more saturated variant for dark/neon themes.</summary>
        public static Color Vivid(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double h = 0;
            if (max - min > 1e-6)
            {
                if (max == r) h = 60 * (((g - b) / (max - min)) % 6);
                else if (max == g) h = 60 * ((b - r) / (max - min) + 2);
                else h = 60 * ((r - g) / (max - min) + 4);
            }
            if (h < 0) h += 360;
            return Palette.Hsl(h, 0.95, 0.62);
        }

        public static Color Vivid(Rgba c) { return Vivid(c.ToWpf()); }
        public static Color Desaturate(Rgba c, double t) { return Desaturate(c.ToWpf(), t); }

        public static Color Desaturate(Color c, double t)
        {
            byte gray = (byte)(c.R * 0.3 + c.G * 0.59 + c.B * 0.11);
            return Palette.Mix(c, Color.FromRgb(gray, gray, gray), t);
        }
    }
}
