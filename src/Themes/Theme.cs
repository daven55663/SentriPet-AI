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
        /// <summary>Show a speech line for a provider (null = whole widget).</summary>
        public virtual void Say(string providerId, string text) { }
        /// <summary>User clicked a provider.</summary>
        public virtual void Poke(string providerId) { }
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
        public string Id, Name, Mood, Blurb, Glyph;
        public Func<Theme> Create;
    }

    static class ThemeCatalog
    {
        public static readonly List<ThemeInfo> All = new List<ThemeInfo>
        {
            new ThemeInfo { Id = "pet", Name = "果凍桌寵", Mood = "元氣滿滿", Glyph = "●", Blurb = "果凍小怪獸，額度越多肚子越滿", Create = () => new PetTheme() },
            new ThemeInfo { Id = "glass", Name = "極簡玻璃", Mood = "平靜專注", Glyph = "◎", Blurb = "毛玻璃卡片＋圓環，乾淨俐落", Create = () => new GlassTheme() },
            new ThemeInfo { Id = "pixel", Name = "像素勇者", Mood = "想打電動", Glyph = "▦", Blurb = "RPG 狀態列，HP/MP 就是你的額度", Create = () => new PixelTheme() },
            new ThemeInfo { Id = "terminal", Name = "駭客終端", Mood = "進入心流", Glyph = "▮", Blurb = "綠色磷光 CRT，點標題列換顏色", Create = () => new TerminalTheme() },
            new ThemeInfo { Id = "gauge", Name = "賽車儀表", Mood = "全速前進", Glyph = "◔", Blurb = "油表指針＋警示燈，AI 工作時遠光燈會亮", Create = () => new GaugeTheme() },
            new ThemeInfo { Id = "potion", Name = "魔法藥水", Mood = "有點夢幻", Glyph = "⚗", Blurb = "每個額度一瓶藥水，會冒泡泡", Create = () => new PotionTheme() },
            new ThemeInfo { Id = "neon", Name = "霓虹夜城", Mood = "深夜模式", Glyph = "◆", Blurb = "賽博龐克霓虹燈管，偶爾故障閃爍", Create = () => new NeonTheme() },
            new ThemeInfo { Id = "note", Name = "手寫便利貼", Mood = "慢慢來", Glyph = "✎", Blurb = "貼在螢幕角落的手寫小紙條", Create = () => new NoteTheme() },
        };

        public static ThemeInfo Get(string id)
        {
            return All.FirstOrDefault(t => t.Id == id) ?? All[0];
        }
    }

    /// <summary>Exponential smoothing toward a target.</summary>
    class Anim
    {
        public double Value, Target;
        public Anim(double v) { Value = Target = v; }
        public void Step(double dt, double speed)
        {
            Value += (Target - Value) * (1 - Math.Exp(-speed * dt));
            // settle exactly, so unchanged values stop invalidating the visuals every frame
            if (Math.Abs(Target - Value) < 0.02) Value = Target;
        }
    }

    /// <summary>Damped spring (for needles that overshoot a little).</summary>
    class Spring
    {
        public double X, V, Target;
        public Spring(double x) { X = Target = x; }
        public void Step(double dt, double k, double damping)
        {
            int n = (int)Math.Ceiling(dt / 0.008);
            double h = dt / Math.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                double a = k * (Target - X) - damping * V;
                V += a * h;
                X += V * h;
            }
        }
    }

    /// <summary>Drawing helpers shared by the themes.</summary>
    static class G
    {
        public static readonly FontFamily Ui = new FontFamily("Microsoft JhengHei UI, Segoe UI");
        public static readonly FontFamily Num = new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft JhengHei UI");
        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas, Microsoft JhengHei UI");
        public static readonly FontFamily Din = new FontFamily("Bahnschrift, Segoe UI, Microsoft JhengHei UI");
        public static readonly FontFamily Hand = new FontFamily("Ink Free, Segoe Print, DFKai-SB");
        public static readonly FontFamily Kai = new FontFamily("DFKai-SB, 標楷體, Microsoft JhengHei UI");

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
            if (m.Unlimited) return "無限制";
            if (!m.ResetsAt.HasValue) return m.Used <= 0 ? "閒置中" : "重置時間未知";
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

        public static Color Desaturate(Color c, double t)
        {
            byte gray = (byte)(c.R * 0.3 + c.G * 0.59 + c.B * 0.11);
            return Palette.Mix(c, Color.FromRgb(gray, gray, gray), t);
        }
    }
}
