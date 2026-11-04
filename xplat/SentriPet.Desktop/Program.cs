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
    ///   SentriPet --install-update URL REPORT   test the self-update (#39) against a releases/latest URL
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
                    int dir = Array.IndexOf(args, "--claude-dir");   // another account's folder (#26)
                    return ClaudeStatusLine.Run(stdin, Console.Out, settings, dir >= 0 && dir + 1 < args.Length ? args[dir + 1] : null);
                }
                catch (Exception ex) { Log.Error("status line", ex); Console.WriteLine(AppInfo.Name); return 0; }
            }
            // "done / waiting for you" (#14): Claude Code's hook (input on stdin) or Codex's notify program (JSON as the
            // last argument). Fast, no window, prints nothing, always exit code 0 (never stops Claude or Codex).
            int hook = Array.IndexOf(args, "--hook");
            if (hook >= 0 && hook + 1 < args.Length)
            {
                if (Array.IndexOf(args, "--dev") >= 0) AppPaths.UseDevProfile();   // tests
                try
                {
                    if (args[hook + 1] == "claude")
                        return AgentHooks.RunClaude(new System.IO.StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false)), DateTime.UtcNow);
                    if (args[hook + 1] == "codex") return AgentHooks.RunCodex(args, AppSettings.Load(), DateTime.UtcNow);
                }
                catch (Exception ex) { Log.Error("hook", ex); }
                return 0;
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
                // the settings page it draws saves its sample settings: into a throwaway folder, not the dev profile
                string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sentripet-snapshot-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                AppPaths.DataDirOverride = scratch;
                UseLanguage(LanguageOverride ?? L.Source);
                try { return Snapshots.Run(args); }
                finally { try { System.IO.Directory.Delete(scratch, true); } catch { } }
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
            // --install-update <releases/latest URL> <report>: the whole self-update without a window, for testing (#39);
            // the new program is started with --smoke-test <report>.smoke
            if (mode == "--install-update")
            {
                AppPaths.UseDevProfile();
                UseLanguage(L.Source);
                return InstallUpdateTest(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
            }
            if (Array.IndexOf(args, "--dev") >= 0) AppPaths.UseDevProfile();
            // started by an update: the copy it replaces is still quitting (#39)
            int wait = Array.IndexOf(args, "--wait-pid");
            if (wait >= 0 && wait + 1 < args.Length) Updater.WaitFor(args[wait + 1], 30000);
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

        static int InstallUpdateTest(string feed, string report)
        {
            var lines = new System.Collections.Generic.List<string> { AppInfo.Name + " " + AppInfo.Version + " install-update test, " + Updater.Kind + ", " + Environment.ProcessPath };
            int code = 0;
            try
            {
                var r = Updater.Fetch(feed);
                lines.Add("release " + (r != null ? r.Version + " with " + r.Assets.Count + " file(s)" : "none"));
                if (r == null || !Updater.IsNewer(r.Version, AppInfo.Version)) throw new UpdateException("not newer than " + AppInfo.Version);
                var psi = Updater.InstallOver(r, Environment.ProcessPath, (text, part) => { if (part <= 0) lines.Add(text); }, System.Threading.CancellationToken.None,
                                              "--smoke-test", report + ".smoke").GetAwaiter().GetResult();
                lines.Add("start " + psi.FileName + " " + string.Join(" ", psi.ArgumentList));
                System.Diagnostics.Process.Start(psi).Dispose();
                lines.Add("PASS");
            }
            catch (Exception ex)
            {
                lines.Add("FAIL " + ex.Message);
                code = 1;
            }
            if (report != null) System.IO.File.WriteAllLines(report, lines);
            return code;
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
