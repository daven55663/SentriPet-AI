using System;
using System.Windows.Media;

namespace SentriPet
{
    /// <summary>WPF colours and brushes for the Windows build (the core uses the framework-free <see cref="Rgba"/>).</summary>
    static class Palette
    {
        public static Color Hex(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.Gray; }
        }

        public static Color Mix(Color a, Color b, double t) { return a.ToRgba().Mix(b.ToRgba(), t).ToWpf(); }
        public static Color Lighten(Color c, double t) { return c.ToRgba().Lighten(t).ToWpf(); }
        public static Color Darken(Color c, double t) { return c.ToRgba().Darken(t).ToWpf(); }
        public static Color A(Color c, double alpha) { return c.ToRgba().WithAlpha(alpha).ToWpf(); }

        public static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static SolidColorBrush Brush(string hex) { return Brush(Hex(hex)); }

        /// <summary>Traffic-light colour for a remaining percentage.</summary>
        public static Color Level(double remaining) { return Rgba.Level(remaining).ToWpf(); }

        /// <summary>Stable pleasant colour for an unknown provider id.</summary>
        public static Color FromId(string id) { return Rgba.FromId(id).ToWpf(); }

        public static Color Hsl(double h, double s, double l) { return Rgba.Hsl(h, s, l).ToWpf(); }
    }

    static class WpfColor
    {
        public static Color ToWpf(this Rgba c) { return Color.FromArgb(c.A, c.R, c.G, c.B); }
        public static Rgba ToRgba(this Color c) { return Rgba.FromArgb(c.A, c.R, c.G, c.B); }
    }
}
