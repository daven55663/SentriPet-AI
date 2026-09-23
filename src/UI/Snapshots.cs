using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SentriPet
{
    /// <summary>
    /// --snapshot outDir [--mock] [--theme id] [--scale 1.5]
    /// Renders themes off-screen to PNG (transparent + on a sample wallpaper). Used for previews and testing.
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
            public Point? CursorIn(FrameworkElement element) { return new Point(160, -40); }
            public void SaveSettings() { }
        }

        public static BitmapSource RenderToBitmap(FrameworkElement visual, double scale)
        {
            visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            visual.Arrange(new Rect(visual.DesiredSize));
            visual.UpdateLayout();
            var size = visual.DesiredSize;
            var rtb = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        public static int Run(string[] args)
        {
            string outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "sentripet-shots");
            bool mock = args.Contains("--mock");
            string only = Arg(args, "--theme");
            double scale = 1.5;
            double s;
            if (double.TryParse(Arg(args, "--scale") ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s)) scale = s;
            Directory.CreateDirectory(outDir);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            var sets = new List<KeyValuePair<string, List<ProviderView>>>();
            if (mock)
            {
                sets.Add(new KeyValuePair<string, List<ProviderView>>("a", MockA()));
                sets.Add(new KeyValuePair<string, List<ProviderView>>("b", MockB()));
            }
            else sets.Add(new KeyValuePair<string, List<ProviderView>>("live", LiveViews()));

            foreach (var info in ThemeCatalog.All)
            {
                if (only != null && info.Id != only) continue;
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
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(Path.Combine(outDir, info.Id + "_" + set.Key + "_error.txt"), ex.ToString());
                    }
                }
            }
            // the hover card and the floating speech bubble for the first provider of each set
            foreach (var set in sets)
            {
                if (set.Value.Count == 0 || only != null) continue;
                try
                {
                    var v = set.Value[0];
                    var card = DetailCardView.Build(v);
                    card.SetPointer(DetailPlacement.Side.Above, 90);
                    Render(card.Root, Path.Combine(outDir, "detail_" + set.Key + ".png"), scale, true);
                    var speech = new SpeechWindow(null);
                    speech.SetText(v.Id, v.UseItLevel > 0 ? Lines.UseItAlert(v) : Lines.Poke(v, new Random(5)), v.Color, 5);
                    speech.SetPointer(DetailPlacement.Side.Above, 60);
                    var content = (FrameworkElement)speech.Content;
                    speech.Content = null;
                    Render(content, Path.Combine(outDir, "speech_" + set.Key + ".png"), scale, true);
                    speech.Close();
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(outDir, "detail_" + set.Key + "_error.txt"), ex.ToString());
                }
            }
            return 0;
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static void Render(FrameworkElement element, string file, double scale, bool onWallpaper)
        {
            FrameworkElement visual = element;
            Grid host = null;
            if (onWallpaper)
            {
                var old = element.Parent as Panel;
                if (old != null) old.Children.Remove(element);
                host = new Grid { Background = Wallpaper() };
                host.Children.Add(new Border { Padding = new Thickness(24), Child = element });
                visual = host;
            }
            visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            visual.Arrange(new Rect(visual.DesiredSize));
            visual.UpdateLayout();
            var size = visual.DesiredSize;
            var rtb = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(file)) enc.Save(fs);
            if (host != null)
            {
                ((Border)host.Children[0]).Child = null;
            }
        }

        static Brush Wallpaper()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            b.GradientStops.Add(new GradientStop(Palette.Hex("#3A6073"), 0));
            b.GradientStops.Add(new GradientStop(Palette.Hex("#16222A"), 0.55));
            b.GradientStops.Add(new GradientStop(Palette.Hex("#6D4C7D"), 1));
            return b;
        }

        static List<ProviderView> LiveViews()
        {
            var settings = AppSettings.Load();
            var list = new List<ProviderView>();
            foreach (var p in ProviderRegistry.CreateAll())
            {
                try
                {
                    var d = p.Detect();
                    if (!d.Installed || !settings.IsEnabled(p.Id)) continue;
                    var snap = p.Fetch(false, settings);
                    if (snap.Offline) continue;
                    list.Add(UsageService.MakeView(p, snap));
                }
                catch { }
                finally { p.Dispose(); }
            }
            return list;
        }

        static Meter M(string key, string label, string shortLabel, double used, double hours, bool approx, int window)
        {
            return new Meter { Key = key, Label = label, ShortLabel = shortLabel, Used = used, ResetsAt = DateTime.UtcNow.AddHours(hours), ResetApprox = approx, WindowMinutes = window };
        }

        static ProviderView View(Provider p, Snapshot s)
        {
            return UsageService.MakeView(p, s);
        }

        public static List<ProviderView> MockA()
        {
            var claude = new Snapshot { Source = "Claude 桌面版快取", ObservedAt = DateTime.UtcNow.AddMinutes(-4) };
            claude.Meters.Add(M("fh", "5 小時", "5h", 34, 2.2, true, 300));
            claude.Meters.Add(M("sd", "每週", "週", 18, 33.5, true, 10080));
            var codex = new Snapshot { Source = "Codex 官方 app-server", ObservedAt = DateTime.UtcNow, Plan = "Plus", Active = true };
            codex.Meters.Add(M("codex:300", "5 小時", "5h", 4, 4.9, false, 300));
            codex.Meters.Add(M("codex:10080", "每週", "週", 23, 3.5, false, 10080));
            var copilot = new Snapshot { Source = "Copilot CLI 快取", ObservedAt = DateTime.UtcNow.AddDays(-11), Stale = true, Plan = "Pro" };
            copilot.Meters.Add(new Meter { Key = "premium_interactions", Label = "進階請求", ShortLabel = "PR", Used = 0, ResetsAt = DateTime.UtcNow.AddDays(7.7), WindowMinutes = 43200, ValueText = "200 / 200" });
            return new List<ProviderView> { View(new ClaudeProvider(), claude), View(new CodexProvider(), codex), View(new CopilotProvider(), copilot) };
        }

        public static List<ProviderView> MockB()
        {
            var claude = new Snapshot { Source = "Claude 桌面版快取", ObservedAt = DateTime.UtcNow.AddMinutes(-2) };
            claude.Meters.Add(M("fh", "5 小時", "5h", 92, 0.6, true, 300));
            claude.Meters.Add(M("sd", "每週", "週", 71, 20, true, 10080));
            var codex = new Snapshot { Source = "Codex 本機紀錄", ObservedAt = DateTime.UtcNow.AddMinutes(-30), Plan = "Pro" };
            codex.Meters.Add(M("codex:300", "5 小時", "5h", 100, 1.4, false, 300));
            codex.Meters.Add(M("codex:10080", "每週", "週", 64, 60, false, 10080));
            var worried = new Snapshot { Source = "外掛", ObservedAt = DateTime.UtcNow };
            worried.Meters.Add(M("q", "每日", "日", 78, 9, false, 1440));
            var gemini = new CustomProviderStub("gemini", "Gemini", "cat", "#4C8DF6");
            var ollama = new Snapshot { Error = "Ollama 沒有在執行" };
            return new List<ProviderView> { View(new ClaudeProvider(), claude), View(new CodexProvider(), codex), View(gemini, worried), View(new CustomProviderStub("err", "Kiro", "antenna", "#9D7CFF"), ollama) };
        }

        class CustomProviderStub : Provider
        {
            public CustomProviderStub(string id, string name, string mascot, string color)
            {
                Id = id; Name = name; Mascot = mascot; Color = Palette.Hex(color);
            }
            public override Detection Detect() { return new Detection { Installed = true }; }
            public override Snapshot Fetch(bool force, AppSettings settings) { return null; }
        }
    }
}
