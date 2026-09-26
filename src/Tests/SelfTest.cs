using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace SentriPet
{
    /// <summary>
    /// --selftest [report]: every automated check — formatting, JSON, the usage model, settings, reminders, each data
    /// source (on sample files and local fake servers, never the user's real data) and the widget (themes, faces,
    /// hover card, placement). The exit code is the number of failures; CI runs it on every push.
    /// </summary>
    static class SelfTest
    {
        public static int Run(string outFile)
        {
            var t = new TestKit();
            var sw = Stopwatch.StartNew();
            string oldDataDir = AppPaths.DataDirOverride;
            AppPaths.DataDirOverride = t.TempDir("profile");   // settings and logs of this run go to a throwaway folder
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                CoreSuite.Run(t);
                UiTests.Run(t);
            }
            finally
            {
                AppPaths.DataDirOverride = oldDataDir;
                t.Cleanup();
            }
            string text = CoreSuite.Report(t, sw.Elapsed, "self-test");
            if (outFile != null) File.WriteAllText(outFile, text, new UTF8Encoding(false));
            else Console.Write(text);
            return t.Failed;
        }
    }
}
