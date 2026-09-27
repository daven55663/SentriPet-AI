using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentriPet
{
    /// <summary>The Claude Code status-line bridge (#9): reading its input, keeping the user's own status line, the provider.</summary>
    static class ClaudeStatusLineTests
    {
        // the example from the Claude Code documentation (status line input), trimmed to what matters
        const string Input = "{\"session_id\":\"abc\",\"model\":{\"id\":\"claude-opus-4-1\",\"display_name\":\"Opus\"}," +
                             "\"rate_limits\":{\"five_hour\":{\"used_percentage\":23.5,\"resets_at\":RESET5},\"seven_day\":{\"used_percentage\":41.2,\"resets_at\":RESET7}}}";

        static string InputWith(DateTime reset5, DateTime reset7)
        {
            return Input.Replace("RESET5", (Json.ToUnixMs(reset5) / 1000).ToString()).Replace("RESET7", (Json.ToUnixMs(reset7) / 1000).ToString());
        }

        static DateTime WholeSecond(DateTime t) { return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, DateTimeKind.Utc); }

        public static void Run(TestKit t)
        {
            string oldConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            try
            {
                Reading(t);
                Command(t);
                Settings(t);
                Provider(t);
            }
            finally
            {
                ClaudeStatusLine.FileOverride = null;
                ClaudeStatusLine.SettingsOverride = null;
                ClaudeProvider.HistoryOverride = null;
                Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", oldConfig);
            }
        }

        static void Reading(TestKit t)
        {
            t.Section("Claude Code 狀態列：讀官方用量");
            t.Run("Extract", () =>
            {
                var now = WholeSecond(DateTime.UtcNow);
                var d = ClaudeStatusLine.Extract(Json.Parse(InputWith(now.AddHours(2), now.AddDays(3))), now);
                t.Check("讀到 5 小時與每週", d != null && d.FiveHour != null && d.SevenDay != null);
                t.Near("5 小時用量", 23.5, d.FiveHour.Used, 0.001);
                t.Equal("5 小時重置時間（Unix 秒）", now.AddHours(2), d.FiveHour.ResetsAt);
                t.Near("每週用量", 41.2, d.SevenDay.Used, 0.001);
                t.Equal("每週重置時間", now.AddDays(3), d.SevenDay.ResetsAt);
                t.Equal("模型名稱", "Opus", d.Model);
                t.Check("沒有 rate_limits（API 金鑰、還沒回覆）：沒有資料", ClaudeStatusLine.Extract(Json.Parse("{\"model\":{\"display_name\":\"Opus\"}}"), now) == null);
                var onlyWeek = ClaudeStatusLine.Extract(Json.Parse("{\"rate_limits\":{\"seven_day\":{\"used_percentage\":5,\"resets_at\":1900000000}}}"), now);
                t.Check("只有每週（5 小時剛重置）：只讀每週", onlyWeek != null && onlyWeek.FiveHour == null && onlyWeek.SevenDay != null);
            });
        }

        static void Command(TestKit t)
        {
            t.Section("Claude Code 狀態列：SentriPet 當作狀態列指令");
            t.Run("Command", () =>
            {
                string dir = t.TempDir("statusline");
                ClaudeStatusLine.FileOverride = Path.Combine(dir, "claude-code-status.json");
                var now = WholeSecond(DateTime.UtcNow);
                string input = InputWith(now.AddHours(2), now.AddDays(3));

                var outp = new StringWriter();
                ClaudeStatusLine.Run(new StringReader(input), outp, new AppSettings());
                var saved = ClaudeStatusLine.Load();
                t.Check("把官方數字存起來", saved != null && Math.Abs(saved.FiveHour.Used - 23.5) < 0.001 && saved.SevenDay.ResetsAt == now.AddDays(3));
                t.Contains("沒有自己的狀態列：顯示剩餘額度", outp.ToString(), Fmt.Pct(100 - 23.5));

                var s = new AppSettings { ClaudeStatusLineChain = "{\"type\":\"command\",\"command\":\"cat\"}" };
                outp = new StringWriter();
                ClaudeStatusLine.Run(new StringReader(input), outp, s);
                t.Contains("有自己的狀態列：照常顯示，而且收到一樣的資料", outp.ToString(), "\"rate_limits\"");
                s.ClaudeStatusLineChain = "{\"type\":\"command\",\"command\":\"echo chained-line\"}";
                outp = new StringWriter();
                ClaudeStatusLine.Run(new StringReader(input), outp, s);
                t.Contains("有自己的狀態列：顯示它的輸出", outp.ToString(), "chained-line");

                outp = new StringWriter();
                ClaudeStatusLine.Run(new StringReader("{\"model\":{\"display_name\":\"Opus\"}}"), outp, new AppSettings());
                t.Check("這次沒有用量資料：保留上一次的數字", ClaudeStatusLine.Load() != null && ClaudeStatusLine.Load().FiveHour != null);

                string home = AppPaths.Home;
                string exe = Path.Combine(home, ".local", "share", "sentripet", Os.Windows ? "SentriPet.exe" : "SentriPet");
                string cmd = ClaudeStatusLine.CommandFor(exe);
                t.Check("指令用 ~/ 開頭、正斜線（Git Bash、PowerShell、sh 都能用）", cmd.StartsWith("~/") && !cmd.Contains("\\") && cmd.EndsWith(" --statusline"), cmd);
                string spaced = ClaudeStatusLine.CommandFor(Os.Windows ? @"D:\My Apps\SentriPet.exe" : "/opt/My Apps/SentriPet");
                t.Check("路徑有空白：加引號", spaced.StartsWith("\"") && spaced.Contains("My Apps"), spaced);
                t.Check("認得自己的指令", ClaudeStatusLine.IsOurs(cmd) && !ClaudeStatusLine.IsOurs("~/.claude/statusline.sh"));
            });
        }

        static void Settings(TestKit t)
        {
            t.Section("Claude Code 狀態列：開關（~/.claude/settings.json）");
            t.Run("Settings", () =>
            {
                string dir = t.TempDir("claude-settings");
                string file = Path.Combine(dir, ".claude", "settings.json");
                ClaudeStatusLine.SettingsOverride = file;
                string exe = Path.Combine(AppPaths.Home, "Apps", "SentriPet", "SentriPet");

                // no settings file yet
                var s = new AppSettings();
                ClaudeStatusLine.Connect(s, exe);
                var o = Json.Parse(File.ReadAllText(file));
                t.Equal("沒有設定檔：建立並加上狀態列", ClaudeStatusLine.CommandFor(exe), Json.Str(Json.Path(o, "statusLine.command")));
                t.Check("連接後：開關打開、沒有要保留的舊狀態列", s.ClaudeStatusBridge && s.ClaudeStatusLineChain == null && ClaudeStatusLine.IsConnected());
                ClaudeStatusLine.Disconnect(s);
                o = Json.Parse(File.ReadAllText(file));
                t.Check("關掉：狀態列設定拿掉", Json.Get(o, "statusLine") == null && !ClaudeStatusLine.IsConnected() && !s.ClaudeStatusBridge);

                // the user already has settings and a status line of their own
                string original = "{\n  \"model\": \"opus\",\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"~/.claude/my.sh\",\n    \"padding\": 2\n  },\n  \"permissions\": {\n    \"allow\": [\"Bash(ls)\"]\n  }\n}\n";
                File.WriteAllText(file, original);
                File.Delete(file + ".sentripet-backup");
                s = new AppSettings();
                ClaudeStatusLine.Connect(s, exe);
                o = Json.Parse(File.ReadAllText(file));
                t.Equal("原本的狀態列：換成 SentriPet", ClaudeStatusLine.CommandFor(exe), Json.Str(Json.Path(o, "statusLine.command")));
                t.Equal("原本狀態列的其他選項保留（padding）", 2.0, Json.Num(Json.Path(o, "statusLine.padding")));
                t.Check("其他設定完全不動", Json.Str(Json.Get(o, "model")) == "opus" && Json.Str(Json.Path(o, "permissions.allow[0]")) == "Bash(ls)");
                t.Contains("記住原本的狀態列（照常顯示）", s.ClaudeStatusLineChain, "~/.claude/my.sh");
                t.Equal("修改前先備份原檔", original, File.ReadAllText(file + ".sentripet-backup"));
                ClaudeStatusLine.Connect(s, exe);
                t.Contains("再按一次連接：不會把自己當成原本的狀態列", s.ClaudeStatusLineChain, "~/.claude/my.sh");
                ClaudeStatusLine.Disconnect(s);
                o = Json.Parse(File.ReadAllText(file));
                t.Equal("關掉：原本的狀態列原封不動還原", "~/.claude/my.sh", Json.Str(Json.Path(o, "statusLine.command")));
                t.Check("還原後其他設定也不動", Json.Num(Json.Path(o, "statusLine.padding")) == 2 && Json.Str(Json.Get(o, "model")) == "opus" && s.ClaudeStatusLineChain == null);

                // following the program, noticing a removal
                ClaudeStatusLine.Connect(s, exe);
                string moved = Path.Combine(AppPaths.Home, "Other", "SentriPet");
                ClaudeStatusLine.Repair(s, moved);
                t.Equal("程式搬家：狀態列指令跟著換", ClaudeStatusLine.CommandFor(moved), Json.Str(Json.Path(Json.Parse(File.ReadAllText(file)), "statusLine.command")));
                File.WriteAllText(file, "{\"model\":\"opus\"}");
                ClaudeStatusLine.Repair(s, moved);
                t.Check("使用者自己在 Claude 拿掉狀態列：SentriPet 的開關跟著關", !s.ClaudeStatusBridge && s.ClaudeStatusLineChain == null);

                File.WriteAllText(file, "[1, 2]");
                bool refused = false;
                try { ClaudeStatusLine.Connect(new AppSettings(), exe); }
                catch (InvalidDataException) { refused = true; }
                t.Check("看不懂的設定檔：不修改", refused && File.ReadAllText(file) == "[1, 2]");
            });
        }

        static void Provider(TestKit t)
        {
            t.Section("Claude 用量：狀態列的官方數字");
            t.Run("Provider", () =>
            {
                var now = WholeSecond(DateTime.UtcNow);
                string dir = t.TempDir("claude-bridge");
                Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", dir);
                ClaudeStatusLine.FileOverride = Path.Combine(dir, "claude-code-status.json");
                var on = new AppSettings { ClaudeStatusBridge = true, ClaudeEstimate = false };

                // no desktop app (Linux): the status line alone
                ClaudeProvider.HistoryOverride = Path.Combine(dir, "missing.json");
                ClaudeStatusLine.Save(new ClaudeStatusLine.Data
                {
                    ObservedAt = now.AddMinutes(-10),
                    FiveHour = new ClaudeStatusLine.Window { Used = 30, ResetsAt = now.AddHours(2) },
                    SevenDay = new ClaudeStatusLine.Window { Used = 45, ResetsAt = now.AddDays(3) },
                });
                var s = new ClaudeProvider().Fetch(false, on);
                var fh = s.Meters.FirstOrDefault(m => m.Key == "fh");
                var sd = s.Meters.FirstOrDefault(m => m.Key == "sd");
                t.Check("沒有桌面版：用狀態列的官方數字", s.Error == null && fh != null && sd != null && fh.Used == 30 && sd.Used == 45, s.Error);
                t.Check("重置時間是官方的（不標 ≈）", fh.ResetsAt == now.AddHours(2) && !fh.ResetApprox && sd.ResetsAt == now.AddDays(3) && !sd.ResetApprox);
                t.Check("註明來源", s.Source.Contains("狀態列") && !s.Stale, s.Source);
                var off = new ClaudeProvider().Fetch(false, new AppSettings { ClaudeEstimate = false });
                t.Check("沒開橋接：不讀（提示怎麼開）", off.Error != null && !off.Meters.Any(), off.Error);

                ClaudeStatusLine.Save(new ClaudeStatusLine.Data
                {
                    ObservedAt = now.AddHours(-3),
                    FiveHour = new ClaudeStatusLine.Window { Used = 80, ResetsAt = now.AddMinutes(-30) },
                    SevenDay = new ClaudeStatusLine.Window { Used = 60, ResetsAt = now.AddHours(-1) },
                });
                s = new ClaudeProvider().Fetch(false, on);
                fh = s.Meters.First(m => m.Key == "fh");
                sd = s.Meters.First(m => m.Key == "sd");
                t.Check("5 小時已經過了重置時間：歸零", fh.Used == 0 && fh.WasReset);
                t.Check("每週已經重置：歸零，下次是一週後", sd.Used == 0 && sd.ResetsAt == now.AddHours(-1).AddDays(7) && !sd.ResetApprox, sd.ResetsAt.ToString());

                // with the desktop app's history
                string history = Path.Combine(dir, "plan-usage-history.json");
                ClaudeProvider.HistoryOverride = history;
                var samples = new List<Dictionary<string, object>>();
                foreach (var ago in new[] { 50, 35, 20 })
                    samples.Add(new Dictionary<string, object> { { "t", Json.ToUnixMs(now.AddMinutes(-ago)) }, { "u", new Dictionary<string, object> { { "fh", 10.0 }, { "sd", 40.0 } } } });
                TestKit.WriteFile(history, Json.Serialize(new Dictionary<string, object> { { "samples", samples } }, false));

                ClaudeStatusLine.Save(new ClaudeStatusLine.Data
                {
                    ObservedAt = now.AddMinutes(-2),
                    FiveHour = new ClaudeStatusLine.Window { Used = 12, ResetsAt = now.AddHours(3) },
                    SevenDay = new ClaudeStatusLine.Window { Used = 41, ResetsAt = now.AddDays(2) },
                });
                s = new ClaudeProvider().Fetch(false, on);
                fh = s.Meters.First(m => m.Key == "fh");
                t.Check("狀態列比桌面版新：用官方的用量與重置時間", fh.Used == 12 && fh.ResetsAt == now.AddHours(3) && !fh.ResetApprox, fh.Used + " " + fh.ResetsAt);
                t.Check("來源標成狀態列（官方）", s.Source.Contains("狀態列") && s.Note.Contains("官方"), s.Source + " / " + s.Note);

                ClaudeStatusLine.Save(new ClaudeStatusLine.Data
                {
                    ObservedAt = now.AddMinutes(-40),
                    FiveHour = new ClaudeStatusLine.Window { Used = 8, ResetsAt = now.AddHours(3) },
                });
                s = new ClaudeProvider().Fetch(false, on);
                fh = s.Meters.First(m => m.Key == "fh");
                sd = s.Meters.First(m => m.Key == "sd");
                t.Check("桌面版比較新：用量用桌面版的，重置時間仍用官方的", fh.Used == 10 && fh.ResetsAt == now.AddHours(3) && !fh.ResetApprox, fh.Used + " " + fh.ResetsAt);
                t.Check("狀態列沒有每週：每週照舊推算", sd.Used == 40 && sd.ResetApprox);
                t.Contains("註明重置時間是官方的", s.Note, "官方");

                s = new ClaudeProvider().Fetch(false, new AppSettings { ClaudeEstimate = false });
                fh = s.Meters.First(m => m.Key == "fh");
                t.Check("沒開橋接：完全照舊（推算的重置時間）", fh.Used == 10 && fh.ResetApprox);
            });
        }
    }
}
