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
                CoreTests.Run(t);
                ModelTests.Run(t);
                SettingsTests.Run(t);
                TrackerTests.Run(t);
                ClaudeTests.Run(t);
                CodexTests.Run(t);
                ProviderTests.Run(t);
                ServiceTests.Run(t);
                UiTests.Run(t);
            }
            finally
            {
                AppPaths.DataDirOverride = oldDataDir;
                t.Cleanup();
            }
            string summary = (t.Failed == 0 ? "ALL PASS" : t.Failed + " FAILED") + "  (" + t.Passed + " passed, " + t.Failed + " failed, " + t.Skipped + " skipped, " +
                             sw.Elapsed.TotalSeconds.ToString("0.0") + " s)";
            string text = App.DisplayName + " " + App.Version + " self-test @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                          t.Report + Environment.NewLine + summary + Environment.NewLine;
            if (outFile != null) File.WriteAllText(outFile, text, new UTF8Encoding(false));
            else Console.Write(text);
            return t.Failed;
        }
    }
}
