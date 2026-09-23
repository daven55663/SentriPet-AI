using System;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;

[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: AssemblyTitle("SentriPet")]
[assembly: AssemblyProduct("SentriPet")]
[assembly: AssemblyDescription("AI 用量監控桌寵")]
[assembly: AssemblyCopyright("Copyright © 2026 歐育典 · MIT License")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]

namespace SentriPet
{
    static class App
    {
        public const string Version = "1.2.1";
        public const string DisplayName = "SentriPet";

        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;

            // diagnostic modes never touch the real profile
            string mode = args.Length > 0 ? args[0] : "";
            bool diagnostic = mode == "--probe" || mode == "--snapshot" || mode == "--make-icon" || mode == "--snapshot-ui" || mode == "--selftest";
            if (diagnostic || Array.IndexOf(args, "--dev") >= 0) AppPaths.UseDevProfile();

            switch (mode)
            {
                case "--probe": return Probe.Run(args.Length > 1 ? args[1] : null);
                case "--snapshot": return Snapshots.Run(args);
                case "--make-icon": return IconMaker.Run(args);
                case "--snapshot-ui": return new Controller().SnapshotUi(args.Length > 1 ? args[1] : System.IO.Path.GetTempPath());
                case "--selftest": return SelfTest.Run(args.Length > 1 ? args[1] : null);
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
