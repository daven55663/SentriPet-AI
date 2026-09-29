using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SentriPet
{
    /// <summary>
    /// --demo FILE.gif [--lang xx] [--scale 1]: the animated tour for the README and for posts — the jelly pets
    /// (normal, then with quota about to reset unused), then the other looks — drawn off-screen with the sample
    /// data like --snapshot, and saved as a GIF (a palette per scene, only the changed pixels per frame).
    /// --social-card FILE.png: the repository's social preview (1280 × 640, English).
    /// </summary>
    static class Demo
    {
        const int Width = 720, Height = 460, Fps = 10;

        /// <summary>A theme host whose cursor drifts over the stage, so the pets' eyes follow it.</summary>
        class DemoHost : IThemeHost
        {
            readonly AppSettings settings = new AppSettings { Chatty = false };
            readonly Random rng = new Random(7);
            public Control Stage;
            public double Time;
            public AppSettings Settings { get { return settings; } }
            public Random Rng { get { return rng; } }
            public void SaveSettings() { }
            public Point? CursorIn(Visual element)
            {
                if (Stage == null) return null;
                var p = new Point(Width / 2 + Math.Sin(Time * 0.9) * 250, 70 + Math.Sin(Time * 1.7) * 40);
                try { return Stage.TranslatePoint(p, element); }
                catch { return null; }
            }
        }

        class Scene
        {
            public string ThemeId, Caption;
            public List<ProviderView> Views;
            public bool UseIt;
            public double Seconds;
        }

        public static int Run(string[] args)
        {
            string file = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "sentripet-demo.gif");
            double scale = 1;
            double s;
            int si = Array.IndexOf(args, "--scale");
            if (si >= 0 && si + 1 < args.Length &&
                double.TryParse(args[si + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s)) scale = s;
            string dir = Path.GetDirectoryName(Path.GetFullPath(file));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            AppBuilder.Configure<DesktopApp>()
                .UseSkia()
                .UseHarfBuzz()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            var scenes = new List<Scene>
            {
                new Scene { ThemeId = "pet", Views = Calm(), Seconds = 3.4, Caption = L.T("AI 額度還剩多少，看桌寵就知道") },
                new Scene { ThemeId = "pet", Views = MockData.C(), Seconds = 3.4, UseIt = true, Caption = L.T("額度快重置了還沒用完？牠們會提醒你") },
            };
            foreach (var t in ThemeCatalog.All)
                if (t.Id != "pet")
                    scenes.Add(new Scene { ThemeId = t.Id, Views = Calm(), Seconds = 1.7, Caption = L.F("{0} · {1}", t.Name, t.Mood) });

            int w = (int)Math.Round(Width * scale), h = (int)Math.Round(Height * scale);
            var gif = new GifWriter(w, h);
            foreach (var scene in scenes)
            {
                var frames = new List<byte[]>();
                var host = new DemoHost();
                var theme = ThemeCatalog.Get(scene.ThemeId).Create();
                theme.Attach(host);
                theme.Update(scene.Views);
                for (int i = 0; i < 45; i++) { host.Time += 1 / 30.0; theme.Tick(1 / 30.0); }
                var first = scene.Views[0];
                theme.Say(first.Id, scene.UseIt ? Lines.UseIt(first, new Random(4)) : Lines.Poke(first, new Random(3)));
                for (int i = 0; i < 9; i++) { host.Time += 1 / 30.0; theme.Tick(1 / 30.0); }
                // as large as fits (at most 1.5×), the same for the whole scene
                theme.Root.Measure(Size.Infinity);
                var size = theme.Root.DesiredSize;
                double zoom = Math.Min(1.5, Math.Min((Width - 48) / size.Width, (Height - 110) / size.Height));
                var stage = Stage(theme.Root, zoom, scene.Caption);
                host.Stage = stage;
                int count = (int)Math.Round(scene.Seconds * Fps);
                for (int f = 0; f < count; f++)
                {
                    for (int i = 0; i < 3; i++) { host.Time += 1.0 / (3 * Fps); theme.Tick(1.0 / (3 * Fps)); }
                    frames.Add(Pixels(stage, w, h, scale));
                }
                gif.AddScene(frames, 100 / Fps);
                Console.WriteLine(scene.ThemeId + ": " + count + " frames");
            }
            gif.Save(file);
            Console.WriteLine(file + "  " + new FileInfo(file).Length / 1024 + " KB");
            return 0;
        }

        static Meter M(string key, string label, string shortLabel, double used, double hours, bool approx, int window)
        {
            return new Meter { Key = key, Label = label, ShortLabel = shortLabel, Used = used, ResetsAt = DateTime.UtcNow.AddHours(hours), ResetApprox = approx, WindowMinutes = window };
        }

        /// <summary>An ordinary day: nothing about to expire unused, Codex at work.</summary>
        static List<ProviderView> Calm()
        {
            var claude = new Snapshot { Source = L.T("Claude 桌面版快取"), ObservedAt = DateTime.UtcNow.AddMinutes(-4) };
            claude.Meters.Add(M("fh", L.T("5 小時"), "5h", 34, 2.2, true, 300));
            claude.Meters.Add(M("sd", L.T("每週"), L.T("週"), 41, 110, true, 10080));
            var codex = new Snapshot { Source = L.T("Codex 官方 app-server"), ObservedAt = DateTime.UtcNow, Plan = "Plus", Active = true };
            codex.Meters.Add(M("codex:300", L.T("5 小時"), "5h", 12, 4.1, false, 300));
            codex.Meters.Add(M("codex:10080", L.T("每週"), L.T("週"), 27, 130, false, 10080));
            var copilot = new Snapshot { Source = L.T("Copilot CLI 快取"), ObservedAt = DateTime.UtcNow, Plan = "Pro" };
            copilot.Meters.Add(new Meter { Key = "premium_interactions", Label = L.T("進階請求"), ShortLabel = "PR", Used = 35, ResetsAt = DateTime.UtcNow.AddDays(19), WindowMinutes = 43200, ValueText = "130 / 200" });
            return new List<ProviderView>
            {
                UsageService.MakeView(new ClaudeProvider(), claude),
                UsageService.MakeView(new CodexProvider(), codex),
                UsageService.MakeView(new CopilotProvider(), copilot),
            };
        }

        public static int SocialCard(string[] args)
        {
            string file = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "sentripet-social.png");
            AppBuilder.Configure<DesktopApp>()
                .UseSkia()
                .UseHarfBuzz()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            var host = new DemoHost();
            var theme = ThemeCatalog.Get("pet").Create();
            theme.Attach(host);
            var views = Calm();
            theme.Update(views);
            for (int i = 0; i < 45; i++) { host.Time += 1 / 30.0; theme.Tick(1 / 30.0); }

            const int W = 1280, H = 640;
            var card = new Grid { Width = W, Height = H, Background = Wallpaper() };
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RenderOptions.SetTextRenderingMode(card, TextRenderingMode.Antialias);
            var text = new StackPanel { Margin = new Thickness(72, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 18 };
            text.Children.Add(new TextBlock { Text = AppInfo.Name, FontFamily = G.Ui, FontSize = 76, FontWeight = FontWeight.Bold, Foreground = Brushes.White });
            text.Children.Add(new TextBlock
            {
                Text = "A desktop pet that shows how much of your AI plan is left — and nudges you to use it before it resets.",
                FontFamily = G.Ui, FontSize = 29, LineHeight = 40, TextWrapping = TextWrapping.Wrap,
                Foreground = Palette.Brush(Palette.A(Colors.White, 0.92)),
            });
            text.Children.Add(new TextBlock
            {
                Text = "Claude · Codex · Copilot · Ollama\nWindows · macOS · Linux · open source",
                FontFamily = G.Ui, FontSize = 22, LineHeight = 34, Margin = new Thickness(0, 8, 0, 0),
                Foreground = Palette.Brush(Palette.A(Colors.White, 0.6)),
            });
            card.Children.Add(text);
            var pets = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(1.55, 1.55),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 40, 0),
                Child = theme.Root,
            };
            Grid.SetColumn(pets, 1);
            card.Children.Add(pets);
            host.Stage = card;
            for (int i = 0; i < 6; i++) { host.Time += 1 / 30.0; theme.Tick(1 / 30.0); }

            var panel = new Panel();
            panel.Children.Add(card);
            panel.Measure(new Size(W, H));
            panel.Arrange(new Rect(0, 0, W, H));
            Dispatcher.UIThread.RunJobs();
            using (var rtb = new RenderTargetBitmap(new PixelSize(W, H), new Vector(96, 96)))
            {
                rtb.Render(panel);
                rtb.Save(file);
            }
            Console.WriteLine(file);
            return 0;
        }

        /// <summary>The theme on a sample wallpaper, zoomed and centred, with a caption under it.</summary>
        static Grid Stage(Control theme, double zoom, string caption)
        {
            var stage = new Grid { Width = Width, Height = Height, Background = Wallpaper() };
            RenderOptions.SetTextRenderingMode(stage, TextRenderingMode.Antialias);
            stage.Children.Add(new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(zoom, zoom),
                Margin = new Thickness(0, 0, 0, 44),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = theme,
            });
            stage.Children.Add(new TextBlock
            {
                Text = AppInfo.Name, FontFamily = G.Ui, FontSize = 13, FontWeight = FontWeight.SemiBold,
                Foreground = Palette.Brush(Palette.A(Colors.White, 0.55)), Margin = new Thickness(16, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            });
            stage.Children.Add(new Border
            {
                Background = Palette.Brush(Palette.A(Palette.Hex("#0B0E14"), 0.62)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 6, 16, 7),
                Margin = new Thickness(0, 0, 0, 20),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock { Text = caption, FontFamily = G.Ui, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
            });
            return stage;
        }

        static IBrush Wallpaper()
        {
            var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
            b.GradientStops.Add(new GradientStop(Palette.Hex("#34566A"), 0));
            b.GradientStops.Add(new GradientStop(Palette.Hex("#17232C"), 0.55));
            b.GradientStops.Add(new GradientStop(Palette.Hex("#5A4169"), 1));
            return b;
        }

        /// <summary>Renders the stage; returns the pixels as 0xRRGGBB.</summary>
        static byte[] Pixels(Control stage, int w, int h, double scale)
        {
            // a new parent each frame, like --snapshot (a reused, unrooted tree does not show the animation);
            // scaled by a transform at 96 dpi (see Snapshots.Draw)
            var old = stage.Parent as LayoutTransformControl;
            if (old != null) old.Child = null;
            var host = new Panel();
            host.Children.Add(new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = stage });
            host.Measure(new Size(w, h));
            host.Arrange(new Rect(0, 0, w, h));
            Dispatcher.UIThread.RunJobs();
            using (var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96)))
            {
                rtb.Render(host);
                return Snapshots.Bgra(rtb, new PixelRect(0, 0, w, h));   // the stage is opaque: premultiplied or not makes no difference
            }
        }
    }

    /// <summary>
    /// A small animated-GIF writer: each scene gets its own 255-colour palette (median cut, ordered dithering,
    /// so still areas stay the same from frame to frame), and each frame stores only the rectangle that changed,
    /// with unchanged pixels transparent. Loops forever.
    /// </summary>
    class GifWriter
    {
        const int Transparent = 255;

        class Frame { public int X, Y, W, H, Delay; public byte[] Indices; public byte[] Palette; }

        readonly int width, height;
        readonly int[] shown;   // what is on screen after the frames so far (0xRRGGBB, -1 = nothing yet)
        readonly List<Frame> frames = new List<Frame>();
        byte[] globalPalette;

        static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        public GifWriter(int width, int height)
        {
            this.width = width;
            this.height = height;
            shown = new int[width * height];
            for (int i = 0; i < shown.Length; i++) shown[i] = -1;
        }

        /// <summary>Adds the frames of one scene (BGRA pixels), each shown for the given time (1/100 s).</summary>
        public void AddScene(List<byte[]> bgra, int delay)
        {
            var palette = MedianCut(bgra, 255);
            if (globalPalette == null) globalPalette = palette;
            var lookup = new short[1 << 18];
            for (int i = 0; i < lookup.Length; i++) lookup[i] = -1;
            var index = new byte[width * height];
            var colour = new int[width * height];
            foreach (var px in bgra)
            {
                // quantize (ordered dithering: the same colour at the same place always gives the same index)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int p = y * width + x, o = p * 4;
                        int d = (Bayer[(y & 3) * 4 + (x & 3)] - 8) / 2;
                        int r = Clamp(px[o + 2] + d), g = Clamp(px[o + 1] + d), b = Clamp(px[o] + d);
                        int key = ((r >> 2) << 12) | ((g >> 2) << 6) | (b >> 2);
                        int k = lookup[key];
                        if (k < 0) lookup[key] = (short)(k = Nearest(palette, r, g, b));
                        index[p] = (byte)k;
                        colour[p] = (palette[k * 3] << 16) | (palette[k * 3 + 1] << 8) | palette[k * 3 + 2];
                    }
                // the rectangle that changed
                int x0 = width, y0 = height, x1 = -1, y1 = -1;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int p = y * width + x;
                        if (colour[p] == shown[p]) continue;
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
                if (x1 < 0)
                {
                    frames[frames.Count - 1].Delay += delay;   // nothing moved: show the last frame longer
                    continue;
                }
                var f = new Frame { X = x0, Y = y0, W = x1 - x0 + 1, H = y1 - y0 + 1, Delay = delay, Palette = palette };
                f.Indices = new byte[f.W * f.H];
                for (int y = 0; y < f.H; y++)
                    for (int x = 0; x < f.W; x++)
                    {
                        int p = (y0 + y) * width + x0 + x;
                        if (colour[p] == shown[p]) f.Indices[y * f.W + x] = Transparent;
                        else { f.Indices[y * f.W + x] = index[p]; shown[p] = colour[p]; }
                    }
                frames.Add(f);
            }
        }

        public void Save(string file)
        {
            using (var s = File.Create(file))
            {
                Write(s, "GIF89a");
                Short(s, width);
                Short(s, height);
                s.WriteByte(0xF7);   // global colour table, 256 entries
                s.WriteByte(0);
                s.WriteByte(0);
                s.Write(globalPalette, 0, 768);
                s.WriteByte(0x21); s.WriteByte(0xFF); s.WriteByte(11);
                Write(s, "NETSCAPE2.0");
                s.WriteByte(3); s.WriteByte(1); Short(s, 0); s.WriteByte(0);   // loop forever
                foreach (var f in frames)
                {
                    s.WriteByte(0x21); s.WriteByte(0xF9); s.WriteByte(4);
                    s.WriteByte((1 << 2) | 1);   // keep the frame (only changes follow), transparent index
                    Short(s, f.Delay);
                    s.WriteByte(Transparent);
                    s.WriteByte(0);
                    s.WriteByte(0x2C);
                    Short(s, f.X); Short(s, f.Y); Short(s, f.W); Short(s, f.H);
                    bool local = f.Palette != globalPalette;
                    s.WriteByte(local ? (byte)0x87 : (byte)0);
                    if (local) s.Write(f.Palette, 0, 768);
                    Lzw(s, f.Indices);
                }
                s.WriteByte(0x3B);
            }
        }

        static int Clamp(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        static int Nearest(byte[] pal, int r, int g, int b)
        {
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < 255; i++)
            {
                int dr = pal[i * 3] - r, dg = pal[i * 3 + 1] - g, db = pal[i * 3 + 2] - b;
                int d = 3 * dr * dr + 4 * dg * dg + 2 * db * db;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Median cut over a 5-bit-per-channel histogram of all the frames; returns 256 RGB entries.</summary>
        static byte[] MedianCut(List<byte[]> bgra, int colours)
        {
            var count = new long[1 << 15];
            var sum = new long[(1 << 15) * 3];
            foreach (var px in bgra)
                for (int o = 0; o < px.Length; o += 4)
                {
                    int bin = ((px[o + 2] >> 3) << 10) | ((px[o + 1] >> 3) << 5) | (px[o] >> 3);
                    count[bin]++;
                    sum[bin * 3] += px[o + 2];
                    sum[bin * 3 + 1] += px[o + 1];
                    sum[bin * 3 + 2] += px[o];
                }
            var all = new List<int>();
            for (int i = 0; i < count.Length; i++) if (count[i] > 0) all.Add(i);
            var boxes = new List<List<int>> { all };
            while (boxes.Count < colours)
            {
                // split the box with the most pixels × widest range
                int pick = -1, axis = 0;
                double score = 0;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Count < 2) continue;
                    long n = 0;
                    int[] lo = { 31, 31, 31 }, hi = { 0, 0, 0 };
                    foreach (int bin in boxes[i])
                    {
                        n += count[bin];
                        for (int c = 0; c < 3; c++)
                        {
                            int v = (bin >> (10 - 5 * c)) & 31;
                            if (v < lo[c]) lo[c] = v;
                            if (v > hi[c]) hi[c] = v;
                        }
                    }
                    for (int c = 0; c < 3; c++)
                    {
                        double sc = (double)n * (hi[c] - lo[c]);
                        if (sc > score) { score = sc; pick = i; axis = c; }
                    }
                }
                if (pick < 0) break;
                var box = boxes[pick];
                int shift = 10 - 5 * axis;
                box.Sort((a, b) => ((a >> shift) & 31).CompareTo((b >> shift) & 31));
                long total = 0, half = 0;
                foreach (int bin in box) total += count[bin];
                int cut = 1;
                for (int i = 0; i < box.Count - 1; i++)
                {
                    half += count[box[i]];
                    cut = i + 1;
                    if (half * 2 >= total) break;
                }
                boxes[pick] = box.GetRange(0, cut);
                boxes.Add(box.GetRange(cut, box.Count - cut));
            }
            var pal = new byte[768];
            for (int i = 0; i < 255; i++)
            {
                long n = 0, r = 0, g = 0, b = 0;
                foreach (int bin in boxes[i < boxes.Count ? i : 0]) { n += count[bin]; r += sum[bin * 3]; g += sum[bin * 3 + 1]; b += sum[bin * 3 + 2]; }
                pal[i * 3] = (byte)(r / n);   // (fewer colours than entries: the rest repeat the first)
                pal[i * 3 + 1] = (byte)(g / n);
                pal[i * 3 + 2] = (byte)(b / n);
            }
            return pal;
        }

        /// <summary>GIF's variable-length LZW (8-bit symbols, codes up to 12 bits, cleared when the table is full).</summary>
        static void Lzw(Stream s, byte[] data)
        {
            s.WriteByte(8);
            var block = new List<byte>();
            int bits = 0, nbits = 0;
            Action<int, int> emit = (code, size) =>
            {
                bits |= code << nbits;
                nbits += size;
                while (nbits >= 8)
                {
                    block.Add((byte)(bits & 0xFF));
                    bits >>= 8;
                    nbits -= 8;
                    if (block.Count == 255) { s.WriteByte(255); s.Write(block.ToArray(), 0, 255); block.Clear(); }
                }
            };
            const int clear = 256, end = 257;
            var table = new Dictionary<int, int>();
            int next = 258, codeSize = 9;
            emit(clear, codeSize);
            int prefix = data[0];
            for (int i = 1; i < data.Length; i++)
            {
                int k = data[i], key = (prefix << 8) | k, code;
                if (table.TryGetValue(key, out code)) { prefix = code; continue; }
                emit(prefix, codeSize);
                if (next < 4096)
                {
                    table[key] = next++;
                    if (next > (1 << codeSize) && codeSize < 12) codeSize++;
                }
                else
                {
                    emit(clear, codeSize);
                    table.Clear();
                    next = 258;
                    codeSize = 9;
                }
                prefix = k;
            }
            emit(prefix, codeSize);
            emit(end, codeSize);
            if (nbits > 0) block.Add((byte)(bits & 0xFF));
            if (block.Count > 0) { s.WriteByte((byte)block.Count); s.Write(block.ToArray(), 0, block.Count); }
            s.WriteByte(0);
        }

        static void Write(Stream s, string ascii) { foreach (char c in ascii) s.WriteByte((byte)c); }
        static void Short(Stream s, int v) { s.WriteByte((byte)(v & 0xFF)); s.WriteByte((byte)(v >> 8)); }
    }
}
