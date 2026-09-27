using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SentriPet
{
    /// <summary>
    /// --snapshot DIR [--theme id] [--scale 1.5] [--frames N]: renders every theme with the sample data (a, b, c)
    /// off-screen to PNG — works without a display, so CI checks the drawing on Windows, macOS and Linux.
    /// </summary>
    static class Snapshots
    {
        /// <summary>Theme host for off-screen rendering (fixed cursor, throwaway settings).</summary>
        public class PreviewHost : IThemeHost
        {
            readonly AppSettings settings = new AppSettings { Chatty = false };
            readonly Random rng = new Random(7);
            public AppSettings Settings { get { return settings; } }
            public Random Rng { get { return rng; } }
            public Point? CursorIn(Visual element) { return new Point(160, -40); }
            public void SaveSettings() { }
        }

        /// <summary>The settings page without a running app: sample data, a service that is never started.</summary>
        class SnapshotSettingsHost : ISettingsHost
        {
            readonly AppSettings settings = new AppSettings();
            UsageService service;
            public AppSettings Settings { get { return settings; } }
            public UsageService Service { get { return service ?? (service = new UsageService(settings)); } }
            public List<ProviderView> Views { get { return MockData.A(); } }
            public void ApplyWidgetSettings() { }
            public void ChangeTheme(string id) { }
            public void ChangeLanguage(string code) { }
            public void RefreshViews() { }
        }

        public static int Run(string[] args)
        {
            string outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "sentripet-shots");
            string only = Arg(args, "--theme");
            double scale = 1.5;
            double s;
            if (double.TryParse(Arg(args, "--scale") ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s)) scale = s;
            int frames;
            if (!int.TryParse(Arg(args, "--frames") ?? "", out frames)) frames = 0;
            Directory.CreateDirectory(outDir);

            AppBuilder.Configure<DesktopApp>()
                .UseSkia()
                .UseHarfBuzz()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            var sets = new List<KeyValuePair<string, List<ProviderView>>>
            {
                new KeyValuePair<string, List<ProviderView>>("a", MockData.A()),
                new KeyValuePair<string, List<ProviderView>>("b", MockData.B()),
                new KeyValuePair<string, List<ProviderView>>("c", MockData.C()),
            };
            int failures = 0;
            foreach (var info in ThemeCatalog.All)
            {
                if (only != null && info.Id != only) continue;
                Console.WriteLine("rendering " + info.Id);
                foreach (var set in sets)
                {
                    try
                    {
                        var theme = info.Create();
                        theme.Attach(new PreviewHost());
                        theme.Update(set.Value);
                        for (int i = 0; i < 45; i++) theme.Tick(1 / 30.0);
                        if (set.Value.Count > 0) theme.Say(set.Value[0].Id, Lines.Poke(set.Value[0], new Random(3)));
                        for (int i = 0; i < 12; i++) theme.Tick(1 / 30.0);
                        Render(theme.Root, Path.Combine(outDir, info.Id + "_" + set.Key + ".png"), scale, false);
                        Render(theme.Root, Path.Combine(outDir, info.Id + "_" + set.Key + "_desk.png"), scale, true);
                        for (int f = 1; f <= frames; f++)
                        {
                            for (int i = 0; i < 8; i++) theme.Tick(1 / 32.0);
                            Render(theme.Root, Path.Combine(outDir, info.Id + "_" + set.Key + "_f" + f + ".png"), scale, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        File.WriteAllText(Path.Combine(outDir, info.Id + "_" + set.Key + "_error.txt"), ex.ToString());
                    }
                }
            }
            // the hover card for the first provider of each set
            foreach (var set in sets)
            {
                if (set.Value.Count == 0 || only != null) continue;
                try
                {
                    var card = DetailCardView.Build(set.Value[0]);
                    card.SetPointer(DetailPlacement.Side.Above, 90);
                    Render(card.Root, Path.Combine(outDir, "detail_" + set.Key + ".png"), scale, true);
                }
                catch (Exception ex)
                {
                    failures++;
                    File.WriteAllText(Path.Combine(outDir, "detail_" + set.Key + "_error.txt"), ex.ToString());
                }
            }
            // the settings page (with a stand-in for the app)
            if (only == null)
            {
                try
                {
                    // templated controls (switches, sliders, buttons) get their look only inside a window
                    var win = new SettingsWindow(new SnapshotSettingsHost()) { Width = 700, Height = 900 };
                    win.Show();
                    win.RenderPreviews();
                    for (int i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
                    var page = win.Page;
                    var size = page.Bounds.Size;
                    using (var rtb = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)), new Vector(96, 96)))
                    {
                        rtb.Render(page);
                        rtb.Save(Path.Combine(outDir, "settings.png"));
                    }
                    win.Close();
                }
                catch (Exception ex)
                {
                    failures++;
                    File.WriteAllText(Path.Combine(outDir, "settings_error.txt"), ex.ToString());
                }
            }
            // the floating speech bubble (themes without speech of their own)
            if (only == null)
            {
                try
                {
                    var views = MockData.A();
                    var bubble = new SpeechWindow();
                    bubble.SetText(views[0].Id, Lines.Poke(views[0], new Random(5)), views[0].Color.ToColor(), 5);
                    bubble.SetPointer(DetailPlacement.Side.Above, 60);
                    var content = (Control)bubble.Content;
                    bubble.Content = null;
                    Render(content, Path.Combine(outDir, "speech.png"), scale, true);
                }
                catch (Exception ex)
                {
                    failures++;
                    File.WriteAllText(Path.Combine(outDir, "speech_error.txt"), ex.ToString());
                }
            }
            return failures;
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>Lays out and renders a control to a bitmap (the settings page's theme previews).</summary>
        public static Bitmap RenderToBitmap(Control element, double scale)
        {
            DetachFromParent(element);
            var host = new Panel();
            RenderOptions.SetTextRenderingMode(host, TextRenderingMode.Antialias);
            host.Children.Add(element);
            host.Measure(Size.Infinity);
            host.Arrange(new Rect(host.DesiredSize));
            var size = host.DesiredSize;
            var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
            var rtb = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
            rtb.Render(host);
            host.Children.Clear();
            return rtb;
        }

        /// <summary>Lays out and renders a control to a PNG (optionally on a sample wallpaper).</summary>
        public static void Render(Control element, string file, double scale, bool onWallpaper)
        {
            Control visual = element;
            Border frame = null;
            if (onWallpaper)
            {
                DetachFromParent(element);
                frame = new Border { Background = Wallpaper(), Padding = new Thickness(24), Child = element };
                visual = frame;
            }
            else DetachFromParent(element);
            var host = new Panel();
            RenderOptions.SetTextRenderingMode(host, TextRenderingMode.Antialias);   // like the windows (see PetWindow)
            host.Children.Add(visual);
            host.Measure(Size.Infinity);
            host.Arrange(new Rect(host.DesiredSize));
            Dispatcher.UIThread.RunJobs();
            var size = host.DesiredSize;
            var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
            using (var rtb = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale)))
            {
                rtb.Render(host);
                rtb.Save(file);
            }
            host.Children.Clear();
            if (frame != null) frame.Child = null;
        }

        static void DetachFromParent(Control c)
        {
            var panel = c.Parent as Panel;
            if (panel != null) panel.Children.Remove(c);
            var border = c.Parent as Border;
            if (border != null) border.Child = null;
        }

        static IBrush Wallpaper()
        {
            var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
            b.GradientStops.Add(new GradientStop(Palette.Hex("#3A6073"), 0));
            b.GradientStops.Add(new GradientStop(Palette.Hex("#16222A"), 0.55));
            b.GradientStops.Add(new GradientStop(Palette.Hex("#6D4C7D"), 1));
            return b;
        }
    }
}
