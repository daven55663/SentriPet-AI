using System;
using System.Collections.Generic;
using System.Globalization;

namespace SentriPet
{
    /// <summary>
    /// A colour that does not belong to any UI framework, so the core can be shared by the WPF and the Avalonia
    /// builds; each UI converts it to its own colour type.
    /// </summary>
    struct Rgba : IEquatable<Rgba>
    {
        public readonly byte A, R, G, B;

        public Rgba(byte a, byte r, byte g, byte b)
        {
            A = a; R = r; G = g; B = b;
        }

        public static Rgba FromArgb(byte a, byte r, byte g, byte b) { return new Rgba(a, r, g, b); }
        public static Rgba FromRgb(byte r, byte g, byte b) { return new Rgba(255, r, g, b); }

        public static readonly Rgba Gray = FromRgb(128, 128, 128);
        public static readonly Rgba SlateGray = FromRgb(112, 128, 144);
        public static readonly Rgba White = FromRgb(255, 255, 255);
        public static readonly Rgba Black = FromRgb(0, 0, 0);

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "black", "#000000" }, { "white", "#FFFFFF" }, { "gray", "#808080" }, { "grey", "#808080" }, { "silver", "#C0C0C0" },
            { "red", "#FF0000" }, { "crimson", "#DC143C" }, { "tomato", "#FF6347" }, { "coral", "#FF7F50" }, { "salmon", "#FA8072" },
            { "orange", "#FFA500" }, { "gold", "#FFD700" }, { "yellow", "#FFFF00" }, { "olive", "#808000" }, { "lime", "#00FF00" },
            { "green", "#008000" }, { "teal", "#008080" }, { "turquoise", "#40E0D0" }, { "cyan", "#00FFFF" }, { "skyblue", "#87CEEB" },
            { "blue", "#0000FF" }, { "navy", "#000080" }, { "indigo", "#4B0082" }, { "purple", "#800080" }, { "violet", "#EE82EE" },
            { "magenta", "#FF00FF" }, { "pink", "#FFC0CB" }, { "brown", "#A52A2A" }, { "maroon", "#800000" }, { "slategray", "#708090" },
        };

        /// <summary>"#RGB", "#RRGGBB", "#AARRGGBB" or a common colour name; anything else is gray.</summary>
        public static Rgba Hex(string text)
        {
            Rgba c;
            return TryParse(text, out c) ? c : Gray;
        }

        public static bool TryParse(string text, out Rgba color)
        {
            color = Gray;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim();
            string named;
            if (Names.TryGetValue(s, out named)) s = named;
            if (!s.StartsWith("#")) return false;
            s = s.Substring(1);
            if (s.Length == 3 || s.Length == 4)
            {
                // #RGB / #ARGB → doubled digits
                var sb = new System.Text.StringBuilder();
                foreach (char ch in s) sb.Append(ch).Append(ch);
                s = sb.ToString();
            }
            uint v;
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            if (s.Length == 6) { color = FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v); return true; }
            if (s.Length == 8) { color = FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v); return true; }
            return false;
        }

        public Rgba Mix(Rgba other, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return FromArgb(
                (byte)(A + (other.A - A) * t),
                (byte)(R + (other.R - R) * t),
                (byte)(G + (other.G - G) * t),
                (byte)(B + (other.B - B) * t));
        }

        public Rgba Lighten(double t) { return Mix(FromArgb(A, 255, 255, 255), t); }
        public Rgba Darken(double t) { return Mix(FromArgb(A, 0, 0, 0), t); }
        public Rgba WithAlpha(double alpha) { return FromArgb((byte)Math.Max(0, Math.Min(255, alpha * 255)), R, G, B); }

        public static Rgba Hsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        /// <summary>Stable pleasant colour for an unknown provider id.</summary>
        public static Rgba FromId(string id)
        {
            int h = 0;
            foreach (char ch in id ?? "") h = h * 31 + ch;
            return Hsl(Math.Abs(h % 360), 0.62, 0.58);
        }

        /// <summary>Traffic-light colour for a remaining percentage.</summary>
        public static Rgba Level(double remaining)
        {
            if (remaining >= 50) return Hex("#4ADE80");
            if (remaining >= 20) return Hex("#FBBF24");
            return Hex("#F87171");
        }

        public string ToHex()
        {
            return (A == 255 ? "#" : "#" + A.ToString("X2")) + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
        }

        public bool Equals(Rgba o) { return A == o.A && R == o.R && G == o.G && B == o.B; }
        public override bool Equals(object obj) { return obj is Rgba && Equals((Rgba)obj); }
        public override int GetHashCode() { return (A << 24) | (R << 16) | (G << 8) | B; }
        public override string ToString() { return ToHex(); }
        public static bool operator ==(Rgba a, Rgba b) { return a.Equals(b); }
        public static bool operator !=(Rgba a, Rgba b) { return !a.Equals(b); }
    }
}
