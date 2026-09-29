using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentriPet
{
    /// <summary>The pace (#15) and the report of weekly/monthly windows (#16).</summary>
    static class UsageHistoryTests
    {
        static readonly DateTime Base = new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc);

        static List<ProviderView> Views(string key, int window, double used, DateTime resets)
        {
            var snap = new Snapshot { ObservedAt = Base };
            snap.Meters.Add(new Meter { Key = key, Label = window >= 10080 ? L.T("每週") : L.T("5 小時"), Used = used, ResetsAt = resets, WindowMinutes = window });
            return new List<ProviderView> { UsageService.MakeView(new StubProvider("claude", "Claude"), snap) };
        }

        public static void Run(TestKit t)
        {
            t.Section("用完時間預測（#15）與額度利用率週報（#16）");
            t.Run("Forecast", () =>
            {
                var s = new AppSettings();
                var reset = Base.AddHours(3);
                // 1 point an minute for half an hour: 20% → 50%
                var h = new UsageHistory(null);
                List<ProviderView> v = null;
                for (int i = 0; i <= 30; i++) { v = Views("fh", 300, 20 + i, reset); h.Observe(v, s, Base.AddMinutes(i)); }
                var now = Base.AddMinutes(30);
                var at = h.RunsOutAt("claude", v[0].Meters[0], now);
                t.Check("穩定地用：1%／分鐘、剩 50% → 約 50 分鐘後用完", at.HasValue && Math.Abs((at.Value - now.AddMinutes(50)).TotalMinutes) < 2, at.ToString());
                h.Annotate(v, now);
                t.Check("預測寫到額度上（懸停卡片與提醒用）", v[0].Meters[0].RunsOutAt.HasValue);

                var slow = new UsageHistory(null);
                for (int i = 0; i <= 30; i++) { v = Views("fh", 300, 20 + i * 0.1, reset); slow.Observe(v, s, Base.AddMinutes(i)); }
                t.Check("用得慢：重置前用不完就不預測", slow.RunsOutAt("claude", v[0].Meters[0], now) == null);

                var idle = new UsageHistory(null);
                for (int i = 0; i <= 30; i++) { v = Views("fh", 300, 20 + i, reset); idle.Observe(v, s, Base.AddMinutes(i)); }
                for (int i = 31; i <= 55; i++) { v = Views("fh", 300, 50, reset); idle.Observe(v, s, Base.AddMinutes(i)); }
                t.Check("停下來 20 分鐘以上：不預測（閒置時不亂報）", idle.RunsOutAt("claude", v[0].Meters[0], Base.AddMinutes(55)) == null);

                var fresh = new UsageHistory(null);
                for (int i = 0; i <= 5; i++) { v = Views("fh", 300, 20 + i * 2, reset); fresh.Observe(v, s, Base.AddMinutes(i)); }
                t.Check("資料太少（5 分鐘）：不預測", fresh.RunsOutAt("claude", v[0].Meters[0], Base.AddMinutes(5)) == null);

                var rolled = new UsageHistory(null);
                for (int i = 0; i <= 30; i++) { v = Views("fh", 300, 20 + i, reset); rolled.Observe(v, s, Base.AddMinutes(i)); }
                v = Views("fh", 300, 1, reset.AddHours(5));
                rolled.Observe(v, s, Base.AddMinutes(31));
                t.Check("重置之後重新累積，不拿上一期的速度", rolled.RunsOutAt("claude", v[0].Meters[0], Base.AddMinutes(31)) == null);

                var weekly = new UsageHistory(null);
                var wreset = Base.AddDays(2);
                for (int i = 0; i <= 36; i++) { v = Views("sd", 10080, 40 + i * 0.5, wreset); weekly.Observe(v, s, Base.AddMinutes(i * 10)); }
                var wat = weekly.RunsOutAt("claude", v[0].Meters[0], Base.AddMinutes(360));
                t.Check("每週額度：看最近 6 小時的速度（3%／小時、剩 42% → 14 小時後）", wat.HasValue && Math.Abs((wat.Value - Base.AddHours(20)).TotalMinutes) < 20, wat.ToString());
            });

            t.Run("Report", () =>
            {
                var s = new AppSettings();
                var r1 = Base.AddDays(2);
                var h = new UsageHistory(null);
                t.Check("第一次看到：沒有結束的週期", h.Observe(Views("sd", 10080, 60, r1), s, Base).Count == 0);
                h.Observe(Views("sd", 10080, 75, r1.AddMinutes(40)), s, Base.AddDays(1));   // estimated reset time moved a little
                h.Observe(Views("sd", 10080, 80, r1), s, r1.AddMinutes(-30));
                s.UseItNotified["claude|sd"] = r1.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + "#2";
                var ended = h.Observe(Views("sd", 10080, 2, r1.AddDays(7)), s, r1.AddMinutes(5));
                t.Equal("重置：記下一期", 1, ended.Count);
                if (ended.Count == 1)
                {
                    var r = ended[0];
                    t.Check("記下最後看到的用量、結束時間、有催過", r.Used == 80 && r.EndedAt == r1 && r.NudgeLevel == 2 && r.SeenToEnd && r.Wasted == 20,
                        r.Used + " " + r.EndedAt.ToString("o") + " nudge " + r.NudgeLevel + " seen " + r.SeenToEnd);
                }
                t.Check("同一期不會記兩次", h.Observe(Views("sd", 10080, 3, r1.AddDays(7)), s, r1.AddMinutes(6)).Count == 0 && h.Results.Count == 1);

                var drop = new UsageHistory(null);
                drop.Observe(Views("sd", 10080, 70, r1), s, r1.AddHours(-1));
                t.Check("重置時間還沒更新、但數字掉下來：也算重置", drop.Observe(Views("sd", 10080, 1, r1), s, r1.AddMinutes(10)).Count == 1);

                var five = new UsageHistory(null);
                five.Observe(Views("fh", 300, 70, Base.AddHours(1)), s, Base);
                t.Check("5 小時額度不列入週報", five.Observe(Views("fh", 300, 1, Base.AddHours(6)), s, Base.AddHours(1.1)).Count == 0);

                // the file: a window that ended while the computer was off
                string file = Path.Combine(Path.GetTempPath(), "sentripet-report-test.json");
                if (File.Exists(file)) File.Delete(file);
                var a = new UsageHistory(file);
                a.Observe(Views("sd", 10080, 70, r1), s, Base);
                t.Check("存成檔案", File.Exists(file));
                var b = new UsageHistory(file);
                var off = b.Observe(Views("sd", 10080, 5, r1.AddDays(7)), s, r1.AddDays(1));
                t.Check("關機時跨過重置：開機後用關機前最後的數字補記，並標明沒看到最後", off.Count == 1 && off[0].Used == 70 && !off[0].SeenToEnd);
                var c = new UsageHistory(file);
                t.Check("讀回週報紀錄", c.Results.Count == 1 && c.Results[0].Used == 70 && c.Results[0].Label == L.T("每週") && c.Results[0].Name == "Claude");
                for (int i = 0; i < 30; i++) c.AddResult(new WindowResult { Provider = "claude", Meter = "sd", Name = "Claude", Label = "x", EndedAt = r1.AddDays(-7 * (i + 1)), Used = 50 });
                c.Save(r1.AddDays(1));
                t.Equal("每個額度最多留 26 期（約半年）", 26, new UsageHistory(file).Results.Count);
                File.WriteAllText(file, "{ broken");
                t.Check("壞掉的檔案：從頭開始、不當機", new UsageHistory(file).Results.Count == 0);
                File.Delete(file);

                t.Contains("總結：用得好", Lines.WindowSummary(new WindowResult { Name = "Claude", Label = L.T("每週"), Used = 95 }), "95%");
                t.Contains("總結：浪費很多", Lines.WindowSummary(new WindowResult { Name = "Codex", Label = L.T("每週"), Used = 20 }), "80%");
            });
        }
    }

    /// <summary>Quiet time (#17).</summary>
    static class QuietTests
    {
        // a known week: 2026-09-28 is a Monday
        static DateTime Local(int day, int h, int m) { return new DateTime(2026, 9, 28, h, m, 0, DateTimeKind.Local).AddDays(day - 1); }   // day 1 = Monday … 7 = Sunday

        public static void Run(TestKit t)
        {
            t.Section("勿擾時段（#17）");
            t.Run("Quiet", () =>
            {
                TimeSpan ts;
                t.Check("時間：22:00、8:30、0800、7、24:00、全形冒號都看得懂",
                    Quiet.TryParseTime("22:00", out ts) && ts == new TimeSpan(22, 0, 0) &&
                    Quiet.TryParseTime("8:30", out ts) && ts == new TimeSpan(8, 30, 0) &&
                    Quiet.TryParseTime("0800", out ts) && ts == new TimeSpan(8, 0, 0) &&
                    Quiet.TryParseTime("7", out ts) && ts == new TimeSpan(7, 0, 0) &&
                    Quiet.TryParseTime("24:00", out ts) && ts == new TimeSpan(24, 0, 0) &&
                    Quiet.TryParseTime("12：30", out ts) && ts == new TimeSpan(12, 30, 0));
                t.Check("時間：25:00、12:60、12:5、空白、文字都不接受",
                    !Quiet.TryParseTime("25:00", out ts) && !Quiet.TryParseTime("12:60", out ts) && !Quiet.TryParseTime("12:5", out ts) &&
                    !Quiet.TryParseTime("", out ts) && !Quiet.TryParseTime("晚上", out ts));

                var s = new AppSettings();
                t.Check("預設關閉", !Quiet.InHours(s, Local(1, 23, 0)) && !Quiet.IsQuiet(s, DateTime.UtcNow));

                s.QuietHours = true;
                s.QuietFrom = "09:00";
                s.QuietTo = "17:00";
                s.QuietDays = "12345";
                t.Check("白天的時段：週一 10:00 勿擾、17:00 結束、週六不算",
                    Quiet.InHours(s, Local(1, 10, 0)) && !Quiet.InHours(s, Local(1, 17, 0)) && !Quiet.InHours(s, Local(1, 8, 59)) && !Quiet.InHours(s, Local(6, 10, 0)));

                s.QuietFrom = "22:00";
                s.QuietTo = "08:00";
                t.Check("跨午夜：週五 23:00 與週六 07:00（屬於週五晚上）勿擾",
                    Quiet.InHours(s, Local(5, 23, 0)) && Quiet.InHours(s, Local(6, 7, 0)));
                t.Check("跨午夜：週六晚上不算、週一 07:00（屬於週日晚上）不算、週一 23:00 算",
                    !Quiet.InHours(s, Local(6, 23, 0)) && !Quiet.InHours(s, Local(1, 7, 0)) && Quiet.InHours(s, Local(1, 23, 0)));
                t.Check("跨午夜：08:00 結束、21:59 還沒開始", !Quiet.InHours(s, Local(2, 8, 0)) && !Quiet.InHours(s, Local(2, 21, 59)));

                s.QuietFrom = s.QuietTo = "00:00";
                s.QuietDays = "06";
                t.Check("開始等於結束：整天（只有週六、週日）", Quiet.InHours(s, Local(7, 13, 0)) && Quiet.InHours(s, Local(6, 0, 0)) && !Quiet.InHours(s, Local(1, 13, 0)));

                s.QuietFrom = "abc";
                t.Check("時間看不懂：不算勿擾", !Quiet.InHours(s, Local(7, 13, 0)));

                var p = new AppSettings { PausedUntil = DateTime.UtcNow.AddMinutes(30) };
                t.Check("暫停提醒：期間內安靜、過了就恢復", Quiet.IsQuiet(p, DateTime.UtcNow) && !Quiet.IsQuiet(p, DateTime.UtcNow.AddMinutes(31)));
                var end = Quiet.EndsAt(p, DateTime.UtcNow);
                t.Check("暫停提醒：知道到幾點", end.HasValue && Math.Abs((end.Value - p.PausedUntil.Value.ToLocalTime()).TotalMinutes) < 1.01, end.ToString());

                var h = new AppSettings { QuietHours = true, QuietFrom = "22:00", QuietTo = "08:00" };
                var at23 = Local(2, 23, 0).ToUniversalTime();
                var hEnd = Quiet.EndsAt(h, at23);
                t.Equal("勿擾時段：23:00 時知道到隔天 08:00", Local(3, 8, 0).ToString("MM-dd HH:mm"), hEnd.HasValue ? hEnd.Value.ToString("MM-dd HH:mm") : null);
                h.PausedUntil = Local(2, 23, 30).ToUniversalTime();
                t.Check("暫停到 23:30 又在勿擾時段：到 08:00", Quiet.EndsAt(h, at23).Value.ToString("HH:mm") == "08:00");
                t.Check("不安靜時沒有結束時間（隔天中午，暫停已過）", Quiet.EndsAt(h, Local(3, 12, 0).ToUniversalTime()) == null);

                // settings file
                if (File.Exists(AppPaths.SettingsFile)) File.Delete(AppPaths.SettingsFile);
                var d = AppSettings.Load();
                t.Check("設定檔：預設 22:00–08:00、每天、沒有暫停", !d.QuietHours && d.QuietFrom == "22:00" && d.QuietTo == "08:00" && d.QuietDays == "0123456" && d.PausedUntil == null);
                var until = new DateTime(2099, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                new AppSettings { QuietHours = true, QuietFrom = "23:30", QuietTo = "07:15", QuietDays = "135", PausedUntil = until }.Save();
                var r = AppSettings.Load();
                t.Check("設定檔：讀回勿擾時段與暫停時間", r.QuietHours && r.QuietFrom == "23:30" && r.QuietTo == "07:15" && r.QuietDays == "135" &&
                                                     r.PausedUntil == until && r.PausedUntil.Value.Kind == DateTimeKind.Utc, r.PausedUntil.ToString());
                new AppSettings { PausedUntil = DateTime.UtcNow.AddHours(-1) }.Save();
                t.Check("設定檔：過期的暫停不存", AppSettings.Load().PausedUntil == null);
                File.Delete(AppPaths.SettingsFile);
            });
        }
    }
}
