using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SentriPet
{
    interface IThemeHost
    {
        AppSettings Settings { get; }
        Random Rng { get; }
        /// <summary>Mouse position in the coordinates of <paramref name="element"/>, when it is known.</summary>
        Point? CursorIn(Visual element);
        void SaveSettings();
    }

    /// <summary>
    /// A visual style for the widget (Avalonia port of the WPF themes). Elements that represent a provider set
    /// Tag = "pv:{id}" so the window can hit-test hovers and clicks.
    /// </summary>
    abstract class Theme
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Mood { get; }
        public abstract string Blurb { get; }

        public Control Root { get; private set; }
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

        protected abstract Control CreateRoot();
        /// <summary>Recreate the provider visuals (provider list or meter layout changed).</summary>
        protected abstract void Rebuild();
        /// <summary>Update numbers and texts. Called on data change and once per second.</summary>
        protected abstract void Refresh();
        /// <summary>Animation frame.</summary>
        public virtual void Tick(double dt) { Time += dt; }
        /// <summary>Show a speech line (null = whole widget). False when the theme has no speech of its own.</summary>
        public virtual bool Say(string providerId, string text) { return false; }
        /// <summary>User clicked a provider. False when the theme has no reaction of its own.</summary>
        public virtual bool Poke(string providerId) { return false; }
        /// <summary>Clicked somewhere in the widget; true when the theme handled it.</summary>
        public virtual bool Click(Point rootPoint) { return false; }
        public virtual void Celebrate(string providerId) { }
        public virtual void Detach() { }

        /// <summary>The visible part of the widget in the coordinates of <paramref name="relativeTo"/> (null if not laid out).</summary>
        public virtual Rect? ContentBounds(Visual relativeTo)
        {
            return BoundsOf(Root, relativeTo);
        }

        protected static Rect? BoundsOf(Control e, Visual relativeTo)
        {
            if (e == null || e.Bounds.Width <= 0) return null;
            var m = e.TransformToVisual(relativeTo);
            if (m == null) return null;
            return new Rect(e.Bounds.Size).TransformToAABB(m.Value);
        }

        protected ProviderView View(string id) { return Views.FirstOrDefault(v => v.Id == id); }
    }

    class ThemeInfo
    {
        public string Id, Name, Mood, Blurb;
        public Func<Theme> Create;
    }

    static class ThemeCatalog
    {
        public static readonly List<ThemeInfo> All = new List<ThemeInfo>
        {
            new ThemeInfo { Id = "pet", Name = "果凍桌寵", Mood = "元氣滿滿", Blurb = "果凍小怪獸，額度越多肚子越滿", Create = () => new PetTheme() },
        };

        public static ThemeInfo Get(string id)
        {
            return All.FirstOrDefault(t => t.Id == id) ?? All[0];
        }
    }

    /// <summary>Avalonia colours and brushes (the core uses the framework-free <see cref="Rgba"/>).</summary>
    static class Palette
    {
        public static Color Hex(string hex) { return Rgba.Hex(hex).ToColor(); }
        public static Color Mix(Color a, Color b, double t) { return a.ToRgba().Mix(b.ToRgba(), t).ToColor(); }
        public static Color Lighten(Color c, double t) { return c.ToRgba().Lighten(t).ToColor(); }
        public static Color Darken(Color c, double t) { return c.ToRgba().Darken(t).ToColor(); }
        public static Color A(Color c, double alpha) { return c.ToRgba().WithAlpha(alpha).ToColor(); }
        public static IBrush Brush(Color c) { return new ImmutableSolidColorBrush(c); }
        public static IBrush Brush(string hex) { return Brush(Hex(hex)); }
        public static Color Level(double remaining) { return Rgba.Level(remaining).ToColor(); }
        public static Color FromId(string id) { return Rgba.FromId(id).ToColor(); }
        public static Color Hsl(double h, double s, double l) { return Rgba.Hsl(h, s, l).ToColor(); }
    }

    static class AvaloniaColor
    {
        public static Color ToColor(this Rgba c) { return Color.FromArgb(c.A, c.R, c.G, c.B); }
        public static Rgba ToRgba(this Color c) { return Rgba.FromArgb(c.A, c.R, c.G, c.B); }
    }

    /// <summary>Drawing helpers shared by the themes.</summary>
    static class G
    {
        // font lists: the first one installed wins (Windows, macOS, Linux)
        public static readonly FontFamily Ui = new FontFamily("Microsoft JhengHei UI, PingFang TC, Noto Sans CJK TC, Noto Sans TC, Source Han Sans TC, Segoe UI, Helvetica Neue, Noto Sans, Ubuntu, DejaVu Sans, Liberation Sans");
        public static readonly FontFamily Num = new FontFamily("Segoe UI Variable Display, Segoe UI, SF Pro Display, Helvetica Neue, Noto Sans, Ubuntu, DejaVu Sans, Liberation Sans, Noto Sans CJK TC, Microsoft JhengHei UI, PingFang TC");
        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas, SF Mono, Menlo, DejaVu Sans Mono, Noto Sans Mono");

        public static IBrush B(Color c) { return Palette.Brush(c); }
        public static IBrush B(string hex) { return Palette.Brush(hex); }
        public static IBrush B(Color c, double alpha) { return Palette.Brush(Palette.A(c, alpha)); }

        public static TextBlock T(string text, double size, Color color, FontWeight weight, FontFamily font)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = B(color), FontWeight = weight, FontFamily = font ?? Ui };
        }

        public static TextBlock T(string text, double size, Color color) { return T(text, size, color, FontWeight.Normal, Ui); }

        static RelativePoint Rel(double x, double y) { return new RelativePoint(x, y, RelativeUnit.Relative); }

        public static IBrush Lg(Color a, Color b, double angleDeg)
        {
            var rad = angleDeg * Math.PI / 180;
            var br = new LinearGradientBrush
            {
                StartPoint = Rel(0.5 - Math.Cos(rad) / 2, 0.5 - Math.Sin(rad) / 2),
                EndPoint = Rel(0.5 + Math.Cos(rad) / 2, 0.5 + Math.Sin(rad) / 2),
            };
            br.GradientStops.Add(new GradientStop(a, 0));
            br.GradientStops.Add(new GradientStop(b, 1));
            return br.ToImmutable();
        }

        public static IBrush Vertical(params Color[] colors)
        {
            var br = new LinearGradientBrush { StartPoint = Rel(0, 0), EndPoint = Rel(0, 1) };
            for (int i = 0; i < colors.Length; i++)
                br.GradientStops.Add(new GradientStop(colors[i], colors.Length == 1 ? 0 : (double)i / (colors.Length - 1)));
            return br.ToImmutable();
        }

        public static IEffect Shadow(double blur, double depth, double opacity, Color color)
        {
            return new DropShadowEffect { BlurRadius = blur, OffsetX = 0, OffsetY = depth, Opacity = opacity, Color = color };
        }

        public static IEffect Glow(Color color, double blur, double opacity)
        {
            return new DropShadowEffect { BlurRadius = blur, OffsetX = 0, OffsetY = 0, Opacity = opacity, Color = color };
        }

        public static void Place(Control e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
        }

        public static Ellipse Circle(double cx, double cy, double r, IBrush fill, IBrush stroke, double thickness)
        {
            var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
            Place(e, cx - r, cy - r);
            return e;
        }

        public static Ellipse Oval(double cx, double cy, double w, double h, IBrush fill)
        {
            var e = new Ellipse { Width = w, Height = h, Fill = fill };
            Place(e, cx - w / 2, cy - h / 2);
            return e;
        }

        public static Path P(string data, IBrush fill, IBrush stroke, double thickness)
        {
            return new Path
            {
                Data = Geometry.Parse(data),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeJoin = PenLineJoin.Round,
                StrokeLineCap = PenLineCap.Round,
            };
        }

        /// <summary>Scale/rotate around a point given in the element's own coordinates (WPF's CenterX/CenterY).</summary>
        public static RelativePoint At(double x, double y) { return new RelativePoint(x, y, RelativeUnit.Absolute); }

        /// <summary>Point on a circle; 0° = 12 o'clock, clockwise.</summary>
        public static Point Polar(Point c, double r, double deg)
        {
            double rad = (deg - 90) * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
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
                    if (i == 0) ctx.BeginFigure(p, true);
                    else ctx.LineTo(p);
                }
                ctx.EndFigure(true);
            }
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

        /// <summary>Opacity of a bar whose quota is about to expire unused (slow breath → fast blink).</summary>
        public static double UrgentPulse(int level, double t)
        {
            double period = level >= 3 ? 0.7 : level == 2 ? 1.2 : 2.4;
            double low = level >= 3 ? 0.15 : level == 2 ? 0.3 : 0.5;
            double s = 0.5 + 0.5 * Math.Cos(2 * Math.PI * t / period);
            return Math.Round(low + (1 - low) * s, 2);
        }

        public static Border Pill(Control child, IBrush bg, double radius, Thickness pad)
        {
            return new Border { Child = child, Background = bg, CornerRadius = new CornerRadius(radius), Padding = pad };
        }

        /// <summary>Simple horizontal progress bar (remaining) with rounded ends.</summary>
        public static Grid Bar(double width, double height, IBrush track, out Border fill)
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

        /// <summary>A brighter, more saturated variant for dark themes.</summary>
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

        public static Color Vivid(Rgba c) { return Vivid(c.ToColor()); }
        public static Color Desaturate(Rgba c, double t) { return Desaturate(c.ToColor(), t); }

        public static Color Desaturate(Color c, double t)
        {
            byte gray = (byte)(c.R * 0.3 + c.G * 0.59 + c.B * 0.11);
            return Palette.Mix(c, Color.FromRgb(gray, gray, gray), t);
        }
    }
}
