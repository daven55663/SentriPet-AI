using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SentriPet
{
    /// <summary>Other accounts (#26): a Claude Code or Codex settings folder per extra pet.</summary>
    static class AccountTests
    {
        public static void Run(TestKit t)
        {
            t.Section("多帳號（#26）");
            try
            {
                t.Run("Adding", () => Adding(t));
                t.Run("Claude account", () => Claude(t));
                t.Run("Codex account", () => Codex(t));
            }
            finally
            {
                ClaudeStatusLine.FileOverride = null;
                ClaudeStatusLine.SettingsOverride = null;
            }
        }

        static void Adding(TestKit t)
        {
            var s = new AppSettings();
            string work = t.TempDir("claude-work"), home = t.TempDir("claude-home"), codex = t.TempDir("codex-work");
            t.Equal("找不到的資料夾：不能加", L.T("找不到這個資料夾"), AccountSetup.Problem(s, "claude", Path.Combine(work, "nope")));
            t.Check("預設的資料夾：不能再加一次", AccountSetup.Problem(s, "claude", AccountSetup.DefaultFolder("claude")) != null);
            var a = AccountSetup.Add(s, "claude", work);
            var b = AccountSetup.Add(s, "claude", home);
            var c = AccountSetup.Add(s, "codex", codex);
            t.Equal("編號、預設名稱", "claude-2 Claude 2, claude-3 Claude 3, codex-2 Codex 2", string.Join(", ", new[] { a, b, c }.Select(x => x.Id + " " + x.Name)));
            t.Check("每個帳號一個不同的顏色", a.Color != b.Color && b.Color != c.Color && a.Color == AccountSetup.Colors[0]);
            t.Equal("同一個資料夾不能加兩次", L.T("這個資料夾已經加過了"), AccountSetup.Problem(s, "claude", work + Path.DirectorySeparatorChar));
            t.Check("同一個資料夾可以同時當 Claude 和 Codex（不同的工具）", AccountSetup.Problem(s, "codex", work) == null);
            s.Accounts.Remove(b);
            t.Equal("刪掉後編號可以再用", "claude-3", AccountSetup.Add(s, "claude", t.TempDir("claude-three")).Id);

            a.Name = "公司";
            a.StatusLine = true;
            a.StatusLineChain = "{\"type\":\"command\",\"command\":\"echo hi\"}";
            s.Save();
            var loaded = AppSettings.Load();
            t.Equal("存檔後讀回：帳號、名稱、顏色、狀態列", "claude-2 公司 " + a.Color + " True echo hi|codex-2|claude-3",
                    string.Join("|", loaded.Accounts.Select(x => x.Id == "claude-2" ? x.Id + " " + x.Name + " " + x.Color + " " + x.StatusLine + " " + Json.Str(Json.Get(Json.TryParse(x.StatusLineChain), "command")) : x.Id)));

            var all = ProviderRegistry.CreateAll(s);
            t.Check("每個帳號都是一隻桌寵（名稱、顏色）", all.Any(p => p.Id == "claude-2" && p.Name == "公司" && p.Color.ToHex() == a.Color && p is ClaudeProvider) &&
                                                 all.Any(p => p.Id == "codex-2" && p is CodexProvider), string.Join(", ", all.Select(p => p.Id)));
            t.Check("沒有設定時沒有額外的帳號", !ProviderRegistry.CreateAll().Any(p => p.Id == "claude-2" || p.Id == "codex-2"));
        }

        static string Input(DateTime reset5, DateTime resetWeek)
        {
            return "{\"model\":{\"display_name\":\"Opus\"},\"rate_limits\":{\"five_hour\":{\"used_percentage\":40,\"resets_at\":" + TestKit.Unix(reset5) +
                   "},\"seven_day\":{\"used_percentage\":12.5,\"resets_at\":" + TestKit.Unix(resetWeek) + "}}}";
        }

        static void Claude(TestKit t)
        {
            string root = t.TempDir("accounts");
            ClaudeStatusLine.FileOverride = Path.Combine(root, "main-status.json");
            ClaudeStatusLine.SettingsOverride = Path.Combine(root, "main-settings.json");
            string dir = Path.Combine(root, "claude-work");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"theme\": \"dark\", \"statusLine\": { \"type\": \"command\", \"command\": \"echo my-own-line\", \"padding\": 1 } }");
            var s = new AppSettings();
            var a = AccountSetup.Add(s, "claude", dir);
            a.Name = "Work";

            string exe = Path.Combine(AppPaths.Home, ".local", "share", "sentripet", Os.Windows ? "SentriPet.exe" : "SentriPet");
            string cmd = ClaudeStatusLine.CommandFor(exe, a);
            t.Check("狀態列指令帶著這個帳號的資料夾", cmd.Contains(" --statusline --claude-dir ") && ClaudeStatusLine.IsOurs(cmd), cmd);

            var provider = new ClaudeProvider(a);
            var d = provider.Detect();
            t.Check("偵測到資料夾，提醒要連接狀態列", d.Installed && d.Hint != null);
            var snap = provider.Fetch(false, s);
            t.Check("還沒連接：說出要怎麼做", snap.Error != null && snap.Meters.Count == 0, snap.Error);

            ClaudeStatusLine.Connect(a, exe);
            var o = Json.Obj(Json.Parse(File.ReadAllText(Path.Combine(dir, "settings.json"))));
            t.Check("連接：這個資料夾的 settings.json 改成 SentriPet，其他設定和 padding 都留著",
                    Json.Str(Json.Path(o, "statusLine.command")) == cmd && Json.Num(Json.Path(o, "statusLine.padding")) == 1 && Json.Str(Json.Get(o, "theme")) == "dark");
            t.Check("原本的狀態列記在這個帳號", a.StatusLine && (a.StatusLineChain ?? "").Contains("my-own-line") && ClaudeStatusLine.IsConnected(a));
            t.Check("修改前先備份", File.Exists(Path.Combine(dir, "settings.json.sentripet-backup")));
            t.Check("預設帳號的 settings.json 沒被動到", !File.Exists(ClaudeStatusLine.SettingsOverride) && !ClaudeStatusLine.IsConnected());

            var now = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, 0, 0, DateTimeKind.Utc);
            var outp = new StringWriter();
            ClaudeStatusLine.Run(new StringReader(Input(now.AddHours(3), now.AddDays(4))), outp, s, dir);
            t.Check("這個帳號的數字存在它自己的資料夾", File.Exists(ClaudeStatusLine.DataFileFor(dir)) && !File.Exists(ClaudeStatusLine.FileOverride));
            t.Contains("顯示這個帳號原本的狀態列", outp.ToString(), "my-own-line");

            snap = provider.Fetch(false, s);
            var fh = snap.Meters.FirstOrDefault(m => m.Key == "fh");
            t.Check("這個帳號的桌寵：官方數字（5 小時用了 40%、每週 12.5%）", snap.Error == null && fh != null && Math.Abs(fh.Used - 40) < 0.01 &&
                    snap.Meters.Any(m => m.Key == "sd" && Math.Abs(m.Used - 12.5) < 0.01), snap.Error ?? string.Join(",", snap.Meters.Select(m => m.Key + "=" + m.Used)));

            ClaudeStatusLine.Disconnect(a);
            o = Json.Obj(Json.Parse(File.ReadAllText(Path.Combine(dir, "settings.json"))));
            t.Check("關掉：放回原本的狀態列", Json.Str(Json.Path(o, "statusLine.command")) == "echo my-own-line" && !a.StatusLine && a.StatusLineChain == null);

            a.StatusLine = true;
            ClaudeStatusLine.Repair(a, exe);
            t.Check("使用者自己拿掉了：啟動時發現、當成關掉", !a.StatusLine);
        }

        static void Codex(TestKit t)
        {
            string home = t.TempDir("codex-account");
            var now = DateTime.UtcNow;
            TestKit.WriteFile(Path.Combine(home, "sessions", "2026", "09", "30", "rollout-1.jsonl"),
                CodexTests.RateLine(now.AddMinutes(-5), null, null, 61, now.AddHours(2), 30, now.AddDays(3), null) + "\n");
            var s = new AppSettings { CodexLiveMinutes = 0 };
            var a = AccountSetup.Add(s, "codex", home);
            a.Name = "Codex 公司";
            var p = new CodexProvider(a);
            var d = p.Detect();
            t.Check("Codex 帳號：偵測到它的資料夾", d.Installed && d.Evidence.Any(e => e.Contains("Codex")), string.Join(", ", d.Evidence));
            var snap = p.Fetch(true, s);
            t.Check("讀這個帳號資料夾的 sessions（5 小時用了 61%）", snap.Error == null && snap.Meters.Any(m => m.WindowMinutes == 300 && Math.Abs(m.Used - 61) < 0.01),
                    snap.Error ?? string.Join(",", snap.Meters.Select(m => m.Key + "=" + m.Used)));
            p.Dispose();
            var empty = new CodexProvider(AccountSetup.Add(s, "codex", t.TempDir("codex-empty2")));
            t.Check("沒有紀錄的帳號：說出來、不會當掉", empty.Fetch(true, s).Error != null);
            empty.Dispose();
        }
    }
}
