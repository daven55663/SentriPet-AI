using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentriPet
{
    /// <summary>The usage report (2.3): the token ledger (#18) and how numbers of tokens are written.</summary>
    static class ReportTests
    {
        static string Iso(DateTime t) { return t.ToString("o", System.Globalization.CultureInfo.InvariantCulture); }

        static string Reply(DateTime at, string id, string model, int input, int output, int write, int read, string cwd)
        {
            return "{\"type\":\"assistant\",\"cwd\":\"" + cwd.Replace("\\", "\\\\") + "\",\"message\":{\"id\":\"msg_" + id + "\",\"model\":\"" + model +
                   "\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output + ",\"cache_creation_input_tokens\":" + write +
                   ",\"cache_read_input_tokens\":" + read + "}},\"requestId\":\"req_" + id + "\",\"timestamp\":\"" + Iso(at) + "\"}";
        }

        static string Tokens(DateTime at, int input, int cached, int output)
        {
            return "{\"timestamp\":\"" + Iso(at) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":" + input +
                   ",\"cached_input_tokens\":" + cached + ",\"cache_write_input_tokens\":0,\"output_tokens\":" + output + ",\"reasoning_output_tokens\":0,\"total_tokens\":" + (input + output) + "}}}}";
        }

        public static void Run(TestKit t)
        {
            t.Section("用量報告（#18）：token 帳本");
            string root = Path.Combine(Path.GetTempPath(), "sentripet-ledger-test");
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                string claude = Path.Combine(root, "projects"), codex = Path.Combine(root, "sessions");
                Directory.CreateDirectory(Path.Combine(claude, "D--work-shop"));
                Directory.CreateDirectory(Path.Combine(codex, "2026", "09", "28"));
                t.Run("Ledger", () => Ledger(t, claude, codex));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
            t.Run("Token numbers", () => Numbers(t));
        }

        static void Ledger(TestKit t, string claude, string codex)
        {
            var now = DateTime.UtcNow;
            string transcript = Path.Combine(claude, "D--work-shop", "s1.jsonl");
            File.WriteAllLines(transcript, new[]
            {
                // one reply written as two content blocks: the larger counts
                Reply(now.AddHours(-2), "a", "claude-opus-5-5", 10, 100, 1000, 50000, @"D:\work\shop"),
                Reply(now.AddHours(-2), "a", "claude-opus-5-5", 10, 300, 1000, 50000, @"D:\work\shop"),
                Reply(now.AddHours(-1), "b", "claude-sonnet-5", 5, 50, 0, 2000, @"D:\work\shop"),
                Reply(now.AddHours(-1), "c", "<synthetic>", 0, 10, 0, 0, @"D:\work\shop"),
                Reply(now.AddDays(-40), "d", "claude-opus-5-5", 1, 1, 1, 1, @"D:\work\shop"),
                "{\"type\":\"user\",\"message\":{\"content\":\"hi\"},\"timestamp\":\"" + Iso(now) + "\"}",
            });
            string session = Path.Combine(codex, "2026", "09", "28", "rollout-2026-09-28T10-00-00-x.jsonl");
            File.WriteAllLines(session, new[]
            {
                "{\"timestamp\":\"" + Iso(now.AddHours(-3)) + "\",\"type\":\"session_meta\",\"payload\":{\"cwd\":\"/home/me/api\"}}",
                "{\"timestamp\":\"" + Iso(now.AddHours(-3)) + "\",\"type\":\"turn_context\",\"payload\":{\"cwd\":\"/home/me/api\",\"model\":\"gpt-6-astra\"}}",
                "{\"timestamp\":\"" + Iso(now.AddHours(-3)) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null,\"rate_limits\":{}}}",
                Tokens(now.AddHours(-3), 100, 30, 10),
                Tokens(now.AddHours(-2.5), 250, 80, 25),
            });
            var ledger = new TokenLedger(claude, codex);
            ledger.Update(now);
            var all = ledger.Entries();
            var c = all.Where(e => e.Source == "claude").ToList();
            var x = all.Where(e => e.Source == "codex").ToList();
            t.Equal("Claude Code：同一則回覆只算一次、跳過合成訊息與 31 天前的", 2, c.Count);
            var a = c.FirstOrDefault(e => e.Model == "claude-opus-5-5");
            t.Check("Claude Code：同一則回覆取最大的那筆，四種 token 分開記", a != null && a.Output == 300 && a.Input == 10 && a.CacheWrite == 1000 && a.CacheRead == 50000,
                a == null ? "null" : a.Output + "/" + a.Input);
            t.Check("Claude Code：專案＝工作資料夾的名稱", c.All(e => e.Project == "shop"));
            t.Equal("Codex：沒有 token 資訊的那筆不算、累計值相減成每一回合", 2, x.Count);
            if (x.Count == 2)
            {
                t.Check("Codex：第一回合（輸入扣掉快取、快取讀取、輸出）", x[0].Input == 70 && x[0].CacheRead == 30 && x[0].Output == 10, x[0].Input + "/" + x[0].CacheRead + "/" + x[0].Output);
                t.Check("Codex：第二回合只算增加的部分", x[1].Input == 100 && x[1].CacheRead == 50 && x[1].Output == 15, x[1].Input + "/" + x[1].CacheRead + "/" + x[1].Output);
                t.Check("Codex：模型與專案", x.All(e => e.Model == "gpt-6-astra" && e.Project == "api"));
            }
            // appended later: only the new line is read
            File.AppendAllText(transcript, Reply(now.AddMinutes(-5), "e", "claude-opus-5-5", 1, 20, 0, 100, @"D:\work\shop") + "\n");
            ledger.Update(now);
            t.Equal("之後新增的紀錄：只讀新的那幾行", 5, ledger.Entries().Count);

            // summaries
            var today = DateTime.Now.Date;
            var days = TokenLedger.ByDay(ledger.Entries(), 7, today);
            t.Check("每天：7 天、最後一天是今天", days.Count == 7 && days[6].Key == today);
            long sum = days.Sum(d => d.Value.Values.Sum());
            t.Equal("每天的合計＝全部（都在這 7 天內）", ledger.Entries().Sum(e => e.Total), sum);
            var projects = TokenLedger.ByProject(ledger.Entries(), 8);
            t.Check("專案排行：依 token 數由多到少", projects.Count == 2 && projects[0].Key == "claude|shop" && projects[1].Key == "codex|api");
            var models = TokenLedger.ByModel(ledger.Entries());
            t.Equal("模型：最多的在前面", "claude-opus-5-5", models[0].Key);
            t.Check("沒有資料夾時也不當機", new TokenLedger(null, Path.Combine(claude, "nope")).Entries().Count == 0);
        }

        static void Numbers(TestKit t)
        {
            string was = L.Current;
            try
            {
                L.Use("zh-TW");
                t.Equal("token 數（繁中）：萬", "1.2 萬", Fmt.Tokens(12000));
                t.Equal("token 數（繁中）：千萬也用萬", "3,456 萬", Fmt.Tokens(34560000));
                t.Equal("token 數（繁中）：億", "1.2 億", Fmt.Tokens(120000000));
                t.Equal("token 數：小數字照寫", "9,999", Fmt.Tokens(9999));
                L.Use("en");
                t.Equal("token 數（英文）：K／M／B", "12K|3.4M|1.2B", Fmt.Tokens(12000) + "|" + Fmt.Tokens(3400000) + "|" + Fmt.Tokens(1200000000));
                L.Use("ko");
                t.Check("token 數（韓文）：만／억、不加空格", Fmt.Tokens(12000).StartsWith("1.2") && !Fmt.Tokens(12000).Contains(" "), Fmt.Tokens(12000));
            }
            finally { L.Use(was); }
        }
    }
}
