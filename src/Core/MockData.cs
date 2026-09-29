using System;
using System.Collections.Generic;

namespace SentriPet
{
    /// <summary>
    /// Sample usage for screenshots, previews and tests: a = relaxed (one AI working), b = running low,
    /// c = weekly/monthly quota about to expire unused at urgency levels 1–3.
    /// </summary>
    static class MockData
    {
        static Meter M(string key, string label, string shortLabel, double used, double hours, bool approx, int window)
        {
            return new Meter { Key = key, Label = label, ShortLabel = shortLabel, Used = used, ResetsAt = DateTime.UtcNow.AddHours(hours), ResetApprox = approx, WindowMinutes = window };
        }

        static ProviderView View(Provider p, Snapshot s)
        {
            return UsageService.MakeView(p, s);
        }

        public static List<ProviderView> A()
        {
            var claude = new Snapshot { Source = L.T("Claude 桌面版快取"), ObservedAt = DateTime.UtcNow.AddMinutes(-4) };
            claude.Meters.Add(M("fh", L.T("5 小時"), "5h", 34, 2.2, true, 300));
            claude.Meters.Add(M("sd", L.T("每週"), L.T("週"), 18, 33.5, true, 10080));
            var codex = new Snapshot { Source = L.T("Codex 官方 app-server"), ObservedAt = DateTime.UtcNow, Plan = "Plus", Active = true };
            codex.Meters.Add(M("codex:300", L.T("5 小時"), "5h", 4, 4.9, false, 300));
            codex.Meters.Add(M("codex:10080", L.T("每週"), L.T("週"), 23, 3.5, false, 10080));
            var copilot = new Snapshot { Source = L.T("Copilot CLI 快取"), ObservedAt = DateTime.UtcNow.AddDays(-11), Stale = true, Plan = "Pro" };
            copilot.Meters.Add(new Meter { Key = "premium_interactions", Label = L.T("進階請求"), ShortLabel = "PR", Used = 0, ResetsAt = DateTime.UtcNow.AddDays(7.7), WindowMinutes = 43200, ValueText = "200 / 200" });
            return new List<ProviderView> { View(new ClaudeProvider(), claude), View(new CodexProvider(), codex), View(new CopilotProvider(), copilot) };
        }

        public static List<ProviderView> B()
        {
            var claude = new Snapshot { Source = L.T("Claude 桌面版快取"), ObservedAt = DateTime.UtcNow.AddMinutes(-2) };
            claude.Meters.Add(M("fh", L.T("5 小時"), "5h", 92, 0.6, true, 300));
            claude.Meters.Add(M("sd", L.T("每週"), L.T("週"), 71, 20, true, 10080));
            var codex = new Snapshot { Source = L.T("Codex 本機紀錄"), ObservedAt = DateTime.UtcNow.AddMinutes(-30), Plan = "Pro" };
            codex.Meters.Add(M("codex:300", L.T("5 小時"), "5h", 100, 1.4, false, 300));
            codex.Meters.Add(M("codex:10080", L.T("每週"), L.T("週"), 64, 60, false, 10080));
            var worried = new Snapshot { Source = L.T("外掛"), ObservedAt = DateTime.UtcNow };
            worried.Meters.Add(M("q", L.T("每日"), L.T("日"), 78, 9, false, 1440));
            var gemini = new CustomProviderStub("gemini", "Gemini", "cat", "#4C8DF6");
            var ollama = new Snapshot { Error = L.T("Ollama 沒有在執行") };
            return new List<ProviderView> { View(new ClaudeProvider(), claude), View(new CodexProvider(), codex), View(gemini, worried), View(new CustomProviderStub("err", "Kiro", "antenna", "#9D7CFF"), ollama) };
        }

        /// <summary>Quota about to expire unused, at urgency levels 1, 2 and 3 (nobody working).</summary>
        public static List<ProviderView> C()
        {
            var claude = new Snapshot { Source = L.T("Claude 桌面版快取"), ObservedAt = DateTime.UtcNow.AddMinutes(-3) };
            claude.Meters.Add(M("fh", L.T("5 小時"), "5h", 20, 3.1, true, 300));
            claude.Meters.Add(M("sd", L.T("每週"), L.T("週"), 45, 30, true, 10080));
            var codex = new Snapshot { Source = L.T("Codex 官方 app-server"), ObservedAt = DateTime.UtcNow, Plan = "Plus" };
            codex.Meters.Add(M("codex:300", L.T("5 小時"), "5h", 10, 4.2, false, 300));
            codex.Meters.Add(M("codex:10080", L.T("每週"), L.T("週"), 30, 14, false, 10080));
            var copilot = new Snapshot { Source = L.T("Copilot CLI 快取"), ObservedAt = DateTime.UtcNow, Plan = "Pro" };
            copilot.Meters.Add(new Meter { Key = "premium_interactions", Label = L.T("進階請求"), ShortLabel = "PR", Used = 40, ResetsAt = DateTime.UtcNow.AddHours(3), WindowMinutes = 43200, ValueText = "120 / 200" });
            return new List<ProviderView> { View(new ClaudeProvider(), claude), View(new CodexProvider(), codex), View(new CopilotProvider(), copilot) };
        }

        /// <summary>A report of past weekly windows (settings page snapshot, self-test), not saved anywhere.</summary>
        public static UsageHistory History()
        {
            var h = new UsageHistory(null);
            var end = DateTime.UtcNow.Date.AddDays(-2).AddHours(15);
            double[] claude = { 62, 78, 91, 55, 84, 97, 73, 88 };
            double[] codex = { 40, 35, 66, 71, 52, 80 };
            for (int i = 0; i < claude.Length; i++)
                h.AddResult(new WindowResult { Provider = "claude", Name = "Claude", Meter = "sd", Label = L.T("每週"), EndedAt = end.AddDays(-7 * (claude.Length - 1 - i)), Used = claude[i], SeenToEnd = true, NudgeLevel = claude[i] < 70 ? 2 : 0 });
            for (int i = 0; i < codex.Length; i++)
                h.AddResult(new WindowResult { Provider = "codex", Name = "Codex", Meter = "codex:10080", Label = L.T("每週"), EndedAt = end.AddDays(1 - 7 * (codex.Length - 1 - i)), Used = codex[i], SeenToEnd = i != 2 });
            return h;
        }

        /// <summary>A month of token counts (the report window's snapshot and self-test), always the same.</summary>
        public static TokenLedger Tokens()
        {
            var ledger = new TokenLedger(null, null);
            var rng = new Random(12);
            var today = DateTime.Now.Date;
            string[] claudeProjects = { "SentriPet", "web-shop", "SentriPet", "notes", "SentriPet" };
            string[] codexProjects = { "api-server", "SentriPet", "api-server" };
            for (int d = 29; d >= 0; d--)
            {
                var day = today.AddDays(-d);
                bool weekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
                int replies = weekend ? rng.Next(0, 8) : rng.Next(15, 45);
                for (int i = 0; i < replies; i++)
                {
                    bool opus = rng.NextDouble() < 0.7;
                    ledger.Add(new TokenEntry
                    {
                        At = day.AddHours(9 + rng.NextDouble() * 12).ToUniversalTime(), Source = "claude",
                        Project = claudeProjects[rng.Next(claudeProjects.Length)], Model = opus ? "claude-opus-5-5" : "claude-sonnet-5",
                        Input = rng.Next(20, 400), Output = rng.Next(300, 4000), CacheWrite = rng.Next(1000, 20000), CacheRead = rng.Next(40000, 400000),
                    });
                }
                int turns = weekend ? rng.Next(0, 3) : rng.Next(3, 14);
                for (int i = 0; i < turns; i++)
                    ledger.Add(new TokenEntry
                    {
                        At = day.AddHours(10 + rng.NextDouble() * 10).ToUniversalTime(), Source = "codex",
                        Project = codexProjects[rng.Next(codexProjects.Length)], Model = rng.NextDouble() < 0.8 ? "gpt-6-astra" : "codex-auto-review",
                        Input = rng.Next(2000, 30000), Output = rng.Next(200, 3000), CacheRead = rng.Next(20000, 200000),
                    });
            }
            return ledger;
        }

        class CustomProviderStub : Provider
        {
            public CustomProviderStub(string id, string name, string mascot, string color)
            {
                Id = id; Name = name; Mascot = mascot; Color = Rgba.Hex(color);
            }
            public override Detection Detect() { return new Detection { Installed = true }; }
            public override Snapshot Fetch(bool force, AppSettings settings) { return null; }
        }
    }
}
