using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>"Done / waiting for you" (#14): the hook commands, the events file, Claude Code's hooks and Codex's notify.</summary>
    static class AgentHookTests
    {
        static string Iso(DateTime t) { return t.ToString("o", System.Globalization.CultureInfo.InvariantCulture); }

        public static void Run(TestKit t)
        {
            string root = Path.Combine(Path.GetTempPath(), "sentripet-hooks-test");
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                AgentHooks.ClaudeDirOverride = Path.Combine(root, "claude");
                AgentHooks.CodexHomeOverride = Path.Combine(root, "codex");
                Directory.CreateDirectory(AgentHooks.ClaudeDirOverride);
                Directory.CreateDirectory(AgentHooks.CodexHomeOverride);
                Input(t, root);
                Events(t);
                Toml(t);
                ClaudeSettings(t);
                CodexConfig(t, root);
                CodexApp(t, root);
            }
            finally
            {
                AgentHooks.ClaudeDirOverride = null;
                AgentHooks.CodexHomeOverride = null;
                try { Directory.Delete(root, true); } catch { }
            }
        }

        static void Input(TestKit t, string root)
        {
            t.Section("AI 做完／在等你（#14）：hook 的輸入");
            t.Run("Hook input", () =>
            {
                var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
                // a transcript: the prompt 2 minutes ago, tool results after it
                string transcript = Path.Combine(root, "t.jsonl");
                File.WriteAllLines(transcript, new[]
                {
                    "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"earlier prompt\"},\"timestamp\":\"" + Iso(now.AddMinutes(-30)) + "\"}",
                    "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"refactor it\"}]},\"timestamp\":\"" + Iso(now.AddSeconds(-120)) + "\"}",
                    "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\"}]},\"timestamp\":\"" + Iso(now.AddSeconds(-100)) + "\"}",
                    "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\"}]},\"timestamp\":\"" + Iso(now.AddSeconds(-10)) + "\"}",
                    "{\"type\":\"user\",\"isMeta\":true,\"message\":{\"role\":\"user\",\"content\":\"<command>\"},\"timestamp\":\"" + Iso(now.AddSeconds(-5)) + "\"}",
                });
                var stop = AgentHooks.FromClaude(Json.Parse("{\"hook_event_name\":\"Stop\",\"cwd\":\"C:/work/my-app\",\"transcript_path\":\"" + transcript.Replace("\\", "\\\\") + "\"}"), now);
                t.Check("Stop：做完了，從最後一次提問算花了 120 秒（不算工具結果）、專案名稱", stop != null && stop.Kind == AgentEvent.Done && Math.Abs(stop.Seconds - 120) < 0.5 && stop.Project == "my-app" && stop.Source == "claude",
                    stop == null ? "null" : stop.Kind + " " + stop.Seconds + " " + stop.Project);
                t.Check("Stop：沒有對話紀錄時時間未知", AgentHooks.FromClaude(Json.Parse("{\"hook_event_name\":\"Stop\"}"), now).Seconds == -1);
                t.Check("Stop：另一個 hook 讓它繼續時不算", AgentHooks.FromClaude(Json.Parse("{\"hook_event_name\":\"Stop\",\"stop_hook_active\":true}"), now) == null);
                Func<string, string> kind = type =>
                {
                    var e = AgentHooks.FromClaude(Json.Parse("{\"hook_event_name\":\"Notification\",\"notification_type\":\"" + type + "\",\"message\":\"x\"}"), now);
                    return e == null ? "null" : e.Kind;
                };
                t.Equal("Notification：要你確認", AgentEvent.Permission, kind("permission_prompt"));
                t.Equal("Notification：在等你輸入", AgentEvent.Waiting, kind("idle_prompt"));
                t.Equal("Notification：其他種類不理", "null", kind("auth_success"));
                var old = AgentHooks.FromClaude(Json.Parse("{\"hook_event_name\":\"Notification\",\"message\":\"Claude needs your permission to use Bash\"}"), now);
                t.Check("舊版 Claude Code（沒有種類）：從訊息判斷", old != null && old.Kind == AgentEvent.Permission);
                t.Check("其他事件不理", AgentHooks.FromClaude(Json.Parse("{\"hook_event_name\":\"PreToolUse\"}"), now) == null && AgentHooks.FromClaude(null, now) == null);

                // Codex: the session log of the thread says when the turn started
                string day = Path.Combine(AgentHooks.CodexHome, "sessions", "2026", "09", "29");
                Directory.CreateDirectory(day);
                File.WriteAllLines(Path.Combine(day, "rollout-2026-09-29T19-00-00-0199aaaa-bbbb-7ccc-8ddd-eeeeffff0000.jsonl"), new[]
                {
                    "{\"timestamp\":\"" + Iso(now.AddMinutes(-20)) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}",
                    "{\"timestamp\":\"" + Iso(now.AddMinutes(-19)) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}",
                    "{\"timestamp\":\"" + Iso(now.AddSeconds(-300)) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}",
                    "{\"timestamp\":\"" + Iso(now.AddSeconds(-2)) + "\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\"}}",
                });
                var cx = AgentHooks.FromCodex(Json.Parse("{\"type\":\"agent-turn-complete\",\"thread-id\":\"0199aaaa-bbbb-7ccc-8ddd-eeeeffff0000\",\"cwd\":\"/home/me/api\",\"last-assistant-message\":\"done\"}"), now, AgentHooks.CodexHome);
                t.Check("Codex：做完了，從這回合開始算花了 300 秒、專案名稱", cx != null && cx.Kind == AgentEvent.Done && Math.Abs(cx.Seconds - 300) < 0.5 && cx.Project == "api" && cx.Source == "codex",
                    cx == null ? "null" : cx.Seconds + " " + cx.Project);
                var unknown = AgentHooks.FromCodex(Json.Parse("{\"type\":\"agent-turn-complete\",\"thread-id\":\"nope\"}"), now, AgentHooks.CodexHome);
                t.Check("Codex：找不到紀錄時時間未知", unknown != null && unknown.Seconds == -1);
                t.Check("Codex：其他事件不理", AgentHooks.FromCodex(Json.Parse("{\"type\":\"something-else\"}"), now, AgentHooks.CodexHome) == null);

                var done = new AgentEvent { Kind = AgentEvent.Done, Seconds = 192, Project = "my-app" };
                t.Contains("台詞：做完了、專案與花的時間", Lines.Agent(done, "Claude"), "my-app");
                t.Contains("台詞：花的時間", Lines.Agent(done, "Claude"), Fmt.Span(192));
                t.Contains("台詞：在等你確認", Lines.Agent(new AgentEvent { Kind = AgentEvent.Permission }, "Claude"), "Claude");
            });
        }

        static void Events(TestKit t)
        {
            t.Run("Events file", () =>
            {
                string file = AgentHooks.ClaudeEventsFile;
                if (File.Exists(file)) File.Delete(file);
                AgentHooks.Append(file, new AgentEvent { Source = "claude", Kind = AgentEvent.Done, At = DateTime.UtcNow.AddMinutes(-1), Seconds = 50 });
                var reader = new AgentHooks.Reader(file);
                t.Equal("開始時已經在檔案裡的是舊消息，不報", 0, reader.ReadNew().Count);
                var sw = new StringWriter();
                int code = AgentHooks.RunClaude(new StringReader("{\"hook_event_name\":\"Notification\",\"notification_type\":\"idle_prompt\",\"cwd\":\"/p/web\"}"), DateTime.UtcNow);
                var got = reader.ReadNew();
                t.Check("hook 指令：結束碼 0、寫進事件檔、讀得到", code == 0 && got.Count == 1 && got[0].Kind == AgentEvent.Waiting && got[0].Project == "web" && got[0].Source == "claude");
                t.Equal("讀過的不會再讀", 0, reader.ReadNew().Count);
                // a big file is cut down to its last lines
                var sb = new StringBuilder();
                for (int i = 0; i < 1200; i++) sb.Append("{\"at\":\"" + Iso(DateTime.UtcNow) + "\",\"source\":\"claude\",\"kind\":\"done\",\"seconds\":1}\n");
                File.WriteAllText(file, sb.ToString());
                var reader2 = new AgentHooks.Reader(file);
                reader2.ReadNew();
                AgentHooks.Append(file, new AgentEvent { Source = "claude", Kind = AgentEvent.Permission, At = DateTime.UtcNow });
                var after = reader2.ReadNew();
                t.Check("事件檔太大時只留最後幾行，新的照樣讀得到", new FileInfo(file).Length < 8 * 1024 && after.Count >= 1 && after[after.Count - 1].Kind == AgentEvent.Permission,
                    new FileInfo(file).Length + " bytes, " + after.Count + " new");
            });
        }

        static void Toml(TestKit t)
        {
            t.Run("config.toml", () =>
            {
                t.Check("沒有 notify", AgentHooks.FindNotify("model = \"o3\"\n[tui]\nnotifications = true\n") == null);
                var k = AgentHooks.FindNotify("model = \"o3\"\nnotify = [\"a\", \"b\"]\n[tui]\nnotifications = true\n");
                t.Check("找到最上層的 notify", k != null && k.Value == "[\"a\", \"b\"]", k == null ? "null" : k.Value);
                t.Check("表格裡的 notify 不算（不是最上層）", AgentHooks.FindNotify("[profiles.x]\nnotify = [\"a\"]\n") == null);
                var multi = AgentHooks.FindNotify("notify = [\n  \"python3\", # the chime\n  \"/p/[x].py\",\n]\nmodel = \"o3\"\n");
                var arr = multi != null ? AgentHooks.ParseTomlArray(multi.Value) : null;
                t.Check("跨行、有註解、字串裡有中括號", arr != null && arr.Count == 2 && arr[0] == "python3" && arr[1] == "/p/[x].py", arr == null ? "null" : string.Join("|", arr));
                var lit = AgentHooks.ParseTomlArray("['C:\\Tools\\chime.exe', \"caf\\u00e9\", \"a\\\"b\"]");
                t.Check("字面字串（Windows 路徑）與跳脫字元", lit != null && lit[0] == "C:\\Tools\\chime.exe" && lit[1] == "café" && lit[2] == "a\"b", lit == null ? "null" : string.Join("|", lit));
                t.Check("不是字串陣列：看不懂", AgentHooks.ParseTomlArray("\"just a string\"") == null && AgentHooks.ParseTomlArray("[1, 2]") == null);
                // what the Codex desktop app writes (computer use): blank lines before, tables with literal Windows paths after
                string app = "model = \"gpt-6\"\n\n\n\nnotify = [ \"C:\\\\Users\\\\me\\\\AppData\\\\Local\\\\OpenAI\\\\Codex\\\\runtimes\\\\cua_node\\\\b63e\\\\bin\\\\codex-computer-use.exe\", \"turn-ended\" ]\n\n" +
                             "[marketplaces.openai-bundled]\nsource_type = \"local\"\nsource = '\\\\?\\C:\\Users\\me\\.codex\\.tmp\\bundled'\n";
                var ak = AgentHooks.FindNotify(app);
                var aa = ak != null ? AgentHooks.ParseTomlArray(ak.Value) : null;
                t.Check("Codex 桌面版寫的 notify（computer use）：讀得出程式與參數", aa != null && aa.Count == 2 &&
                        aa[0] == "C:\\Users\\me\\AppData\\Local\\OpenAI\\Codex\\runtimes\\cua_node\\b63e\\bin\\codex-computer-use.exe" && aa[1] == "turn-ended",
                        aa == null ? "null" : string.Join(" | ", aa));
                var ours = AgentHooks.ParseTomlArray(AgentHooks.NotifyLine("C:\\Users\\me\\SentriPet.exe").Substring("notify = ".Length));
                t.Check("我們的 notify：程式路徑原樣、加上 --hook codex", ours != null && ours.Count == 3 && ours[0] == "C:\\Users\\me\\SentriPet.exe" && ours[1] == "--hook" && ours[2] == "codex");
            });
        }

        static void ClaudeSettings(TestKit t)
        {
            t.Run("Claude hooks", () =>
            {
                string file = AgentHooks.ClaudeSettingsFile;
                string original = "{\n  \"model\": \"opus\",\n  \"hooks\": {\n    \"Stop\": [ { \"hooks\": [ { \"type\": \"command\", \"command\": \"say done\" } ] } ]\n  }\n}\n";
                File.WriteAllText(file, original);
                var s = new AppSettings();
                string exe = Path.Combine(AppPaths.Home, "Apps", "SentriPet", "SentriPet.exe");
                AgentHooks.Connect(s, exe, true, false);
                var root = Json.Parse(File.ReadAllText(file));
                var stop = Json.Arr(Json.Path(root, "hooks.Stop"));
                var note = Json.Arr(Json.Path(root, "hooks.Notification"));
                t.Check("開啟：保留使用者自己的 Stop hook，再加上我們的", stop != null && stop.Count == 2 && Json.Str(Json.Get(Json.Arr(Json.Get(stop[0], "hooks"))[0], "command")) == "say done");
                t.Check("開啟：Notification 只接要你確認與在等你", note != null && note.Count == 1 && (Json.Str(Json.Get(note[0], "matcher")) ?? "").Contains("permission_prompt"));
                string cmd = Json.Str(Json.Get(Json.Arr(Json.Get(stop[1], "hooks"))[0], "command"));
                t.Check("指令：~/… 路徑加上 --hook claude、逾時 10 秒", cmd == "~/Apps/SentriPet/SentriPet.exe --hook claude" && Json.Num(Json.Get(Json.Arr(Json.Get(stop[1], "hooks"))[0], "timeout")) == 10, cmd);
                t.Check("其他設定不動、原檔備份一次", Json.Str(Json.Get(root, "model")) == "opus" && File.ReadAllText(file + ".sentripet-backup") == original);
                t.Check("認得出已經開啟", AgentHooks.IsClaudeConnected() && s.AgentHooks);
                AgentHooks.Connect(s, exe, true, false);
                t.Equal("再開一次不會重複加", 2, Json.Arr(Json.Path(Json.Parse(File.ReadAllText(file)), "hooks.Stop")).Count);
                AgentHooks.Disconnect(s);
                var back = Json.Parse(File.ReadAllText(file));
                t.Check("關閉：只拿掉我們的，使用者的 Stop hook 還在、沒有空的 Notification", Json.Arr(Json.Path(back, "hooks.Stop")).Count == 1 && Json.Path(back, "hooks.Notification") == null && !s.AgentHooks && !AgentHooks.IsClaudeConnected());

                File.Delete(file);
                File.Delete(file + ".sentripet-backup");
                AgentHooks.Connect(s, exe, true, false);
                AgentHooks.Disconnect(s);
                t.Check("本來沒有 settings.json：關閉後不留下空的 hooks", Json.Get(Json.Parse(File.ReadAllText(file)), "hooks") == null);

                File.WriteAllText(file, "{ not json");
                bool threw = false;
                try { AgentHooks.Connect(new AppSettings(), exe, true, false); } catch { threw = true; }
                t.Check("看不懂的 settings.json：不修改", threw && File.ReadAllText(file) == "{ not json");
                File.Delete(file);
            });
        }

        static void CodexConfig(TestKit t, string root)
        {
            t.Run("Codex notify", () =>
            {
                string file = AgentHooks.CodexConfigFile;
                string exe = Path.Combine(root, "bin", "SentriPet.exe");
                string plain = "model = \"gpt-5\"\n\n[tui]\nnotifications = true\n";
                File.WriteAllText(file, plain);
                var s = new AppSettings();
                AgentHooks.Connect(s, exe, false, true);
                string text = File.ReadAllText(file);
                var k = AgentHooks.FindNotify(text);
                t.Check("沒有 notify：加在最上層（表格之前）", k != null && text.IndexOf("notify") < text.IndexOf("[tui]") && AgentHooks.ParseTomlArray(k.Value)[0] == exe && s.CodexNotifyChain == null);
                t.Check("認得出已經開啟、其他設定不動", AgentHooks.IsCodexConnected() && text.Contains("model = \"gpt-5\"") && text.Contains("[tui]\nnotifications = true"));
                AgentHooks.Disconnect(s);
                t.Equal("關閉：原封不動還原", plain, File.ReadAllText(file));

                string own = "notify = [\"python3\", \"/x/chime.py\"]\nmodel = \"m\"\n";
                File.WriteAllText(file, own);
                AgentHooks.Connect(s, exe, false, true);
                t.Check("原本有 notify：記下來（之後照樣執行），換成我們的", s.CodexNotifyChain == "[\"python3\", \"/x/chime.py\"]" && AgentHooks.IsCodexConnected(), s.CodexNotifyChain);
                AgentHooks.Connect(s, exe, false, true);
                t.Check("再開一次：不會把我們自己當成原本的", s.CodexNotifyChain == "[\"python3\", \"/x/chime.py\"]");
                AgentHooks.Disconnect(s);
                t.Equal("關閉：原本的 notify 放回去", own, File.ReadAllText(file));

                string app = "model = \"gpt-6\"\n\n\nnotify = [ \"C:\\\\Tools\\\\codex-computer-use.exe\", \"turn-ended\" ]\n\n[marketplaces.x]\nsource = '\\\\?\\C:\\x'\n";
                File.WriteAllText(file, app);
                AgentHooks.Connect(s, exe, false, true);
                var chain = AgentHooks.ParseTomlArray(s.CodexNotifyChain);
                t.Check("Codex 桌面版的 notify：記下來、之後照樣以同樣參數執行", chain != null && chain[0] == "C:\\Tools\\codex-computer-use.exe" && chain[1] == "turn-ended" &&
                                                                       File.ReadAllText(file).Contains("[marketplaces.x]\nsource = '\\\\?\\C:\\x'"));
                AgentHooks.Disconnect(s);
                t.Equal("Codex 桌面版的 notify：關閉後一字不差地放回去", app, File.ReadAllText(file));

                // the user's notify program still runs, with the same argument
                string marker = Path.Combine(root, "chained.txt");
                // (cmd's operators as separate arguments, so they are not quoted; the JSON lands after "rem")
                s.CodexNotifyChain = Os.Windows
                    ? "['cmd.exe', '/c', 'type', 'nul', '>', '" + marker + "', '&', 'rem']"
                    : "['/bin/sh', '-c', 'touch \"" + marker + "\"', 'sh']";
                int code = AgentHooks.RunCodex(new[] { "--hook", "codex", "{\"type\":\"agent-turn-complete\",\"cwd\":\"/w/site\"}" }, s, DateTime.UtcNow);
                for (int i = 0; i < 50 && !File.Exists(marker); i++) System.Threading.Thread.Sleep(100);
                var ev = AgentHooks.ParseLine(File.ReadAllLines(AgentHooks.CodexEventsFile).Last());
                t.Check("Codex hook：記下事件，也執行使用者原本的 notify 程式", code == 0 && ev != null && ev.Project == "site" && File.Exists(marker));

                // the program moved: Repair follows it and keeps the user's program
                File.WriteAllText(file, own);
                s = new AppSettings();
                AgentHooks.Connect(s, exe, false, true);
                string moved = Path.Combine(root, "new place", "SentriPet.exe");
                AgentHooks.Repair(s, moved);
                var arr = AgentHooks.ParseTomlArray(AgentHooks.FindNotify(File.ReadAllText(file)).Value);
                t.Check("程式搬家：notify 跟著改、原本的程式還記得", arr[0] == moved && s.CodexNotifyChain == "[\"python3\", \"/x/chime.py\"]" && s.AgentHooks);
                File.WriteAllText(file, own);
                AgentHooks.Repair(s, moved);
                t.Check("使用者自己拿掉了：開關跟著關", !s.AgentHooks && s.CodexNotifyChain == null);

                if (File.Exists(AppPaths.SettingsFile)) File.Delete(AppPaths.SettingsFile);
                new AppSettings { AgentHooks = true, AgentHookNotify = true, CodexNotifyChain = "[\"a\"]" }.Save();
                var r = AppSettings.Load();
                t.Check("設定檔：讀回開關與原本的 notify", r.AgentHooks && r.AgentHookNotify && r.CodexNotifyChain == "[\"a\"]");
                File.Delete(AppPaths.SettingsFile);
            });
        }

        static string Basic(string s) { return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        /// <summary>
        /// The Codex app's own notify program (codex-computer-use) lives in a version folder that its updates replace, and
        /// the app puts it first in notify again, running the one that was there (SentriPet) with --previous-notify.
        /// </summary>
        static void CodexApp(TestKit t, string root)
        {
            t.Run("Codex app notify", () =>
            {
                string file = AgentHooks.CodexConfigFile;
                string exe = Path.Combine(root, "bin", "SentriPet.exe");
                string runtimes = Path.Combine(root, "OpenAI", "Codex", "runtimes", "cua_node");
                string tail = Path.Combine("bin", "node_modules", "@oai", "sky", "bin", "windows", "codex-computer-use.exe");
                string old = Path.Combine(runtimes, "b63ee7ee40c23b77", tail), now1 = Path.Combine(runtimes, "3dd31cfff853001c", tail), now2 = Path.Combine(runtimes, "4aa0000000000000", tail);
                foreach (var f in new[] { now1, now2 }) { Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllText(f, ""); }
                Directory.SetLastWriteTimeUtc(Path.Combine(runtimes, "3dd31cfff853001c"), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc));
                Directory.SetLastWriteTimeUtc(Path.Combine(runtimes, "4aa0000000000000"), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

                t.Equal("換掉的版本資料夾：在最新的那個裡找到同一個程式", now1, AgentHooks.FindMoved(old));
                t.Check("只是檔案不見了、或沒有類似的資料夾：找不到", AgentHooks.FindMoved(Path.Combine(runtimes, "3dd31cfff853001c", "bin", "gone.exe")) == null &&
                                                                AgentHooks.FindMoved(Path.Combine(root, "nowhere", "v1", "x.exe")) == null);

                string ours = Json.Serialize(new List<object> { exe, "--hook", "codex" }, false);
                // what the Codex app wrote (basic strings, its own spacing)
                string wrapped = "model = \"gpt-6\"\nnotify = [ " + Basic(now1) + ", \"turn-ended\", \"--previous-notify\", " + Basic(ours) + " ]\n\n[features]\nx = true\n";
                File.WriteAllText(file, wrapped);
                var notify = AgentHooks.ReadCodexNotify();
                t.Check("Codex 桌面版把 SentriPet 包在裡面（--previous-notify）：還算開著", notify != null && notify.Count == 4 && notify[3] == ours && AgentHooks.IsCodexConnected());

                string stale = "[" + Basic(old) + ", \"turn-ended\"]";
                t.Check("記下的舊版程式：Codex 自己會先執行它，SentriPet 不再執行（不會重複、也不會找不到）", AgentHooks.ChainToRun(stale, notify) == null);
                var mine = new List<string> { exe, "--hook", "codex" };
                var follow = AgentHooks.ChainToRun(stale, mine);
                t.Check("SentriPet 在最前面時：換到新版資料夾裡的同一個程式、參數不變", follow != null && follow[0] == now1 && follow[1] == "turn-ended" && follow.Count == 2,
                    follow == null ? "null" : string.Join(" ", follow));
                t.Check("程式真的不見了：先不執行", AgentHooks.ChainToRun("[" + Basic(Path.Combine(root, "nowhere", "v1", "x.exe")) + "]", mine) == null);
                t.Check("會再呼叫 SentriPet 的程式：不執行（不會互相呼叫）", AgentHooks.ChainToRun("[" + Basic(now1) + ", \"turn-ended\", \"--previous-notify\", " + Basic(ours) + "]", mine) == null &&
                                                                      AgentHooks.ChainToRun("[" + Basic(exe) + ", \"--hook\", \"codex\"]", mine) == null);
                var chime = AgentHooks.ChainToRun("[\"python3\", \"/x/chime.py\"]", notify);
                t.Check("使用者自己的其他程式：照樣執行（程式名稱不是完整路徑就不檢查）", chime != null && chime[0] == "python3");

                // turning it on while the Codex app runs SentriPet: stays inside, never remembers the app's program (it would run SentriPet again)
                var s = new AppSettings { CodexNotifyChain = stale };
                AgentHooks.Connect(s, exe, false, true);
                t.Check("開啟：Codex 桌面版的 notify 不動、舊版程式不再記著", File.ReadAllText(file) == wrapped && s.CodexNotifyChain == null && s.AgentHooks, s.CodexNotifyChain);
                string moved = Path.Combine(root, "new place", "SentriPet.exe");
                AgentHooks.Repair(s, moved);
                var after = AgentHooks.ReadCodexNotify();
                var inner = after != null && after.Count == 4 ? Json.Arr(Json.TryParse(after[3])) : null;
                t.Check("程式搬家：只改 --previous-notify 裡的 SentriPet", after != null && after[0] == now1 && after[2] == "--previous-notify" && inner != null && Json.Str(inner[0]) == moved &&
                                                          File.ReadAllText(file).Contains("[features]\nx = true"), after == null ? "null" : string.Join(" | ", after));
                File.WriteAllText(file, wrapped);

                s = new AppSettings { AgentHooks = true, CodexNotifyChain = stale };
                AgentHooks.Repair(s, exe);
                t.Check("啟動時：Codex 自己執行的程式就不再記著；開關維持開著", s.AgentHooks && s.CodexNotifyChain == null && File.ReadAllText(file) == wrapped);

                s = new AppSettings { AgentHooks = true, CodexNotifyChain = stale };
                AgentHooks.Disconnect(s);
                var off = AgentHooks.ReadCodexNotify();
                t.Check("關閉：只拿掉 SentriPet，Codex 桌面版的 notify 留著", off != null && off.Count == 2 && off[0] == now1 && off[1] == "turn-ended" && !AgentHooks.IsCodexConnected() &&
                                                                     File.ReadAllText(file).StartsWith("model = \"gpt-6\"\nnotify = ") && File.ReadAllText(file).EndsWith("\n\n[features]\nx = true\n"),
                    off == null ? "null" : string.Join(" | ", off));
                File.WriteAllText(file, wrapped);
                s = new AppSettings { AgentHooks = true, CodexNotifyChain = "[\"python3\", \"/x/chime.py\"]" };
                AgentHooks.Disconnect(s);
                off = AgentHooks.ReadCodexNotify();
                t.Check("關閉：SentriPet 原本執行的其他程式交還給 Codex 桌面版執行", off != null && off.Count == 4 && off[3] == "[\"python3\",\"/x/chime.py\"]", off == null ? "null" : string.Join(" | ", off));

                // SentriPet first, the app's program in an old version folder: Repair follows it into the new one
                File.WriteAllText(file, "notify = [" + Basic(old) + ", \"turn-ended\"]\n");
                s = new AppSettings();
                AgentHooks.Connect(s, exe, false, true);
                var remembered = AgentHooks.ParseTomlArray(s.CodexNotifyChain);
                t.Check("開啟時記下的程式已經被換掉：記成新版資料夾裡的", remembered != null && remembered[0] == now1 && remembered[1] == "turn-ended", s.CodexNotifyChain);
                s.CodexNotifyChain = stale;
                t.Check("找得到新版：設定頁不提示", AgentHooks.MissingChain(s) == null);
                s.CodexNotifyChain = "[" + Basic(Path.Combine(root, "nowhere", "v1", "x.exe")) + "]";
                t.Equal("找不到：設定頁提示是哪個程式", Path.Combine(root, "nowhere", "v1", "x.exe"), AgentHooks.MissingChain(s));

                // a SentriPet started by its own chain does nothing (no loop, no second event)
                string events = AgentHooks.CodexEventsFile;
                try { File.Delete(events); } catch { }
                Environment.SetEnvironmentVariable("SENTRIPET_CODEX_CHAINED", "1");
                try { AgentHooks.RunCodex(new[] { "--hook", "codex", "{\"type\":\"agent-turn-complete\",\"cwd\":\"/w/site\"}" }, new AppSettings(), DateTime.UtcNow); }
                finally { Environment.SetEnvironmentVariable("SENTRIPET_CODEX_CHAINED", null); }
                t.Check("被自己串接的程式再叫起來：什麼都不做", !File.Exists(events));
            });
        }
    }
}
