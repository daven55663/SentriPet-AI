using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentriPet
{
    /// <summary>Growing pets (#23): experience, levels, achievements, the daily streak, progress.json.</summary>
    static class GrowthTests
    {
        public static void Run(TestKit t)
        {
            t.Section("成長與成就（#23）");
            t.Run("Levels and experience", () => Levels(t));
            t.Run("Achievements", () => Achievements(t));
            t.Run("Days", () => Days(t));
            t.Run("File", () => FileRoundTrip(t));
            t.Run("Ran out early", () => EmptyHours(t));
        }

        static readonly DateTime Start = new DateTime(2026, 6, 4, 15, 0, 0, DateTimeKind.Utc);

        static WindowResult W(string provider, int week, double used, double emptyHours = 0, int nudge = 0, bool seen = true)
        {
            return new WindowResult { Provider = provider, Name = provider, Meter = "sd", Label = "每週", EndedAt = Start.AddDays(7 * week), Used = used, EmptyHours = emptyHours, NudgeLevel = nudge, SeenToEnd = seen };
        }

        static List<ProgressEvent> Add(Progress p, List<WindowResult> history, WindowResult r)
        {
            history.Add(r);
            return p.Window(r, history);
        }

        static bool Has(List<ProgressEvent> events, string id) { return events.Any(e => e.Achievement != null && e.Achievement.Id == id); }

        static void Levels(TestKit t)
        {
            t.Equal("Lv 1 從 0 開始、Lv 2 要 40、Lv 3 要 120、Lv 10 要 1800",
                    "0,40,120,240,400,1800", string.Join(",", new[] { 1, 2, 3, 4, 5, 10 }.Select(Progress.XpFor)));
            t.Check("經驗值 → 等級", Progress.LevelFor(0) == 1 && Progress.LevelFor(39) == 1 && Progress.LevelFor(40) == 2 && Progress.LevelFor(1799) == 9 && Progress.LevelFor(1800) == 10);
            t.Equal("用到 90% 以上、沒有提早用完：最多（30）", 30, Progress.WindowXp(W("claude", 0, 96, emptyHours: 2)));
            t.Equal("用完了但太早（卡了 30 小時）：比用到 70～90% 還少（15）", 15, Progress.WindowXp(W("claude", 0, 100, emptyHours: 30)));
            t.Equal("70～90%：20", 20, Progress.WindowXp(W("claude", 0, 75)));
            t.Equal("40～70%：10", 10, Progress.WindowXp(W("claude", 0, 50)));
            t.Equal("不到 40%：3", 3, Progress.WindowXp(W("claude", 0, 20)));
            t.Equal("桌寵沒開到最後、數字又低（可能只是沒看到）：2", 2, Progress.WindowXp(W("claude", 0, 20, seen: false)));
            t.Check("不是用越多越好：提早用完的比有計畫用到 80% 的少", Progress.WindowXp(W("c", 0, 100, emptyHours: 48)) < Progress.WindowXp(W("c", 0, 80)));

            var p = new Progress(null);
            var h = new List<WindowResult>();
            var e = Add(p, h, W("claude", 0, 95, emptyHours: 1));
            t.Equal("第一期：30 + 成就（第一份週報 10、物盡其用 20、精準規劃 30）", 90, p.Xp);
            t.Check("升級事件：Lv 2", e.Any(x => x.Level == 2) && p.Level == 2);
            t.Equal("同一期不會算兩次", 0, p.Window(h[0], h).Count);
            t.Equal("經驗值沒變", 90, p.Xp);
            t.Check("下一個配件：Lv 4 星星徽章", p.NextAccessory.HasValue && p.NextAccessory.Value.Key == 4);
        }

        static void Achievements(TestKit t)
        {
            var p = new Progress(null);
            var h = new List<WindowResult>();
            var e = Add(p, h, W("claude", 0, 100, emptyHours: 20));
            t.Check("用完但太早：物盡其用有，精準規劃沒有", Has(e, "well-used") && !Has(e, "planner") && Has(e, "first-window"));
            e = Add(p, h, W("claude", 1, 30));
            t.Check("用得少：沒有新成就", e.All(x => x.Achievement == null));
            e = Add(p, h, W("claude", 2, 85, nudge: 2));
            t.Check("被催了之後用到 80%：聽勸；上一期不到一半：重新振作", Has(e, "listened") && Has(e, "recovered"));
            Add(p, h, W("claude", 3, 90));
            Add(p, h, W("claude", 4, 82));
            t.Check("連續 3 期 ≥ 80%：還沒有「不浪費」", !p.Unlocked.ContainsKey("no-waste-4"));
            t.Equal("設定頁顯示進度 3 / 4", "3 / 4", p.ProgressOf(Progress.All.First(a => a.Id == "no-waste-4"), h));
            e = Add(p, h, W("claude", 5, 88));
            t.Check("連續 4 期 ≥ 80%：不浪費", Has(e, "no-waste-4"));
            t.Equal("解鎖後不再顯示進度", null, p.ProgressOf(Progress.All.First(a => a.Id == "no-waste-4"), h));
            for (int w = 6; w <= 9; w++) e = Add(p, h, W("claude", w, 75));
            t.Check("連續 8 期 ≥ 70%（第 1 期太早用完、第 2 期太少，不算）：細水長流", Has(e, "steady-8"), string.Join(",", h.Select(x => x.Used)));
            e = Add(p, h, new WindowResult { Provider = "codex", Name = "Codex", Meter = "codex:10080", Label = "每週", EndedAt = Start.AddDays(7 * 9 + 2), Used = 72, SeenToEnd = true });
            t.Check("同一週兩個 AI 都 ≥ 70%：雙管齊下", Has(e, "two-pets"));
            t.Check("成就只解鎖一次", Add(p, h, W("claude", 10, 99)).All(x => x.Achievement == null || x.Achievement.Id != "well-used"));
            t.Check("每個成就都有名稱和說明", Progress.All.All(a => !string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(a.Description) && a.Xp > 0));
        }

        static void Days(TestKit t)
        {
            var p = new Progress(null);
            var d0 = new DateTime(2026, 9, 1);
            var all = new List<ProgressEvent>();
            for (int i = 0; i < 7; i++) all.AddRange(p.Day(d0.AddDays(i)));
            t.Check("連續 7 天：天天見", p.Streak == 7 && all.Any(x => x.Achievement != null && x.Achievement.Id == "streak-7"));
            t.Equal("每天 +3，加上成就 20", 7 * Progress.DailyXp + 20, p.Xp);
            t.Equal("同一天第二次不算", 0, p.Day(d0.AddDays(6)).Count + (p.Xp - (7 * Progress.DailyXp + 20)));
            p.Day(d0.AddDays(9));
            t.Check("中斷一天：從 1 開始，最長紀錄留著", p.Streak == 1 && p.BestStreak == 7 && p.Days == 8);
        }

        static void FileRoundTrip(TestKit t)
        {
            string file = Path.Combine(t.TempDir("progress"), "progress.json");
            var p = new Progress(file);
            t.Check("沒有檔案：Existed 是 false", !p.Existed);
            var h = new List<WindowResult>();
            Add(p, h, W("claude", 0, 92));
            p.Day(new DateTime(2026, 9, 1));
            p.Day(new DateTime(2026, 9, 2));
            t.Check("有變動要存", p.Dirty);
            p.Save();
            t.Check("存檔後不是 Dirty、檔案存在", !p.Dirty && File.Exists(file));
            var q = new Progress(file);
            t.Check("讀回：經驗值、成就、連續天數都一樣", q.Existed && q.Xp == p.Xp && q.Unlocked.Keys.OrderBy(x => x).SequenceEqual(p.Unlocked.Keys.OrderBy(x => x)) && q.Streak == 2,
                    q.Xp + " vs " + p.Xp);
            t.Equal("讀回後同一期不會再算", 0, q.Window(h[0], h).Count);
            t.Equal("讀回後同一天不會再算", 0, q.Day(new DateTime(2026, 9, 2)).Count);
            t.Check("讀回後沒有變動", !q.Dirty);

            var b = new Progress(null);
            var sample = MockData.History().Results;
            int lv = b.Backfill(sample);
            t.Check("第一次開啟：用過去的週報補算（範例資料到 Lv 3 以上）", lv >= 3 && b.Existed, "Lv " + lv + ", " + b.Xp + " XP");
            t.Equal("補算後再看到同樣的週報不會重複給", 0, sample.Sum(r => b.Window(r, sample).Count));
        }

        static void EmptyHours(TestKit t)
        {
            var h = new UsageHistory(null);
            var s = new AppSettings();
            var t0 = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
            var reset = t0.AddHours(48);
            Func<double, DateTime, List<ProviderView>> view = (used, resetsAt) => new List<ProviderView>
            {
                new ProviderView { Id = "claude", Name = "Claude", HasData = true, Meters = new List<Meter> { new Meter { Key = "sd", Label = "每週", Used = used, ResetsAt = resetsAt, WindowMinutes = 10080 } } },
            };
            h.Observe(view(50, reset), s, t0);
            h.Observe(view(100, reset), s, t0.AddHours(18));
            h.Observe(view(100, reset), s, t0.AddHours(30));
            var ended = h.Observe(view(2, reset.AddDays(7)), s, t0.AddHours(49));
            t.Check("週額度結束：記下提早幾小時用完（第 18 小時用完、第 48 小時重置 → 30 小時）",
                    ended.Count == 1 && Math.Abs(ended[0].EmptyHours - 30) < 0.1, ended.Count == 1 ? ended[0].EmptyHours.ToString() : ended.Count + " windows");
            ended = h.Observe(view(90, reset.AddDays(7)), s, t0.AddHours(49 + 100));
            ended = h.Observe(view(1, reset.AddDays(14)), s, t0.AddHours(49 + 170));
            t.Check("下一期沒有用完：0 小時", ended.Count == 1 && ended[0].EmptyHours == 0, ended.Count == 1 ? ended[0].EmptyHours.ToString() : ended.Count + " windows");
        }
    }
}
