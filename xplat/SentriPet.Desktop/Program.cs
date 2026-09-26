using System;
using Avalonia;

namespace SentriPet
{
    /// <summary>
    /// SentriPet for Windows, macOS and Linux (Avalonia).
    ///   SentriPet                     run the desk pet
    ///   SentriPet --dev               separate profile (settings, logs), for testing
    ///   SentriPet --snapshot DIR      render the themes off-screen to PNG (sample data), no window
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "";
            if (mode == "--snapshot")
            {
                AppPaths.UseDevProfile();
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
