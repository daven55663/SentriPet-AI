using System;
using Avalonia;

namespace SentriPet
{
    /// <summary>
    /// SentriPet for Windows, macOS and Linux (Avalonia).
    ///   SentriPet                     run the desk pet
    ///   SentriPet --dev               separate profile (settings, logs), for testing
    ///   SentriPet --snapshot DIR      render the themes off-screen to PNG (sample data), no window
    ///   SentriPet --demo FILE.gif     the animated tour for the README (sample data), no window
    ///   ... --lang en                 use a language (zh-TW, zh-CN, en, ja, ko; default: the setting / zh-TW for snapshots)
    /// </summary>
    static class Program
    {
        /// <summary>The language from --lang (wins over the setting).</summary>
        public static string LanguageOverride;

        /// <summary>Switches texts and fonts to a language setting ("auto" or a code).</summary>
        public static void UseLanguage(string setting)
        {
            L.Use(setting);
            G.UseFonts();
        }

        [STAThread]
        static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "";
            // Claude Code's status-line command (#9): fast, no window, no single-instance check
            if (mode == "--statusline")
            {
                if (Array.IndexOf(args, "--dev") >= 0) AppPaths.UseDevProfile();   // tests
                try
                {
                    var settings = AppSettings.Load();
                    L.Use(settings.Language);
                    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
                    var stdin = new System.IO.StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false));
                    return ClaudeStatusLine.Run(stdin, Console.Out, settings);
                }
                catch (Exception ex) { Log.Error("status line", ex); Console.WriteLine(AppInfo.Name); return 0; }
            }
            // --lang <code>: force a language (snapshots default to the source language, Traditional Chinese)
            int li = Array.IndexOf(args, "--lang");
            if (li >= 0 && li + 1 < args.Length) LanguageOverride = args[li + 1];
            if (mode == "--probe")
            {
                AppPaths.UseDevProfile();
                UseLanguage(LanguageOverride ?? L.Source);
                return Probe.Run(args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null);
            }
            if (mode == "--selftest")
            {
                AppPaths.UseDevProfile();
                UseLanguage(L.Source);
                return DesktopTests.Run(args.Length > 1 ? args[1] : null);
            }
            if (mode == "--snapshot")
            {
                AppPaths.UseDevProfile();
                UseLanguage(LanguageOverride ?? L.Source);
                return Snapshots.Run(args);
            }
            if (mode == "--demo")
            {
                AppPaths.UseDevProfile();
                UseLanguage(LanguageOverride ?? L.Source);
                return Demo.Run(args);
            }
            if (mode == "--social-card")
            {
                AppPaths.UseDevProfile();
                UseLanguage("en");
                return Demo.SocialCard(args);
            }
            if (Array.IndexOf(args, "--dev") >= 0) AppPaths.UseDevProfile();
            // one copy per user: a second start asks the running one to show itself
            if (!Integration.ClaimSingleInstance(() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (DesktopController.Instance != null) DesktopController.Instance.ShowFromOtherCopy();
                })))
                return 0;
            AppPaths.EnsureDataDirs();
            Log.Info("start " + AppInfo.Version + " (" + Os.Name + ", Avalonia)" + (AppPaths.Dev ? " (dev)" : ""));
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }

        /// <summary>Also used by the Avalonia designer.</summary>
        public static AppBuilder BuildAvaloniaApp()
        {
            var b = AppBuilder.Configure<DesktopApp>().UsePlatformDetect().LogToTrace();
            // a small widget draws cheaper on the CPU than through a GPU device (and saves the driver's memory);
            // --gpu keeps the default (like the WPF version)
            // (measured on Windows: 108 MB instead of 252 MB, half the CPU)
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--gpu") < 0)
                b = b.With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } })
                     .With(new X11PlatformOptions { RenderingMode = new[] { X11RenderingMode.Software } })
                     .With(new AvaloniaNativePlatformOptions { RenderingMode = new[] { AvaloniaNativeRenderingMode.Software } });
            return b;
        }
    }
}
