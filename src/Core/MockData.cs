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
            var claude = new Snapshot { Source = "Claude 桌面版快取", ObservedAt = DateTime.UtcNow.AddMinutes(-4) };
            claude.Meters.Add(M("fh", "5 小時", "5h", 34, 2.2, true, 300));
            claude.Meters.Add(M("sd", "每週", "週", 18, 33.5, true, 10080));
            var codex = new Snapshot { Source = "Codex 官方 app-server", ObservedAt = DateTime.UtcNow, Plan = "Plus", Active = true };
            codex.Meters.Add(M("codex:300", "5 小時", "5h", 4, 4.9, false, 300));
            codex.Meters.Add(M("codex:10080", "每週", "週", 23, 3.5, false, 10080));
            var copilot = new Snapshot { Source = "Copilot CLI 快取", ObservedAt = DateTime.UtcNow.AddDays(-11), Stale = true, Plan = "Pro" };
            copilot.Meters.Add(new Meter { Key = "premium_interactions", Label = "進階請求", ShortLabel = "PR", Used = 0, ResetsAt = DateTime.UtcNow.AddDays(7.7), WindowMinutes = 43200, ValueText = "200 / 200" });
            return new List<ProviderView> { View(new ClaudeProvider(), claude), View(new CodexProvider(), codex), View(new CopilotProvider(), copilot) };
        }

        public static List<ProviderView> B()
        {
            var claude = new Snapshot { Source = "Claude 桌面版快取", ObservedAt = DateTime.UtcNow.AddMinutes(-2) };
            claude.Meters.Add(M("fh", "5 小時", "5h", 92, 0.6, true, 300));
            claude.Meters.Add(M("sd", "每週", "週", 71, 20, true, 10080));
            var codex = new Snapshot { Source = "Codex 本機紀錄", ObservedAt = DateTime.UtcNow.AddMinutes(-30), Plan = "Pro" };
            codex.Meters.Add(M("codex:300", "5 小時", "5h", 100, 1.4, false, 300));
            codex.Meters.Add(M("codex:10080", "每週", "週", 64, 60, false, 10080));
            var worried = new Snapshot { Source = "外掛", ObservedAt = DateTime.UtcNow };
            worried.Meters.Add(M("q", "每日", "日", 78, 9, false, 1440));
            var gemini = new CustomProviderStub("gemini", "Gemini", "cat", "#4C8DF6");
            var ollama = new Snapshot { Error = "Ollama 沒有在執行" };
            return new List<ProviderView> { View(new ClaudeProvider(), claude), View(new CodexProvider(), codex), View(gemini, worried), View(new CustomProviderStub("err", "Kiro", "antenna", "#9D7CFF"), ollama) };
        }

        /// <summary>Quota about to expire unused, at urgency levels 1, 2 and 3 (nobody working).</summary>
        public static List<ProviderView> C()
        {
            var claude = new Snapshot { Source = "Claude 桌面版快取", ObservedAt = DateTime.UtcNow.AddMinutes(-3) };
            claude.Meters.Add(M("fh", "5 小時", "5h", 20, 3.1, true, 300));
            claude.Meters.Add(M("sd", "每週", "週", 45, 30, true, 10080));
            var codex = new Snapshot { Source = "Codex 官方 app-server", ObservedAt = DateTime.UtcNow, Plan = "Plus" };
            codex.Meters.Add(M("codex:300", "5 小時", "5h", 10, 4.2, false, 300));
            codex.Meters.Add(M("codex:10080", "每週", "週", 30, 14, false, 10080));
            var copilot = new Snapshot { Source = "Copilot CLI 快取", ObservedAt = DateTime.UtcNow, Plan = "Pro" };
            copilot.Meters.Add(new Meter { Key = "premium_interactions", Label = "進階請求", ShortLabel = "PR", Used = 40, ResetsAt = DateTime.UtcNow.AddHours(3), WindowMinutes = 43200, ValueText = "120 / 200" });
            return new List<ProviderView> { View(new ClaudeProvider(), claude), View(new CodexProvider(), codex), View(new CopilotProvider(), copilot) };
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
