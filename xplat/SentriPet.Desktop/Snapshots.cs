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
        internal class SnapshotSettingsHost : ISettingsHost, IReportHost
        {
            readonly AppSettings settings = new AppSettings();
            UsageService service;
            public AppSettings Settings { get { return settings; } }
            public UsageService Service { get { return service ?? (service = new UsageService(settings)); } }
            public List<ProviderView> Views { get { return MockData.A(); } }
            public UsageHistory History { get { return history ?? (history = MockData.History()); } }
            UsageHistory history;
            public TokenLedger Ledger { get { return ledger ?? (ledger = MockData.Tokens()); } }
            TokenLedger ledger;
            public void ApplyWidgetSettings() { }
            public void ChangeTheme(string id) { }
            public void ChangeLanguage(string code) { }
            public void RefreshViews() { }
            public void OpenReportWindow() { }
            public void MakeShareCard() { }
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
                // the usage report (#18), 7 and 30 days
                try
                {
                    var win = new ReportWindow(new SnapshotSettingsHost(), false) { Width = 760, Height = 900 };
                    win.Show();
                    foreach (var d in new[] { 7, 30 })
                    {
                        win.ShowDays(d);
                        for (int i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
                        var page = win.Page;
                        var size = page.Bounds.Size;
                        using (var rtb = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)), new Vector(96, 96)))
                        {
                            rtb.Render(page);
                            rtb.Save(Path.Combine(outDir, "report_" + d + ".png"));
                        }
                    }
                    win.Close();
                }
                catch (Exception ex)
                {
                    failures++;
                    File.WriteAllText(Path.Combine(outDir, "report_error.txt"), ex.ToString());
                }
                // the tray icons: the jelly and the number (#21)
                try
                {
                    foreach (var v in new[] { 87, 42, 9, 100, -1 })
                    {
                        using (var b = TrayArt.DrawNumber(v, v == 42, 64)) b.Save(Path.Combine(outDir, "tray_number_" + (v < 0 ? "unknown" : v.ToString()) + ".png"));
                        using (var b = TrayArt.Draw(v, v == 42, 64)) b.Save(Path.Combine(outDir, "tray_jelly_" + (v < 0 ? "unknown" : v.ToString()) + ".png"));
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    File.WriteAllText(Path.Combine(outDir, "tray_error.txt"), ex.ToString());
                }
                // the shareable weekly summary (#20)
                try { ShareCard.Save(new SnapshotSettingsHost(), Path.Combine(outDir, "share.png"), DateTime.Now); }
                catch (Exception ex)
                {
                    failures++;
                    File.WriteAllText(Path.Combine(outDir, "share_error.txt"), ex.ToString());
                }
            }
            // the right-click menu and its looks submenu
            if (only == null)
            {
                try
                {
                    var ctl = DesktopController.ForSnapshot(new AppSettings { Theme = "pet" }, MockData.A());
                    RenderMenu(ctl.BuildMenuItems(), Path.Combine(outDir, "menu.png"), scale);
                    var looks = ctl.BuildMenuItems().OfType<MenuItem>().First(m => Equals(m.Header, L.T("換造型")));
                    RenderMenu(((IEnumerable<object>)looks.ItemsSource).ToList(), Path.Combine(outDir, "menu_themes.png"), scale);
                    var report = ctl.BuildMenuItems().OfType<MenuItem>().First(m => Equals(m.Header, L.T("額度利用率（週報／月報）")));
                    RenderMenu(((IEnumerable<object>)report.ItemsSource).ToList(), Path.Combine(outDir, "menu_report.png"), scale);
                }
                catch (Exception ex)
                {
                    failures++;
                    File.WriteAllText(Path.Combine(outDir, "menu_error.txt"), ex.ToString());
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

        /// <summary>A context menu drawn as it looks when open (inside a headless window, so it gets its theme).</summary>
        static void RenderMenu(List<object> items, string file, double scale)
        {
            var menu = new ContextMenu { ItemsSource = items };
            var zoom = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = menu };
            var win = new Window { Content = zoom, SizeToContent = SizeToContent.WidthAndHeight, Background = Brushes.Transparent, FontFamily = G.Ui };
            RenderOptions.SetTextRenderingMode(win, TextRenderingMode.Antialias);
            win.Show();
            for (int i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
            var size = zoom.Bounds.Size;
            using (var rtb = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)), new Vector(96, 96)))
            {
                rtb.Render(zoom);
                rtb.Save(file);
            }
            win.Close();
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>
        /// Lays out and draws a control into a bitmap at a scale. The scale is a transform at 96 dpi, not a higher dpi:
        /// in a bitmap drawn at a higher dpi Avalonia draws a Border that has a BoxShadow without the dpi scale
        /// (the card came out at 1×, its content at 1.5×). Widget windows are not affected.
        /// </summary>
        public static RenderTargetBitmap Draw(Control content, double scale)
        {
            // the transform control puts its transform on the child itself: put back what was there afterwards,
            // or the next drawing of the same control is scaled twice
            var transform = content.RenderTransform;
            var origin = content.RenderTransformOrigin;
            var zoom = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = content };
            var host = new Panel();
            RenderOptions.SetTextRenderingMode(host, TextRenderingMode.Antialias);   // like the windows (see PetWindow)
            host.Children.Add(zoom);
            host.Measure(Size.Infinity);
            host.Arrange(new Rect(host.DesiredSize));
            Dispatcher.UIThread.RunJobs();
            var size = host.DesiredSize;
            var rtb = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width)), Math.Max(1, (int)Math.Ceiling(size.Height))), new Vector(96, 96));
            rtb.Render(host);
            zoom.Child = null;
            host.Children.Clear();
            content.RenderTransform = transform;
            content.RenderTransformOrigin = origin;
            return rtb;
        }

        static bool? rgba;

        /// <summary>
        /// Whether bitmaps give their pixels as R, G, B, A (macOS) rather than B, G, R, A (Windows, Linux): one red pixel tells.
        /// </summary>
        public static bool PixelsAreRgba
        {
            get
            {
                if (rgba == null)
                {
                    using (var bmp = Draw(new Border { Width = 1, Height = 1, Background = Brushes.Red }, 1))
                    {
                        var px = new byte[4];
                        var h = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
                        try { bmp.CopyPixels(new PixelRect(0, 0, 1, 1), h.AddrOfPinnedObject(), 4, 4); }
                        finally { h.Free(); }
                        rgba = px[0] > 128 && px[2] < 128;
                    }
                }
                return rgba.Value;
            }
        }

        /// <summary>The pixels of a bitmap as B, G, R, A whatever the platform gives.</summary>
        public static byte[] Bgra(Bitmap bmp, PixelRect rect)
        {
            var buf = new byte[rect.Width * rect.Height * 4];
            var h = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { bmp.CopyPixels(rect, h.AddrOfPinnedObject(), buf.Length, rect.Width * 4); }
            finally { h.Free(); }
            if (PixelsAreRgba)
                for (int i = 0; i < buf.Length; i += 4) { byte r = buf[i]; buf[i] = buf[i + 2]; buf[i + 2] = r; }
            return buf;
        }

        /// <summary>Lays out and renders a control to a bitmap (the settings page's theme previews).</summary>
        public static Bitmap RenderToBitmap(Control element, double scale)
        {
            DetachFromParent(element);
            return Draw(element, scale);
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
            using (var rtb = Draw(visual, scale)) rtb.Save(file);
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
