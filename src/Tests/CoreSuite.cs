using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentriPet
{
    /// <summary>
    /// Every check that needs no UI. The Windows self-test runs these plus the WPF widget checks;
    /// xplat/SentriPet.Tests runs them on Windows, macOS and Linux.
    /// </summary>
    static class CoreSuite
    {
        public static void Run(TestKit t)
        {
            CoreTests.Run(t);
            ModelTests.Run(t);
            SettingsTests.Run(t);
            TrackerTests.Run(t);
            ClaudeTests.Run(t);
            CodexTests.Run(t);
            ProviderTests.Run(t);
            ServiceTests.Run(t);
            PlatformTests.Run(t);
            I18nTests.Run(t);
        }

        public static string Report(TestKit t, TimeSpan elapsed, string runner)
        {
            string summary = (t.Failed == 0 ? "ALL PASS" : t.Failed + " FAILED") + "  (" + t.Passed + " passed, " + t.Failed + " failed, " + t.Skipped + " skipped, " +
                             elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s)";
            return AppInfo.Name + " " + AppInfo.Version + " " + runner + " on " + Os.Name + " @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                   Environment.NewLine + t.Report + Environment.NewLine + summary + Environment.NewLine;
        }
    }

    /// <summary>Folders, paths, PATH lookup and running programs — the parts that differ per operating system.</summary>
    static class PlatformTests
    {
        public static void Run(TestKit t)
        {
            t.Section("作業系統相關（" + Os.Name + "）");
            t.Run("Platform", () =>
            {
                t.Equal("剛好是一種作業系統", 1, (Os.Windows ? 1 : 0) + (Os.Mac ? 1 : 0) + (Os.Linux ? 1 : 0));
                string expected = Os.Windows ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                                : Os.Mac ? Path.Combine(AppPaths.Home, "Library", "Application Support")
                                : (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(AppPaths.Home, ".config"));
                t.Equal("設定資料夾的位置", expected, AppPaths.AppData);
                t.Equal("%APPDATA% 在每個系統都指向設定資料夾", Path.Combine(AppPaths.AppData, "Claude"), AppPaths.Expand("%APPDATA%/Claude"));
                t.Equal("~ 是家目錄", Path.Combine(AppPaths.Home, ".codex", "sessions"), AppPaths.Expand("~/.codex/sessions"));

                string dir = t.TempDir("glob");
                foreach (var f in new[] { "a1.json", "a2.json", "b.txt" }) TestKit.WriteFile(Path.Combine(dir, f), "{}");
                TestKit.WriteFile(Path.Combine(dir, "x", "deep.json"), "{}");
                TestKit.WriteFile(Path.Combine(dir, "y", "deep.json"), "{}");
                t.Equal("萬用字元：*.json", 2, AppPaths.Glob(Path.Combine(dir, "*.json")).Count);
                t.Equal("萬用字元：中間的資料夾", 2, AppPaths.Glob(Path.Combine(dir, "*", "deep.json")).Count);
                t.Equal("沒有萬用字元：存在的檔案", 1, AppPaths.Glob(Path.Combine(dir, "b.txt")).Count);

                // a program on PATH (a .cmd file on Windows, a plain file elsewhere)
                string bin = t.TempDir("bin");
                TestKit.WriteFile(Path.Combine(bin, Os.Windows ? "sentripet-fake-tool.cmd" : "sentripet-fake-tool"), Os.Windows ? "@echo off\r\n" : "#!/bin/sh\n");
                string oldPath = Environment.GetEnvironmentVariable("PATH");
                try
                {
                    Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + oldPath);
                    AppPaths.ClearWhichCache();
                    string found = AppPaths.Which("sentripet-fake-tool");
                    t.Check("在 PATH 上找得到程式", found != null && found.StartsWith(bin), found);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("PATH", oldPath);
                    AppPaths.ClearWhichCache();
                }

                // arguments arrive intact, spaces included (cmd script on Windows, sh script elsewhere)
                string script = Path.Combine(bin, Os.Windows ? "args.cmd" : "args.sh");
                TestKit.WriteFile(script, Os.Windows ? "@echo off\r\necho [%~1] [%~2]\r\n" : "printf '[%s] [%s]\\n' \"$1\" \"$2\"\n");
                int code;
                string err;
                string output = Net.RunCommand(script, new List<string> { "a b", "c" }, false, 20000, out code, out err);
                t.Equal("執行腳本：參數（含空白）原樣傳入", "[a b] [c]", output.Trim());
                output = Net.RunCommand("echo", new List<string> { "hello world" }, true, 20000, out code, out err);
                t.Equal("透過系統 shell 執行指令", "hello world", output.Trim().Trim('"'));
                bool threw = false;
                try { Net.RunCommand(Path.Combine(bin, "no-such-program"), new List<string>(), false, 5000, out code, out err); }
                catch (Exception) { threw = true; }
                t.Check("不存在的程式：丟出錯誤（外掛會顯示原因）", threw);
            });
        }
    }
}
