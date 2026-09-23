using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>Claude: the transcript reader, the desktop history and the live estimate — all on sample files.</summary>
    static class ClaudeTests
    {
        static DateTime WholeSecond(DateTime t) { return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, DateTimeKind.Utc); }
        static DateTime Minute(DateTime t) { return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, DateTimeKind.Utc); }

        /// <summary>One assistant line as Claude Code writes it (only what the reader looks at, plus some content).</summary>
        internal static string Reply(string id, string req, DateTime at, string model, int input, int output, int cacheWrite, int cacheRead, string text)
        {
            var sb = new StringBuilder();
            sb.Append("{\"parentUuid\":\"p\",\"isSidechain\":false,\"type\":\"assistant\",\"timestamp\":\"").Append(TestKit.Iso(at)).Append('"');
            if (req != null) sb.Append(",\"requestId\":\"").Append(req).Append('"');
            sb.Append(",\"message\":{");
            if (id != null) sb.Append("\"id\":\"").Append(id).Append("\",");
            sb.Append("\"model\":\"").Append(model).Append("\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"").Append(text ?? "ok").Append("\"}],");
            sb.Append("\"usage\":{\"input_tokens\":").Append(input).Append(",\"output_tokens\":").Append(output)
              .Append(",\"cache_creation_input_tokens\":").Append(cacheWrite).Append(",\"cache_read_input_tokens\":").Append(cacheRead)
              .Append(",\"service_tier\":\"standard\"}}}");
            return sb.ToString();
        }

        static Dictionary<string, object> Sample(DateTime t, string org, double? fh, double? sd)
        {
            var u = new Dictionary<string, object>();
            if (fh.HasValue) u["fh"] = fh.Value;
            if (sd.HasValue) u["sd"] = sd.Value;
            var o = new Dictionary<string, object> { { "t", Json.ToUnixMs(t) }, { "u", u } };
            if (org != null) o["org"] = org;
            return o;
        }

        static void WriteHistory(string path, List<Dictionary<string, object>> samples)
        {
            TestKit.WriteFile(path, Json.Serialize(new Dictionary<string, object> { { "samples", samples } }, false));
        }

        static void WriteLines(string path, IEnumerable<string> lines)
        {
            TestKit.WriteFile(path, string.Join("\n", lines) + "\n");
        }

        public static void Run(TestKit t)
        {
            string oldConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            try
            {
                Transcripts(t);
                History(t);
                Estimate(t);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", oldConfig);
                ClaudeProvider.HistoryOverride = null;
            }
        }

        static void Transcripts(TestKit t)
        {
            t.Section("Claude Code 對話紀錄（只讀 token 數）");
            t.Run("ClaudeCodeUsage", () =>
            {
                t.Equal("加權：Opus 輸出 5 倍、寫快取 1.25 倍、讀快取 2%", 1000 + 500 + 125 + 20.0, ClaudeCodeUsage.Weight("claude-opus-4-1", 1000, 100, 100, 1000));
                t.Equal("加權：Sonnet 0.6 倍", 900.0, ClaudeCodeUsage.Weight("claude-sonnet-4-5", 1000, 100, 0, 0));
                t.Equal("加權：Haiku 0.2 倍", 200.0, ClaudeCodeUsage.Weight("claude-haiku-4-5", 1000, 0, 0, 0));

                string root = t.TempDir("claude-config");
                Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", root);
                t.Equal("對話紀錄資料夾跟著 CLAUDE_CONFIG_DIR", Path.Combine(root, "projects"), ClaudeCodeUsage.Root);
                var now = DateTime.UtcNow;
                string file = Path.Combine(root, "projects", "D--proj-a", "session-1.jsonl");
                var lines = new List<string>
                {
                    Reply("msg_a1", "req_a1", now.AddMinutes(-60), "claude-opus-4-1", 1000, 100, 0, 0, "first block"),
                    Reply("msg_a1", "req_a1", now.AddMinutes(-60), "claude-opus-4-1", 1000, 200, 0, 0, "second block, same reply"),
                    "{\"type\":\"user\",\"timestamp\":\"" + TestKit.Iso(now.AddMinutes(-50)) + "\",\"message\":{\"role\":\"user\",\"content\":\"\\\"usage\\\":{\\\"input_tokens\\\":999999}\"}}",
                    Reply("msg_syn", "req_syn", now.AddMinutes(-45), "<synthetic>", 5000, 0, 0, 0, null),
                    Reply("msg_a2", "req_a2", now.AddMinutes(-30), "claude-sonnet-4-5", 10000, 0, 0, 0, null),
                    Reply("msg_old", "req_old", now.AddDays(-4), "claude-opus-4-1", 70000, 0, 0, 0, "older than three days"),
                    Reply("msg_big", "req_big", now.AddMinutes(-20), "claude-haiku-4-5", 5000, 0, 0, 0, new string('x', 200000)),
                    Reply(null, null, now.AddMinutes(-10), "claude-opus-4-1", 300, 0, 0, 0, "no ids"),
                    "not json at all",
                    "",
                };
                WriteLines(file, lines);
                // a reply that is still being written: no newline yet
                File.AppendAllText(file, Reply("msg_part", "req_part", now.AddMinutes(-5), "claude-opus-4-1", 700, 0, 0, 0, null), new UTF8Encoding(false));

                var u = new ClaudeCodeUsage();
                u.Update();
                t.Equal("重複的回覆只算一次（取最大）、跳過使用者訊息與 synthetic、過期的", 4, u.Count);
                t.Near("總量：2000 + 6000 + 1000（200 KB 的長行）+ 300", 9300, u.Sum(now.AddHours(-2), now), 0.001);
                t.Close("第一筆回覆的時間", now.AddMinutes(-60), u.FirstAfter(now.AddHours(-2), now), TimeSpan.FromSeconds(1));
                t.Check("區間外沒有回覆", u.FirstAfter(now.AddMinutes(-9), now) == null);

                File.AppendAllText(file, "\n", new UTF8Encoding(false));
                u.Update();
                t.Equal("寫完的那一行下次讀到", 5, u.Count);
                t.Near("加上 700", 10000, u.Sum(now.AddHours(-2), now), 0.001);
                u.Update();
                t.Equal("沒有新內容：不重複計算", 5, u.Count);

                string file2 = Path.Combine(root, "projects", "D--proj-b", "session-2.jsonl");
                WriteLines(file2, new[] { Reply("msg_b1", "req_b1", now.AddMinutes(-15), "claude-opus-4-1", 111, 0, 0, 0, null) });
                string stale = Path.Combine(root, "projects", "D--proj-c", "session-3.jsonl");
                WriteLines(stale, new[] { Reply("msg_c1", "req_c1", now.AddMinutes(-15), "claude-opus-4-1", 5000, 0, 0, 0, null) });
                File.SetLastWriteTimeUtc(stale, now.AddDays(-4));
                u.Update();
                t.Equal("其他專案的紀錄也算；三天沒動的檔案不讀", 6, u.Count);

                WriteLines(file, new[] { Reply("msg_new", "req_new", now.AddMinutes(-2), "claude-opus-4-1", 50, 0, 0, 0, null) });
                u.Update();
                t.Equal("檔案被改短（重寫）：從頭再讀", 7, u.Count);
            });
        }

        static void History(TestKit t)
        {
            t.Section("Claude 桌面版用量紀錄");
            t.Run("History", () =>
            {
                var now = WholeSecond(DateTime.UtcNow);
                var list = new List<Dictionary<string, object>>
                {
                    Sample(now.AddMinutes(-30), "org-new", 5, 20),
                    Sample(now.AddMinutes(-90), "org-old", 50, 70),
                    Sample(now.AddMinutes(-60), null, 3, 18),
                    Sample(now.AddMinutes(-15), "org-new", 7, 21),
                };
                list.Add(new Dictionary<string, object> { { "t", Json.ToUnixMs(now) }, { "org", "org-new" } });   // no "u": ignored
                var parsed = ClaudeProvider.ParseHistory(Json.Serialize(new Dictionary<string, object> { { "samples", list } }, false));
                t.Equal("只留最新組織（和沒寫組織）的紀錄", 3, parsed.Count);
                t.Check("依時間排序", parsed.Count == 3 && parsed[0].T < parsed[1].T && parsed[1].T < parsed[2].T);
                t.Equal("空檔案 → 沒有紀錄", 0, ClaudeProvider.ParseHistory("").Count);

                t.Equal("整點四捨五入：10:29 → 10:00", new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc), ClaudeProvider.RoundToHour(new DateTime(2026, 9, 24, 10, 29, 0, DateTimeKind.Utc)));
                t.Equal("整點四捨五入：10:30 → 11:00", new DateTime(2026, 9, 24, 11, 0, 0, DateTimeKind.Utc), ClaudeProvider.RoundToHour(new DateTime(2026, 9, 24, 10, 30, 0, DateTimeKind.Utc)));

                // the weekly limit dropped back to zero at the same moment in three different weeks
                var r0 = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddDays(-20);
                var samples = new List<ClaudeProvider.Sample>();
                Action<DateTime, double> add = (at, v) =>
                {
                    var s = new ClaudeProvider.Sample { T = Json.ToUnixMs(at) };
                    s.U["sd"] = v;
                    samples.Add(s);
                };
                for (int k = 0; k < 3; k++)
                {
                    var rk = r0.AddDays(7 * k);
                    add(rk.AddDays(-3), 10);
                    add(rk.AddDays(-1), 30);
                    add(rk.AddMinutes(-10), 45);
                    add(rk.AddMinutes(5), 0);
                    add(rk.AddDays(1), 5);
                }
                var est = ClaudeProvider.EstimateFixedSchedule(samples, "sd", 10080);
                t.Equal("每週固定重置：從三次歸零推回同一個時刻（下一次）", r0.AddDays(21), ClaudeProvider.RoundToHour(est));
                t.Check("沒有歸零紀錄：推不出固定時刻", ClaudeProvider.EstimateFixedSchedule(samples.Take(3).ToList(), "sd", 10080) == null);

                DateTime? next;
                var local = DateTime.Now;
                foreach (var c in new[] { new object[] { "Thu 23:00", DayOfWeek.Thursday, 23, 0 }, new object[] { "週四 23:00", DayOfWeek.Thursday, 23, 0 },
                                          new object[] { "fri 4pm", DayOfWeek.Friday, 16, 0 }, new object[] { "星期日 9:30", DayOfWeek.Sunday, 9, 30 } })
                {
                    bool ok = ClaudeProvider.TryParseWeekly((string)c[0], out next);
                    var l = next.HasValue ? next.Value.ToLocalTime() : DateTime.MinValue;
                    t.Check("每週重置時間「" + c[0] + "」", ok && l.DayOfWeek == (DayOfWeek)c[1] && l.Hour == (int)c[2] && l.Minute == (int)c[3] && l > local && l <= local.AddDays(7),
                        next.HasValue ? l.ToString("ddd HH:mm") : "無法解析");
                }
                t.Check("每週重置時間：亂寫、25 點、空白都不接受",
                    !ClaudeProvider.TryParseWeekly("abc", out next) && !ClaudeProvider.TryParseWeekly("Thu 25:00", out next) && !ClaudeProvider.TryParseWeekly("", out next));
            });
        }

        static void Estimate(TestKit t)
        {
            t.Section("Claude 即時推算（桌面版紀錄 + 對話紀錄）");
            t.Run("Estimate", () =>
            {
                var now = WholeSecond(DateTime.UtcNow);
                string root = t.TempDir("claude-live");
                Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", root);
                string history = Path.Combine(root, "plan-usage-history.json");
                ClaudeProvider.HistoryOverride = history;
                string transcript = Path.Combine(root, "projects", "D--work", "s.jsonl");

                // desktop samples every 15 minutes; between two samples Claude Code used 200k weighted tokens
                var t0 = now.AddMinutes(-70);
                var samples = new List<Dictionary<string, object>>();
                var replies = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    var ti = t0.AddMinutes(15 * i);
                    samples.Add(Sample(ti, "org", 2 * i, 20 + 1.5 * i));
                    if (i < 4)
                    {
                        replies.Add(Reply("msg_" + i + "a", "req_" + i + "a", ti.AddMinutes(3), "claude-opus-4-1", 100000, 0, 0, 0, null));
                        replies.Add(Reply("msg_" + i + "b", "req_" + i + "b", ti.AddMinutes(9), "claude-opus-4-1", 100000, 0, 0, 0, null));
                    }
                }
                var last = t0.AddMinutes(60);
                // after the last desktop sample: 300k more
                replies.Add(Reply("msg_5a", "req_5a", last.AddMinutes(2), "claude-opus-4-1", 150000, 0, 0, 0, null));
                replies.Add(Reply("msg_5b", "req_5b", last.AddMinutes(5), "claude-opus-4-1", 150000, 0, 0, 0, null));
                WriteHistory(history, samples);
                WriteLines(transcript, replies);

                var p = new ClaudeProvider();
                var s = p.Fetch(false, new AppSettings());
                var fh = s.Meters.FirstOrDefault(m => m.Key == "fh");
                var sd = s.Meters.FirstOrDefault(m => m.Key == "sd");
                t.Check("有 5 小時和每週兩個額度（依序）", s.Meters.Count == 2 && s.Meters[0] == fh && s.Meters[1] == sd && fh.Label == "5 小時" && sd.ShortLabel == "週");
                t.Near("校準：每 1% ≈ 10 萬加權 token（5 小時）", 1e-5, p.LastK5 ?? 0, 1e-9);
                t.Near("校準：每週", 7.5e-6, p.LastK7 ?? 0, 1e-9);
                t.Near("5 小時：8% + 之後的 30 萬 token ≈ 11%", 11, fh.Used, 0.01);
                t.Near("每週：26% + 2.25% ≈ 28.25%", 28.25, sd.Used, 0.01);
                t.Check("推算的數字有標記", fh.UsedApprox && sd.UsedApprox && s.Source == "即時推算" && !s.Stale);
                t.Equal("5 小時重置 = 第一則回覆（取到分鐘）+ 5 小時", Minute(t0.AddMinutes(3)).AddHours(5), fh.ResetsAt);
                t.Equal("每週重置：推到整點", ClaudeProvider.RoundToHour(t0.AddMinutes(-7.5).AddDays(7)), sd.ResetsAt);
                t.Contains("說明文字", s.Note, "推算");
                p.Dispose();

                var off = new ClaudeProvider();
                var s2 = off.Fetch(false, new AppSettings { ClaudeEstimate = false });
                var fh2 = s2.Meters.First(m => m.Key == "fh");
                t.Check("關掉推算：只用桌面版的數字", fh2.Used == 8 && !fh2.UsedApprox && s2.Source == "Claude 桌面版快取" && off.LastK5 == null);
                t.Equal("關掉推算：重置時間取兩筆紀錄中間", t0.AddMinutes(7.5).AddHours(5), fh2.ResetsAt);
                off.Dispose();

                var manual = new ClaudeProvider();
                var s3 = manual.Fetch(false, new AppSettings { ClaudeWeeklyReset = "週四 23:00" });
                DateTime? thu;
                ClaudeProvider.TryParseWeekly("週四 23:00", out thu);
                var sd3 = s3.Meters.First(m => m.Key == "sd");
                t.Check("手動填的每週重置時間", !sd3.ResetApprox && thu.HasValue && sd3.ResetsAt.HasValue && Math.Abs((sd3.ResetsAt.Value - thu.Value).TotalMinutes) < 1);
                manual.Dispose();

                // the desktop app has been closed for 5½ hours: the 5-hour window ended in the meantime
                string root2 = t.TempDir("claude-closed");
                Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", root2);
                string history2 = Path.Combine(root2, "plan-usage-history.json");
                ClaudeProvider.HistoryOverride = history2;
                var s0 = now.AddHours(-6);
                WriteHistory(history2, new List<Dictionary<string, object>>
                {
                    Sample(s0, null, 0, 10), Sample(s0.AddMinutes(15), null, 10, 12), Sample(s0.AddMinutes(30), null, 20, 14),
                });
                WriteLines(Path.Combine(root2, "projects", "D--w", "s.jsonl"), new[]
                {
                    Reply("msg_c0", "req_c0", s0.AddMinutes(5), "claude-opus-4-1", 100000, 0, 0, 0, null),
                    Reply("msg_c1", "req_c1", s0.AddMinutes(20), "claude-opus-4-1", 100000, 0, 0, 0, null),
                    Reply("msg_c2", "req_c2", now.AddMinutes(-20), "claude-opus-4-1", 50000, 0, 0, 0, null),
                });
                var closed = new ClaudeProvider();
                var s4 = closed.Fetch(false, new AppSettings());
                var fh4 = s4.Meters.First(m => m.Key == "fh");
                var sd4 = s4.Meters.First(m => m.Key == "sd");
                t.Check("5 小時在桌面版關著時結束了：歸零", fh4.WasReset);
                t.Near("新的 5 小時只算結束後的用量（5 萬 token ≈ 5%）", 5, fh4.Used, 0.01);
                t.Equal("新的 5 小時從結束後第一則回覆起算", Minute(now.AddMinutes(-20)).AddHours(5), fh4.ResetsAt);
                t.Near("每週照常累加：14% + 1%", 15, sd4.Used, 0.01);
                t.Check("桌面版太久沒更新：標成舊資料", s4.Stale && s4.Note.Contains("沒在執行"), s4.Note);
                closed.Dispose();

                ClaudeProvider.HistoryOverride = Path.Combine(root2, "missing.json");
                var missing = new ClaudeProvider().Fetch(false, new AppSettings());
                t.Check("找不到紀錄：說明原因", !missing.Meters.Any() && missing.Error.Contains("找不到用量紀錄"), missing.Error);
                TestKit.WriteFile(history2, "{\"samples\":[]}");
                ClaudeProvider.HistoryOverride = history2;
                var empty = new ClaudeProvider().Fetch(false, new AppSettings());
                t.Check("紀錄是空的：說明原因", empty.Error != null && empty.Error.Contains("空的"), empty.Error);
            });
        }
    }

    /// <summary>Codex: the session logs and the official app-server protocol (against a fake server).</summary>
    static class CodexTests
    {
        static string RateLine(DateTime at, string limitId, string limitName, double used5, DateTime reset5, double usedWeek, DateTime resetWeek, string extras)
        {
            return "{\"timestamp\":\"" + TestKit.Iso(at) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null,\"rate_limits\":{" +
                   (limitId != null ? "\"limit_id\":\"" + limitId + "\"," : "") + (limitName != null ? "\"limit_name\":\"" + limitName + "\"," : "") +
                   "\"primary\":{\"used_percent\":" + used5.ToString(CultureInfo.InvariantCulture) + ",\"window_minutes\":300,\"resets_at\":" + TestKit.Unix(reset5) + "}," +
                   "\"secondary\":{\"used_percent\":" + usedWeek.ToString(CultureInfo.InvariantCulture) + ",\"window_minutes\":10080,\"resets_at\":" + TestKit.Unix(resetWeek) + "}" +
                   (extras ?? "") + "}}}";
        }

        public static void Run(TestKit t)
        {
            string oldHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            try
            {
                Logs(t);
                Live(t);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CODEX_HOME", oldHome);
                CodexProvider.ExeOverride = null;
                CodexProvider.ArgsOverride = null;
            }
        }

        static void Logs(TestKit t)
        {
            t.Section("Codex 本機紀錄（~/.codex/sessions）");
            t.Run("CodexLogs", () =>
            {
                var now = DateTime.UtcNow;
                var local = new AppSettings { CodexLiveMinutes = 0 };
                string home = t.TempDir("codex-home");
                Environment.SetEnvironmentVariable("CODEX_HOME", home);
                string day = Path.Combine(home, "sessions", "2026", "09", "24");
                TestKit.WriteFile(Path.Combine(day, "rollout-1.jsonl"), string.Join("\n", new[]
                {
                    "{\"timestamp\":\"" + TestKit.Iso(now.AddHours(-3)) + "\",\"type\":\"session_meta\",\"payload\":{\"id\":\"x\"}}",
                    RateLine(now.AddHours(-2), null, null, 10, now.AddHours(2), 30, now.AddDays(3), ",\"plan_type\":\"plus\""),
                    RateLine(now.AddHours(-1), null, null, 12, now.AddHours(2), 31, now.AddDays(3),
                        ",\"plan_type\":\"plus\",\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":\"12.5\"},\"rate_limit_reached_type\":\"primary_window\""),
                    RateLine(now.AddHours(-1), "codex_bengalfox", "GPT-5.3-Codex-Spark", 50, now.AddHours(1), 5, now.AddDays(2), null),
                }) + "\n");

                var p = new CodexProvider();
                var s = p.Fetch(false, local);
                t.Check("讀到最新一筆（兩個方案額度）", s.Meters.Count == 4, s.Error ?? s.Meters.Count + " 個額度");
                var m5 = s.Meters.FirstOrDefault(m => m.Key == "codex:300");
                var mw = s.Meters.FirstOrDefault(m => m.Key == "codex:10080");
                t.Check("主要額度在前、數字是最新的", s.Meters.Count > 1 && s.Meters[0] == m5 && m5.Used == 12 && mw != null && mw.Used == 31);
                t.Close("重置時間", now.AddHours(2), m5.ResetsAt, TimeSpan.FromSeconds(2));
                t.Check("其他模型的額度加上名稱", s.Meters.Any(m => m.Key == "codex_bengalfox:300" && m.Label == "GPT-5.3-Codex-Spark 5 小時"));
                t.Equal("方案", "Plus", s.Plan);
                t.Check("額外點數與已達上限寫在說明", s.Note != null && s.Note.Contains("額外點數：12.5") && s.Note.Contains("已達上限：primary window"), s.Note);
                t.Close("資料時間", now.AddHours(-1), s.ObservedAt, TimeSpan.FromSeconds(2));
                t.Equal("來源", "Codex 本機紀錄", s.Source);
                p.Dispose();

                // windows that ended after the log line was written
                string home2 = t.TempDir("codex-expired");
                Environment.SetEnvironmentVariable("CODEX_HOME", home2);
                TestKit.WriteFile(Path.Combine(home2, "sessions", "a", "rollout-2.jsonl"), RateLine(now.AddDays(-2), null, null, 80, now.AddHours(-30), 60, now.AddHours(-1), null) + "\n");
                var e = new CodexProvider().Fetch(false, local);
                var e5 = e.Meters.First(m => m.WindowMinutes == 300);
                var ew = e.Meters.First(m => m.WindowMinutes == 10080);
                t.Check("5 小時已過：歸零、等下次使用才開始計時", e5.Used == 0 && e5.WasReset && e5.ResetsAt == null);
                t.Check("每週已過：歸零、往後推一週", ew.Used == 0 && ew.WasReset && ew.ResetApprox && ew.ResetsAt > now && ew.ResetsAt <= now.AddDays(7));

                // the rate-limit line is far from the end of a big log
                string home3 = t.TempDir("codex-big");
                Environment.SetEnvironmentVariable("CODEX_HOME", home3);
                var big = new StringBuilder();
                big.Append(RateLine(now.AddMinutes(-30), null, null, 7, now.AddHours(3), 9, now.AddDays(4), null)).Append('\n');
                string filler = "{\"timestamp\":\"" + TestKit.Iso(now) + "\",\"type\":\"response_item\",\"payload\":{\"text\":\"" + new string('y', 900) + "\"}}\n";
                while (big.Length < 800 * 1024) big.Append(filler);
                TestKit.WriteFile(Path.Combine(home3, "sessions", "b", "rollout-3.jsonl"), big.ToString());
                var b = new CodexProvider().Fetch(false, local);
                t.Check("大檔案：往前多讀一段也找得到", b.Meters.Any(m => m.Key == "codex:300" && m.Used == 7), b.Error);

                Environment.SetEnvironmentVariable("CODEX_HOME", t.TempDir("codex-empty"));
                var none = new CodexProvider().Fetch(false, local);
                t.Check("還沒有紀錄：說明原因", none.Meters.Count == 0 && none.Error != null && none.Error.Contains("還沒有用量紀錄"), none.Error);
            });
        }

        static void Live(TestKit t)
        {
            t.Section("Codex 官方 app-server（JSON-RPC，對假伺服器）");
            t.Run("CodexLive", () =>
            {
                var now = DateTime.UtcNow;
                string dir = t.TempDir("codex-live");
                Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(dir, "home"));
                CodexProvider.ExeOverride = Process.GetCurrentProcess().MainModule.FileName;
                var live = new AppSettings { CodexLiveMinutes = 5 };

                string single = Path.Combine(dir, "single.json");
                TestKit.WriteFile(single, "{\n \"rateLimits\": {\n  \"primary\": {\"usedPercent\": 25, \"windowDurationMins\": 300, \"resetsAt\": " + TestKit.Unix(now.AddHours(2)) + "},\n" +
                    "  \"secondary\": {\"usedPercent\": 40, \"windowDurationMins\": 10080, \"resetsAt\": " + TestKit.Unix(now.AddDays(3)) + "},\n  \"planType\": \"pro\"\n }\n}");
                CodexProvider.ArgsOverride = "--fake-codex-app-server \"" + single + "\"";
                var sw = Stopwatch.StartNew();
                var s = new CodexProvider().Fetch(true, live);
                t.Check("即時查詢成功", s.Error == null && s.Source == "Codex 官方 app-server", (s.Error ?? s.Source) + "（" + sw.ElapsedMilliseconds + " ms）");
                t.Check("數字與方案", s.Meters.Count == 2 && s.Meters[0].Used == 25 && s.Meters[1].Used == 40 && s.Plan == "Pro");
                t.Close("重置時間", now.AddHours(2), s.Meters.Count > 0 ? s.Meters[0].ResetsAt : null, TimeSpan.FromSeconds(2));

                string byId = Path.Combine(dir, "byid.json");
                TestKit.WriteFile(byId, "{\"rateLimitsByLimitId\":{\"codex\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":10,\"windowDurationMins\":300}," +
                    "\"secondary\":{\"usedPercent\":20,\"windowDurationMins\":10080}},\"codex_bengalfox\":{\"limitId\":\"codex_bengalfox\",\"limitName\":\"GPT-5.3-Codex-Spark\"," +
                    "\"primary\":{\"usedPercent\":70,\"windowDurationMins\":300}}}}");
                CodexProvider.ArgsOverride = "--fake-codex-app-server \"" + byId + "\"";
                var m = new CodexProvider().Fetch(true, live);
                t.Check("多個模型額度（rateLimitsByLimitId）", m.Meters.Count == 3 && m.Meters.Any(x => x.Key == "codex_bengalfox:300" && x.Used == 70), m.Error);

                string none = Path.Combine(dir, "none.json");
                TestKit.WriteFile(none, "{\"rateLimits\":null}");
                CodexProvider.ArgsOverride = "--fake-codex-app-server \"" + none + "\"";
                var n = new CodexProvider().Fetch(true, live);
                t.Check("帳號沒有回傳用量：說明原因", n.Meters.Count == 0 && n.Error != null && n.Error.Contains("沒有回傳用量"), n.Error);

                // both sources: the newer one wins; if the live query fails the log is shown with a note
                string home = Path.Combine(dir, "home");
                TestKit.WriteFile(Path.Combine(home, "sessions", "x", "rollout.jsonl"), RateLine(now.AddHours(-1), null, null, 3, now.AddHours(4), 4, now.AddDays(5), null) + "\n");
                CodexProvider.ArgsOverride = "--fake-codex-app-server \"" + single + "\"";
                var both = new CodexProvider().Fetch(true, live);
                t.Check("本機紀錄較舊：用即時的", both.Source == "Codex 官方 app-server" && both.Meters[0].Used == 25);
                CodexProvider.ExeOverride = Path.Combine(dir, "no-such-codex.exe");
                var fallback = new CodexProvider().Fetch(true, live);
                t.Check("即時查詢失敗：改用本機紀錄並註明", fallback.Source == "Codex 本機紀錄" && fallback.Meters[0].Used == 3 && fallback.Note != null && fallback.Note.Contains("即時查詢失敗"), fallback.Note);
            });
        }
    }
}
