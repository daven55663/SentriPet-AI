using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SentriPet
{
    /// <summary>5×7 bitmap font for the retro theme.</summary>
    static class PixelFont
    {
        static readonly Dictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
        {
            { '0', new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E } }, { '1', new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E } },
            { '2', new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F } }, { '3', new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E } },
            { '4', new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 } }, { '5', new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E } },
            { '6', new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E } }, { '7', new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 } },
            { '8', new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E } }, { '9', new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C } },
            { 'A', new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11 } }, { 'B', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E } },
            { 'C', new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E } }, { 'D', new byte[] { 0x1C, 0x12, 0x11, 0x11, 0x11, 0x12, 0x1C } },
            { 'E', new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F } }, { 'F', new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 } },
            { 'G', new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F } }, { 'H', new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 } },
            { 'I', new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E } }, { 'J', new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C } },
            { 'K', new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 } }, { 'L', new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F } },
            { 'M', new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 } }, { 'N', new byte[] { 0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11 } },
            { 'O', new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E } }, { 'P', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 } },
            { 'Q', new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D } }, { 'R', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 } },
            { 'S', new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E } }, { 'T', new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 } },
            { 'U', new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E } }, { 'V', new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 } },
            { 'W', new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A } }, { 'X', new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 } },
            { 'Y', new byte[] { 0x11, 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04 } }, { 'Z', new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F } },
            { ' ', new byte[] { 0, 0, 0, 0, 0, 0, 0 } }, { '.', new byte[] { 0, 0, 0, 0, 0, 0x0C, 0x0C } },
            { ':', new byte[] { 0, 0x0C, 0x0C, 0, 0x0C, 0x0C, 0 } }, { '/', new byte[] { 0, 0x01, 0x02, 0x04, 0x08, 0x10, 0 } },
            { '%', new byte[] { 0x18, 0x19, 0x02, 0x04, 0x08, 0x13, 0x03 } }, { '-', new byte[] { 0, 0, 0, 0x1F, 0, 0, 0 } },
            { '+', new byte[] { 0, 0x04, 0x04, 0x1F, 0x04, 0x04, 0 } }, { '!', new byte[] { 0x04, 0x04, 0x04, 0x04, 0, 0, 0x04 } },
            { '?', new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0, 0x04 } }, { '(', new byte[] { 0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02 } },
            { ')', new byte[] { 0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08 } }, { '<', new byte[] { 0x02, 0x04, 0x08, 0x10, 0x08, 0x04, 0x02 } },
            { '>', new byte[] { 0x08, 0x04, 0x02, 0x01, 0x02, 0x04, 0x08 } }, { '_', new byte[] { 0, 0, 0, 0, 0, 0, 0x1F } },
            { '=', new byte[] { 0, 0, 0x1F, 0, 0x1F, 0, 0 } }, { '*', new byte[] { 0, 0x04, 0x15, 0x0E, 0x15, 0x04, 0 } },
            { '~', new byte[] { 0, 0, 0x08, 0x15, 0x02, 0, 0 } }, { '#', new byte[] { 0x0A, 0x0A, 0x1F, 0x0A, 0x1F, 0x0A, 0x0A } },
            { '▼', new byte[] { 0, 0x1F, 0x1F, 0x0E, 0x0E, 0x04, 0 } },
        };

        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();

        public static BitmapSource Render(string text, Color color)
        {
            text = (text ?? "").ToUpperInvariant();
            string key = text + "|" + color;
            BitmapSource hit;
            if (cache.TryGetValue(key, out hit)) return hit;
            if (cache.Count > 400) cache.Clear();
            int w = Math.Max(1, text.Length * 6 - 1), h = 7;
            var px = new int[w * h];
            int argb = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
            for (int i = 0; i < text.Length; i++)
            {
                byte[] g;
                if (!Glyphs.TryGetValue(text[i], out g)) g = Glyphs['?'];
                for (int y = 0; y < 7; y++)
                    for (int x = 0; x < 5; x++)
                        if ((g[y] & (0x10 >> x)) != 0) px[y * w + i * 6 + x] = argb;
            }
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, w, h), px, w * 4, 0);
            bmp.Freeze();
            cache[key] = bmp;
            return bmp;
        }

        /// <summary>Crisp pixel text with a 1-pixel drop shadow.</summary>
        public class Label : Grid
        {
            readonly Image shadow = new Image(), main = new Image();
            readonly double scale;
            string text;
            Color color;

            public Label(string text, Color color, double scale)
            {
                this.scale = scale;
                shadow.Margin = new Thickness(scale, scale, 0, 0);
                main.Margin = new Thickness(0, 0, scale, scale);
                Children.Add(shadow);
                Children.Add(main);
                Set(text, color);
            }

            public void Set(string t, Color c)
            {
                if (t == text && c == color) return;
                text = t;
                color = c;
                var src = Render(t, c);
                var sh = Render(t, Color.FromRgb(0x10, 0x10, 0x28));
                main.Source = src;
                shadow.Source = sh;
                main.Width = shadow.Width = src.PixelWidth * scale;
                main.Height = shadow.Height = src.PixelHeight * scale;
            }
        }
    }

    /// <summary>Classic JRPG window: black outline, white border, blue gradient.</summary>
    class PixelFrame : FrameworkElement
    {
        public double P = 2;

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, p = P;
            if (w < p * 6 || h < p * 6) return;
            var bg = new LinearGradientBrush(Palette.Hex("#2F4BC2"), Palette.Hex("#0C1660"), 90);
            var white = Palette.Brush("#F4F4F8");
            var dark = Palette.Brush("#0A0A18");
            var gray = Palette.Brush("#8C94B8");
            dc.DrawRectangle(bg, null, new Rect(p * 2, p * 2, w - p * 4, h - p * 4));
            // outer outline
            dc.DrawRectangle(dark, null, new Rect(p * 2, 0, w - p * 4, p));
            dc.DrawRectangle(dark, null, new Rect(p * 2, h - p, w - p * 4, p));
            dc.DrawRectangle(dark, null, new Rect(0, p * 2, p, h - p * 4));
            dc.DrawRectangle(dark, null, new Rect(w - p, p * 2, p, h - p * 4));
            dc.DrawRectangle(dark, null, new Rect(p, p, p, p));
            dc.DrawRectangle(dark, null, new Rect(w - p * 2, p, p, p));
            dc.DrawRectangle(dark, null, new Rect(p, h - p * 2, p, p));
            dc.DrawRectangle(dark, null, new Rect(w - p * 2, h - p * 2, p, p));
            // white frame
            dc.DrawRectangle(white, null, new Rect(p * 2, p, w - p * 4, p));
            dc.DrawRectangle(white, null, new Rect(p * 2, h - p * 2, w - p * 4, p));
            dc.DrawRectangle(white, null, new Rect(p, p * 2, p, h - p * 4));
            dc.DrawRectangle(white, null, new Rect(w - p * 2, p * 2, p, h - p * 4));
            // inner bevel
            dc.DrawRectangle(gray, null, new Rect(p * 2, p * 2, w - p * 4, p));
            dc.DrawRectangle(gray, null, new Rect(p * 2, p * 2, p, h - p * 4));
        }
    }

    /// <summary>16×16 slime sprites with a hat per mascot.</summary>
    static class Sprites
    {
        static readonly string[] Slime =
        {
            "................",
            "................",
            "................",
            "................",
            "......KKKK......",
            "....KKLLLLKK....",
            "...KLLWWLLLLK...",
            "..KLLWLLLLLLBK..",
            "..KLLLLLLLLLBK..",
            ".KBLLELLLLELBBK.",
            ".KBBLELLLLEBBBK.",
            ".KBBBBBMMBBBBBK.",
            ".KDBBBBBBBBBBDK.",
            "..KDDBBBBBBDDK..",
            "...KKKKKKKKKK...",
            "................",
        };

        static readonly Dictionary<string, string[]> Hats = new Dictionary<string, string[]>
        {
            { "sparkle", new[] {
                "........P.......",
                ".......PP.......",
                "......PPPY......",
                ".....PPPPP......",
                "...PPPPPPPPP....", } },
            { "prompt", new[] {
                "........RR......",
                ".......RR.......",
                "......SSSS......",
                ".....SSSSSS.....",
                "....SSSSSSSS....",
                "...SSSSSSSSSS...",
                "...TTTTTTTTTT...", } },
            { "goggles", new[] {
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "................",
                "..KGGKKKKKGGK...",
                "..KGGK....KGGK..", } },
            { "llama", new[] {
                "................",
                "....KK....KK....",
                "....KLK..KLK....",
                "....KLK..KLK....",
                ".....K....K.....", } },
            { "cat", new[] {
                "................",
                "................",
                "...K........K...",
                "...KK......KK...",
                "...KLK....KLK...", } },
            { "antenna", new[] {
                "................",
                "........Y.......",
                "........K.......",
                "........K.......", } },
        };

        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();

        /// <param name="face">normal | sleep | hurt</param>
        public static BitmapSource Get(Color body, string mascot, int frame, string face)
        {
            string key = body + mascot + frame + face;
            BitmapSource hit;
            if (cache.TryGetValue(key, out hit)) return hit;
            var map = new Dictionary<char, Color>
            {
                { 'K', Palette.Darken(body, 0.7) }, { 'B', body }, { 'L', Palette.Lighten(body, 0.35) }, { 'D', Palette.Darken(body, 0.3) },
                { 'W', Colors.White }, { 'E', Palette.Hex("#101018") }, { 'M', Palette.Darken(body, 0.6) },
                { 'P', Palette.Hex("#5B3FB0") }, { 'Y', Palette.Hex("#FFE14D") }, { 'R', Palette.Hex("#E8403A") },
                { 'S', Palette.Hex("#C3CAD6") }, { 'T', Palette.Hex("#7D8699") }, { 'G', Palette.Hex("#9FE3FF") }, { 'Q', Palette.Hex("#7CC8FF") },
            };
            var grid = Slime.Select(r => r.ToCharArray()).ToArray();
            if (face == "sleep")
            {
                grid[9][5] = 'L'; grid[9][10] = 'L';
                grid[10][4] = 'E'; grid[10][5] = 'E'; grid[10][10] = 'E'; grid[10][11] = 'E';
                grid[11][7] = 'B'; grid[11][8] = 'M';
            }
            else if (face == "hurt")
            {
                grid[6][13] = 'Q'; grid[7][14] = 'Q'; grid[8][14] = 'Q';
                grid[11][7] = 'E'; grid[11][8] = 'E';
            }
            string[] hat;
            if (mascot != null && Hats.TryGetValue(mascot, out hat))
                for (int y = 0; y < hat.Length; y++)
                    for (int x = 0; x < 16; x++)
                        if (hat[y][x] != '.') grid[y][x] = hat[y][x];

            var px = new int[16 * 16];
            int shift = frame == 1 ? 1 : 0;
            for (int y = 0; y < 16; y++)
            {
                int sy = y - shift;
                if (sy < 0) continue;
                for (int x = 0; x < 16; x++)
                {
                    Color c;
                    if (!map.TryGetValue(grid[sy][x], out c)) continue;
                    px[y * 16 + x] = (255 << 24) | (c.R << 16) | (c.G << 8) | c.B;
                }
            }
            var bmp = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, 16, 16), px, 64, 0);
            bmp.Freeze();
            cache[key] = bmp;
            return bmp;
        }
    }

    /// <summary>JRPG party status screen: HP = most constrained window, MP = the next one.</summary>
    class PixelTheme : Theme
    {
        const double P = 2;
        static readonly Color Gold = Palette.Hex("#F8D878");

        class BarUi
        {
            public string Key;
            public Rectangle Fill, Shine;
            public PixelFont.Label Val;
            public Anim W = new Anim(0);
            public int Urgent;              // "use it before it resets" level: the bar blinks
        }

        class Member
        {
            public string Id;
            public Image Sprite;
            public PixelFont.Label Name, Lv, Reset, Zz;
            public TextBlock Status;
            public List<BarUi> Bars = new List<BarUi>();
            public string Face = "normal";
            public Color Color;
            public string Mascot;
        }

        StackPanel root, party;
        TextBlock dialog;
        PixelFont.Label arrow;
        readonly List<Member> members = new List<Member>();
        string message = "";
        double messageStart, nextMessage;
        int messageIdx;

        public override string Id { get { return "pixel"; } }
        public override string Name { get { return "像素勇者"; } }
        public override string Mood { get { return "想打電動"; } }
        public override string Blurb { get { return "RPG 狀態列，HP/MP 就是你的額度"; } }

        protected override FrameworkElement CreateRoot()
        {
            root = new StackPanel { Margin = new Thickness(10), UseLayoutRounding = true, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(root, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(root, EdgeMode.Aliased);
            TextOptions.SetTextRenderingMode(root, TextRenderingMode.Aliased);
            TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);

            var top = new Grid();
            top.Children.Add(new PixelFrame { P = P });
            party = new StackPanel { Margin = new Thickness(14, 12, 16, 12) };
            top.Children.Add(party);
            root.Children.Add(top);

            var bottom = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            bottom.Children.Add(new PixelFrame { P = P });
            var dlg = new Grid { Margin = new Thickness(14, 10, 14, 10) };
            dialog = new TextBlock { FontFamily = G.Ui, FontSize = 12, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 250, MinHeight = 16, Margin = new Thickness(0, 0, 14, 0) };
            dlg.Children.Add(dialog);
            arrow = new PixelFont.Label("▼", Gold, P) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            dlg.Children.Add(arrow);
            bottom.Children.Add(dlg);
            root.Children.Add(bottom);
            return root;
        }

        protected override void Rebuild()
        {
            party.Children.Clear();
            members.Clear();
            if (Views.Count == 0)
            {
                party.Children.Add(new PixelFont.Label("SEARCHING...", Colors.White, P));
                return;
            }
            for (int i = 0; i < Views.Count; i++)
            {
                if (i > 0) party.Children.Add(new Rectangle { Height = P, Fill = G.B(Colors.White, 0.25), Margin = new Thickness(0, 8, 0, 8) });
                party.Children.Add(MakeMember(Views[i]));
            }
            nextMessage = Time;
        }

        static string Level(string plan)
        {
            string p = (plan ?? "").ToLowerInvariant();
            if (p.Contains("max")) return "LV99";
            if (p.Contains("enterprise")) return "LV70";
            if (p.Contains("pro")) return "LV50";
            if (p.Contains("business") || p.Contains("team")) return "LV40";
            if (p.Contains("plus")) return "LV20";
            if (p.Contains("edu")) return "LV15";
            if (p.Contains("go")) return "LV5";
            if (p.Contains("free")) return "LV1";
            if (p.Contains("本機") || p.Contains("local")) return "LV**";
            return "LV??";
        }

        static string Ascii(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (s ?? "").ToUpperInvariant()) sb.Append(c < 128 ? c : '?');
            return sb.ToString();
        }

        FrameworkElement MakeMember(ProviderView v)
        {
            var m = new Member { Id = v.Id, Color = v.HasData ? v.Color : G.Desaturate(v.Color, 0.7), Mascot = v.Mascot };
            var grid = new Grid { Tag = "pv:" + v.Id, Background = G.B(Colors.Black, 0.001) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var spriteBox = new Grid { Width = 36, Height = 40, VerticalAlignment = VerticalAlignment.Top };
            spriteBox.Children.Add(new Rectangle { Width = 24, Height = 4, Fill = G.B(Colors.Black, 0.35), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) });
            m.Sprite = new Image { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
            spriteBox.Children.Add(m.Sprite);
            m.Zz = new PixelFont.Label("Z", Palette.Hex("#A8C8FF"), P) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
            spriteBox.Children.Add(m.Zz);
            grid.Children.Add(spriteBox);

            var info = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            Grid.SetColumn(info, 1);
            var nameRow = new Grid();
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            m.Name = new PixelFont.Label(Ascii(v.Name), Colors.White, P);
            m.Name.HorizontalAlignment = HorizontalAlignment.Left;
            nameRow.Children.Add(m.Name);
            m.Lv = new PixelFont.Label(Level(v.Plan), Gold, P) { Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(m.Lv, 1);
            nameRow.Children.Add(m.Lv);
            info.Children.Add(nameRow);

            if (v.HasData)
            {
                int i = 0;
                foreach (var meter in v.Meters.Take(3))
                {
                    string label = i == 0 ? "HP" : i == 1 ? "MP" : "SP";
                    var b = new BarUi { Key = meter.Key };
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
                    row.Children.Add(new PixelFont.Label(label, Gold, P) { Width = 26, VerticalAlignment = VerticalAlignment.Center });
                    var bar = new Canvas { Width = 104, Height = 10, VerticalAlignment = VerticalAlignment.Center };
                    bar.Children.Add(new Rectangle { Width = 104, Height = 10, Fill = G.B(Palette.Hex("#0A0A18")) });
                    bar.Children.Add(Place(new Rectangle { Width = 100, Height = 6, Fill = G.B(Palette.Hex("#2A3050")) }, 2, 2));
                    b.Fill = Place(new Rectangle { Width = 0, Height = 6 }, 2, 2);
                    b.Shine = Place(new Rectangle { Width = 0, Height = 2, Fill = G.B(Colors.White, 0.45) }, 2, 2);
                    bar.Children.Add(b.Fill);
                    bar.Children.Add(b.Shine);
                    row.Children.Add(bar);
                    b.Val = new PixelFont.Label("", Colors.White, P) { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    row.Children.Add(b.Val);
                    info.Children.Add(row);
                    m.Bars.Add(b);
                    b.W.Value = b.W.Target = meter.Remaining;
                    i++;
                }
            }
            var st = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            m.Status = new TextBlock { FontFamily = G.Ui, FontSize = 12, Foreground = Brushes.White, Margin = new Thickness(0, -2, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(m.Status);
            m.Reset = new PixelFont.Label("", Palette.Hex("#A8C8FF"), P) { VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(m.Reset);
            info.Children.Add(st);
            grid.Children.Add(info);
            members.Add(m);
            return grid;
        }

        static Rectangle Place(Rectangle r, double x, double y)
        {
            G.Place(r, x, y);
            return r;
        }

        static Color HpColor(double rem, int index)
        {
            if (index == 1) return rem >= 20 ? Palette.Hex("#58A8F8") : Palette.Hex("#F83838");
            if (rem >= 50) return Palette.Hex("#48D860");
            if (rem >= 20) return Palette.Hex("#F8D030");
            return Palette.Hex("#F83838");
        }

        protected override void Refresh()
        {
            foreach (var m in members)
            {
                var v = View(m.Id);
                if (v == null) continue;
                string status;
                Color sc;
                if (!v.HasData) { status = "迷路中"; sc = Palette.Hex("#A0A0B0"); }
                else if (v.Active) { status = "戰鬥中"; sc = Palette.Hex("#FFA040"); }
                else if (v.Mood == SentriPet.Mood.Empty) { status = "睡眠"; sc = Palette.Hex("#A8C8FF"); }
                else if (v.Mood == SentriPet.Mood.Critical) { status = "瀕死"; sc = Palette.Hex("#FF5050"); }
                else if (v.Mood == SentriPet.Mood.Worried) { status = "疲勞"; sc = Palette.Hex("#F8D030"); }
                else { status = "正常"; sc = Colors.White; }
                if (v.Stale) status += "·舊";
                m.Status.Text = status;
                m.Status.Foreground = G.B(sc);
                m.Face = !v.HasData ? "normal" : v.Mood == SentriPet.Mood.Empty ? "sleep" : v.Mood == SentriPet.Mood.Critical ? "hurt" : "normal";
                var p = v.ResetMeter;
                m.Reset.Set(!v.HasData ? "" : v.Unlimited ? "NO LIMIT" : p != null && p.ResetsAt.HasValue ? (v.Mood == SentriPet.Mood.Empty ? "REVIVE " : "RST ") + Fmt.Clock(p.ResetsAt).Replace(" ", "") : "IDLE", Palette.Hex("#A8C8FF"));
                int i = 0;
                foreach (var b in m.Bars)
                {
                    var meter = v.Meters.FirstOrDefault(x => x.Key == b.Key);
                    if (meter == null) continue;
                    double rem = meter.Unlimited ? 100 : meter.Remaining;
                    b.W.Target = rem;
                    b.Fill.Fill = G.B(HpColor(rem, i));
                    b.Urgent = meter == v.UseIt ? v.UseItLevel : 0;
                    if (b.Urgent == 0) b.Fill.Opacity = b.Shine.Opacity = 1;
                    b.Val.Set(meter.Unlimited ? "INF" : ((int)Math.Round(rem)).ToString().PadLeft(3) + "/100", rem < 20 ? Palette.Hex("#FF7070") : Colors.White);
                    i++;
                }
            }
        }

        string NextLine()
        {
            if (Views.Count == 0) return "勇者正在尋找夥伴…";
            var v = Views[messageIdx++ % Views.Count];
            string n = v.Name.ToUpperInvariant();
            if (!v.HasData) return n + " 迷失在迷霧中…（" + (v.Error ?? "沒有資料") + "）";
            if (v.Active) return n + " 正在戰鬥中！";
            if (v.UseItLevel >= 2 && v.UseIt != null)
                return n + " 的魔力還剩 " + Fmt.Pct(v.UseIt.Remaining) + "，" + Fmt.Countdown(v.UseIt.ResetsAt) + "後就會消失！快施放大絕招！";
            if (v.UseItLevel == 1 && v.UseIt != null && messageIdx % 2 == 0)
                return n + " 的" + v.UseIt.Label + "魔力還有 " + Fmt.Pct(v.UseIt.Remaining) + "，" + Fmt.When(v.UseIt.ResetsAt) + " 前用掉吧！";
            switch (v.Mood)
            {
                case SentriPet.Mood.Great: return n + " 的 HP 還有 " + Fmt.Pct(v.HeadlineRemaining) + "！精神百倍！";
                case SentriPet.Mood.Good: return n + " 的 HP 還有 " + Fmt.Pct(v.HeadlineRemaining) + "。";
                case SentriPet.Mood.Worried: return n + " 看起來有點累了…（HP " + Fmt.Pct(v.HeadlineRemaining) + "）";
                case SentriPet.Mood.Critical: return n + " 瀕死！快找地方休息！";
                case SentriPet.Mood.Empty: return n + " 睡著了… " + (v.Primary != null ? Fmt.Countdown(v.Primary.ResetsAt) + "後復活" : "");
            }
            return n + " 準備就緒。";
        }

        public override bool Say(string providerId, string text)
        {
            message = text ?? "";
            messageStart = Time;
            nextMessage = Time + 8;
            return true;
        }

        public override bool Poke(string providerId)
        {
            var v = View(providerId);
            if (v == null) return false;
            Say(providerId, Lines.Poke(v, Rng));
            return true;
        }

        public override void Tick(double dt)
        {
            base.Tick(dt);
            if (Time >= nextMessage)
            {
                message = NextLine();
                messageStart = Time;
                nextMessage = Time + 7;
            }
            int shown = Math.Min(message.Length, (int)((Time - messageStart) * 28));
            string t = message.Substring(0, shown);
            if (dialog.Text != t) dialog.Text = t;
            arrow.Visibility = shown >= message.Length && (int)(Time * 2.5) % 2 == 0 ? Visibility.Visible : Visibility.Hidden;

            foreach (var m in members)
            {
                var v = View(m.Id);
                bool active = v != null && v.Active;
                int frame = (int)(Time / (active ? 0.18 : 0.45)) % 2;
                m.Sprite.Source = Sprites.Get(m.Color, m.Mascot, frame, m.Face);
                m.Sprite.Opacity = m.Face == "hurt" && (int)(Time * 5) % 4 == 0 ? 0.35 : 1;
                m.Zz.Visibility = m.Face == "sleep" ? Visibility.Visible : Visibility.Collapsed;
                if (m.Face == "sleep") m.Zz.Margin = new Thickness(0, -2 - ((int)(Time * 2) % 3) * 2, -4, 0);
                foreach (var b in m.Bars)
                {
                    b.W.Step(dt, 4);
                    double w = Math.Round(Math.Max(0, Math.Min(100, b.W.Value)) / P) * P;
                    b.Fill.Width = w;
                    b.Shine.Width = w;
                    // a quota that expires unused soon blinks, in hard pixel steps
                    if (b.Urgent > 0) b.Fill.Opacity = b.Shine.Opacity = G.UrgentPulse(b.Urgent, Time) > 0.6 ? 1 : 0.25;
                }
            }
        }
    }
}
