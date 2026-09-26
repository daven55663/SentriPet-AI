using System;
using Avalonia;

namespace SentriPet
{
    /// <summary>
    /// SentriPet for Windows, macOS and Linux (Avalonia).
    ///   SentriPet                     run the desk pet
    ///   SentriPet --dev               separate profile (settings, logs), for testing
    ///   SentriPet --snapshot DIR      render the themes off-screen to PNG (sample data), no window
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
            // --lang <code>: force a language (snapshots default to the source language, Traditional Chinese)
            int li = Array.IndexOf(args, "--lang");
            if (li >= 0 && li + 1 < args.Length) LanguageOverride = args[li + 1];
            if (mode == "--snapshot")
            {
                AppPaths.UseDevProfile();
                UseLanguage(LanguageOverride ?? L.Source);
                return Snapshots.Run(args);
            }
            if (Array.IndexOf(args, "--dev") >= 0) AppPaths.UseDevProfile();
            AppPaths.EnsureDataDirs();
            Log.Info("start " + AppInfo.Version + " (" + Os.Name + ", Avalonia)" + (AppPaths.Dev ? " (dev)" : ""));
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }

        /// <summary>Also used by the Avalonia designer.</summary>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<DesktopApp>().UsePlatformDetect().LogToTrace();
        }
    }
}
