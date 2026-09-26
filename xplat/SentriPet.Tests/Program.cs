using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SentriPet
{
    /// <summary>
    /// Cross-platform self-test: every check that needs no UI, on Windows, macOS and Linux.
    ///   dotnet run -c Release --project xplat/SentriPet.Tests -- [report.txt]
    /// The exit code is the number of failures.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            // the Codex tests start this program again as a stand-in for "codex app-server"
            if (args.Length > 0 && args[0] == "--fake-codex-app-server") return FakeCodexServer.Run(args.Length > 1 ? args[1] : null);

            AppPaths.UseDevProfile();
            var t = new TestKit();
            var sw = Stopwatch.StartNew();
            AppPaths.DataDirOverride = t.TempDir("profile");   // settings and logs of this run go to a throwaway folder
            try
            {
                CoreSuite.Run(t);
            }
            finally
            {
                AppPaths.DataDirOverride = null;
                t.Cleanup();
            }
            string text = CoreSuite.Report(t, sw.Elapsed, "core tests");
            string outFile = args.Length > 0 ? args[0] : null;
            Console.OutputEncoding = Encoding.UTF8;
            if (outFile == null) Console.Write(text);
            else
            {
                // full report in the file; failures and the summary on the console
                File.WriteAllText(outFile, text, new UTF8Encoding(false));
                foreach (var line in text.Split('\n'))
                    if (line.StartsWith("FAIL ") || line.StartsWith("SKIP ") || line.StartsWith("ALL PASS") || line.Contains(" FAILED  ("))
                        Console.WriteLine(line.TrimEnd('\r'));
            }
            return t.Failed;
        }
    }
}
