using System;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;

[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: AssemblyTitle("SentriPet")]
[assembly: AssemblyProduct("SentriPet")]
[assembly: AssemblyDescription("AI 用量監控桌寵")] // i18n-ignore
[assembly: AssemblyCopyright("Copyright © 2026 歐育典 · MIT License")] // i18n-ignore
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace SentriPet
{
    static class App
    {
        public const string Version = AppInfo.Version;
        public const string DisplayName = AppInfo.Name;

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
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;

            // diagnostic modes never touch the real profile
            string mode = args.Length > 0 ? args[0] : "";
            bool diagnostic = mode == "--probe" || mode == "--snapshot" || mode == "--make-icon" || mode == "--snapshot-ui" || mode == "--selftest" ||
                              mode == "--fake-codex-app-server";
            if (diagnostic || Array.IndexOf(args, "--dev") >= 0) AppPaths.UseDevProfile();

            // --lang <code>: force a language (diagnostic modes default to the source language, Traditional Chinese)
            int li = Array.IndexOf(args, "--lang");
            if (li >= 0 && li + 1 < args.Length) LanguageOverride = args[li + 1];
            if (diagnostic) UseLanguage(LanguageOverride ?? L.Source);

            switch (mode)
            {
                case "--probe": return Probe.Run(args.Length > 1 ? args[1] : null);
                case "--snapshot": return Snapshots.Run(args);
                case "--make-icon": return IconMaker.Run(args);
                case "--snapshot-ui": return new Controller().SnapshotUi(args.Length > 1 ? args[1] : System.IO.Path.GetTempPath());
                case "--selftest": return SelfTest.Run(args.Length > 1 ? args[1] : null);
                case "--fake-codex-app-server": return FakeCodexServer.Run(args.Length > 1 ? args[1] : null);
            }

            string suffix = AppPaths.Dev ? ".dev" : "";
            bool created;
            var mutex = new Mutex(true, @"Local\SentriPet.Singleton" + suffix, out created);
            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SentriPet.Show" + suffix);
            if (!created)
            {
                // Already running → ask that instance to pop up and leave.
                showSignal.Set();
                return 0;
            }
            AppPaths.MigrateLegacyData();
            AppPaths.EnsureDataDirs();
            try
            {
                var controller = new Controller();
                return controller.Run(showSignal);
            }
            finally
            {
                GC.KeepAlive(mutex);
            }
        }
    }
}
