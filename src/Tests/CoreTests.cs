using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;

namespace SentriPet
{
    /// <summary>Formatting, colours, moods and the JSON reader/writer.</summary>
    static class CoreTests
    {
        public static void Run(TestKit t)
        {
            t.Section("格式化（Fmt）");
            t.Run("Fmt", () =>
            {
                var now = DateTime.UtcNow;
                t.Equal("百分比四捨五入", "62%", Fmt.Pct(61.6));
                t.Equal("百分比 0", "0%", Fmt.Pct(0.2));
                t.Equal("倒數：天", "2天3時", Fmt.Countdown(now.AddHours(51).AddMinutes(10)));
                t.Equal("倒數：時", "4時12分", Fmt.Countdown(now.AddHours(4).AddMinutes(12).AddSeconds(30)));
                t.Equal("倒數：分", "5分30秒", Fmt.Countdown(now.AddMinutes(5).AddSeconds(30.6)));
                t.Equal("倒數：秒", "40秒", Fmt.Countdown(now.AddSeconds(40.6)));
                t.Equal("倒數：已過", "即將重置", Fmt.Countdown(now.AddMinutes(-1)));
                t.Equal("倒數：未知", "—", Fmt.Countdown(null));
                t.Equal("時鐘：時分秒", "02:13:45", Fmt.Clock(now.AddHours(2).AddMinutes(13).AddSeconds(45.6)));
                t.Equal("時鐘：天", "3d 04:12", Fmt.Clock(now.AddDays(3).AddHours(4).AddMinutes(12).AddSeconds(30)));
                t.Equal("時鐘：已過", "00:00:00", Fmt.Clock(now.AddSeconds(-5)));

                var today15 = DateTime.Today.AddHours(15);
                t.Equal("日期：今天", "今天 15:00", Fmt.When(today15.ToUniversalTime()));
                t.Equal("日期：明天", "明天 04:00", Fmt.When(DateTime.Today.AddDays(1).AddHours(4).ToUniversalTime()));
                var in3 = DateTime.Today.AddDays(3).AddHours(23);
                t.Equal("日期：這週", "週" + "日一二三四五六"[(int)in3.DayOfWeek] + " 23:00", Fmt.When(in3.ToUniversalTime()));
                var in10 = DateTime.Today.AddDays(10);
                t.Equal("日期：更遠", in10.Month + "/" + in10.Day + " 00:00", Fmt.When(in10.ToUniversalTime()));
                t.Equal("日期：未知", "未知", Fmt.When(null));

                t.Equal("多久前：剛剛", "剛剛", Fmt.Ago(now.AddSeconds(-20)));
                t.Equal("多久前：分鐘", "5 分鐘前", Fmt.Ago(now.AddMinutes(-5.2)));
                t.Equal("多久前：小時", "3 小時前", Fmt.Ago(now.AddHours(-3.1)));
                t.Equal("多久前：天", "2 天前", Fmt.Ago(now.AddDays(-2.1)));

                t.Equal("視窗名稱：每週", "每週", Fmt.WindowLabel(10080));
                t.Equal("視窗名稱：每日", "每日", Fmt.WindowLabel(1440));
                t.Equal("視窗名稱：每月", "每月", Fmt.WindowLabel(43200));
                t.Equal("視窗名稱：5 小時", "5 小時", Fmt.WindowLabel(300));
                t.Equal("視窗名稱：299 分鐘算 5 小時", "5 小時", Fmt.WindowLabel(299));
                t.Equal("視窗名稱：2 天", "2 天", Fmt.WindowLabel(2880));
                t.Equal("視窗名稱：90 分鐘", "90 分鐘", Fmt.WindowLabel(90));
                t.Equal("視窗名稱：未知", "額度", Fmt.WindowLabel(0));
                t.Equal("視窗縮寫：週", "週", Fmt.WindowShort(10080));
                t.Equal("視窗縮寫：5h", "5h", Fmt.WindowShort(300));
                t.Equal("視窗縮寫：月", "月", Fmt.WindowShort(43200));
                t.Equal("視窗縮寫：90m", "90m", Fmt.WindowShort(90));
                t.Equal("首字大寫", "Pro plus", Fmt.Title("pro plus"));
            });

            t.Section("心情與顏色");
            t.Run("Mood", () =>
            {
                t.Equal("60% 以上：開心", Mood.Great, ProviderView.MoodFor(60));
                t.Equal("30～60%：普通", Mood.Good, ProviderView.MoodFor(59.9));
                t.Equal("12～30%：擔心", Mood.Worried, ProviderView.MoodFor(29));
                t.Equal("12% 以下：快沒了", Mood.Critical, ProviderView.MoodFor(11));
                t.Equal("用完：睡覺", Mood.Empty, ProviderView.MoodFor(0.5));
                t.Equal("綠色（50% 以上）", Palette.Hex("#4ADE80"), Palette.Level(50));
                t.Equal("黃色（20～50%）", Palette.Hex("#FBBF24"), Palette.Level(20));
                t.Equal("紅色（20% 以下）", Palette.Hex("#F87171"), Palette.Level(19.9));
                t.Equal("無效顏色 → 灰色", Colors.Gray, Palette.Hex("not-a-colour"));
                t.Equal("同一個 id 顏色固定", Palette.FromId("kiro"), Palette.FromId("kiro"));
            });

            t.Section("JSON");
            t.Run("Json", () =>
            {
                var o = Json.Parse("{\"a\":{\"b\":[{\"c\":1},{\"c\":2.5}]},\"s\":\"x\\n\\u4e2d\\\"q\",\"t\":true,\"n\":null,\"big\":12345678901,\"e\":1.5e3}");
                t.Equal("巢狀路徑 $.a.b[1].c", 2.5, Json.Num(Json.Path(o, "$.a.b[1].c")));
                t.Equal("點號路徑 a.b.0.c", 1.0, Json.Num(Json.Path(o, "a.b.0.c")));
                t.Equal("負索引 a.b[-1].c", 2.5, Json.Num(Json.Path(o, "a.b[-1].c")));
                t.Equal("跳脫字元與 \\u", "x\n中\"q", Json.Str(Json.Get(o, "s")));
                t.Equal("布林", true, Json.Bool(Json.Get(o, "t")));
                t.Check("null", Json.Get(o, "n") == null);
                t.Check("大整數是 long", Json.Get(o, "big") is long);
                t.Equal("科學記號", 1500.0, Json.Num(Json.Get(o, "e")));

                var tolerant = Json.Parse("﻿// comment\n{ key: 'v', /* block */ list: [1, 2, 3,], nan: NaN, inf: -Infinity, }");
                t.Equal("寬鬆：BOM、註解、單引號、沒有引號的鍵", "v", Json.Str(Json.Get(tolerant, "key")));
                t.Equal("寬鬆：結尾逗號", 3, ((IList)Json.Get(tolerant, "list")).Count);
                t.Check("寬鬆：NaN", double.IsNaN((double)Json.Get(tolerant, "nan")));
                t.Check("寬鬆：-Infinity", double.IsNegativeInfinity((double)Json.Get(tolerant, "inf")));
                var lines = Json.Parse("{\"i\":1}\n{\"i\":2}\n") as object[];
                t.Check("JSON Lines → 陣列", lines != null && lines.Length == 2 && Json.Num(Json.Get(lines[1], "i")) == 2);
                t.Equal("a['x y'] 路徑", "ok", Json.Str(Json.Path(Json.Parse("{\"a\":{\"x y\":\"ok\"}}"), "a['x y']")));

                string err;
                t.Check("壞掉的 JSON → null 並附錯誤位置", Json.TryParse("{\"a\": [1, 2", out err) == null && err != null && err.Contains("JSON 第"), err);
                t.Check("空字串 → null", Json.TryParse("") == null);
                t.Equal("數字字串：百分比", 12.5, Json.Num("12.5%"));
                t.Equal("數字字串：錢號與千分位", 1234.0, Json.Num("$1,234"));
                t.Check("布林不是數字", Json.Num(true) == null);
                t.Equal("數字轉字串（不受地區設定影響）", "0.5", Json.Str(0.5));
                t.Equal("字串 TRUE", true, Json.Bool("TRUE"));
                t.Equal("深層找鍵", "deep", Json.Str(Json.FindKey(Json.Parse("{\"x\":[{\"y\":{\"target\":\"deep\"}}]}"), "target")));

                var when = new DateTime(2026, 9, 24, 15, 0, 0, DateTimeKind.Utc);
                t.Equal("日期：ISO（Z）", when, Json.Date("2026-09-24T15:00:00Z"));
                t.Equal("日期：ISO（時區）", when, Json.Date("2026-09-24T23:00:00+08:00"));
                t.Equal("日期：Unix 秒", when, Json.Date(TestKit.Unix(when)));
                t.Equal("日期：Unix 毫秒", when, Json.Date(Json.ToUnixMs(when)));
                t.Equal("日期：數字字串", when, Json.Date(TestKit.Unix(when).ToString()));
                t.Check("日期：亂寫 → null", Json.Date("someday") == null);

                var d = new Dictionary<string, object>
                {
                    { "text", "a\"b\\c\n\t\u0001" }, { "num", 1.25 }, { "int", 7 }, { "nan", double.NaN },
                    { "list", new List<object> { 1, "two", null, true } }, { "nested", new Dictionary<string, object> { { "k", "v" } } },
                };
                foreach (bool pretty in new[] { false, true })
                {
                    var back = Json.Obj(Json.Parse(Json.Serialize(d, pretty)));
                    string label = pretty ? "（縮排）" : "";
                    t.Equal("寫出再讀回：字串" + label, "a\"b\\c\n\t\u0001", Json.Str(Json.Get(back, "text")));
                    t.Equal("寫出再讀回：小數" + label, 1.25, Json.Num(Json.Get(back, "num")));
                    t.Check("寫出再讀回：NaN 變 null" + label, Json.Get(back, "nan") == null && back.ContainsKey("nan"));
                    t.Equal("寫出再讀回：陣列" + label, 4, ((IList)Json.Get(back, "list")).Count);
                    t.Equal("寫出再讀回：巢狀" + label, "v", Json.Str(Json.Path(back, "nested.k")));
                }
            });
        }
    }

    /// <summary>What the themes are given: the big number, reset line, summaries and "use it" levels.</summary>
    static class ModelTests
    {
        static Meter M(string key, string label, string shortLabel, double used, int window, DateTime? reset)
        {
            return new Meter { Key = key, Label = label, ShortLabel = shortLabel, Used = used, WindowMinutes = window, ResetsAt = reset };
        }

        static ProviderView View(string name, params Meter[] meters)
        {
            var snap = new Snapshot { ObservedAt = DateTime.UtcNow, Source = "測試" };
            snap.Meters.AddRange(meters);
            return UsageService.MakeView(new StubProvider(name.ToLowerInvariant(), name), snap);
        }

        public static void Run(TestKit t)
        {
            t.Section("大數字、重置時間、摘要");
            t.Run("View", () =>
            {
                var now = DateTime.UtcNow;
                var fh = M("fh", "5 小時", "5h", 0, 300, null);
                var sd = M("sd", "每週", "週", 39, 10080, now.AddHours(11));
                var v = View("Claude", fh, sd);
                t.Check("大數字是 5 小時（每週用得比較多也一樣）", v.Headline == fh && Math.Abs(v.HeadlineRemaining - 100) < 0.01);
                t.Check("最吃緊的是每週（決定表情）", v.Primary == sd && Math.Abs(v.Remaining - 61) < 0.01 && v.Mood == Mood.Great);
                t.Check("第二個額度是每週", v.Secondary == sd);
                t.Check("5 小時還沒開始計時：重置列改顯示每週", v.ResetMeter == sd);
                t.Equal("重置列標籤", "週 ", v.LabelOf(v.ResetMeter));
                t.Equal("摘要", "Claude 5h 100% 週 61%", v.Summary);
                t.Contains("狀態文字", v.StatusText, "更新 · 測試");

                fh.Used = 30;
                fh.ResetsAt = now.AddHours(2);
                v = View("Claude", fh, sd);
                t.Check("5 小時在計時：重置列顯示 5 小時", v.ResetMeter == fh);
                t.Equal("5 小時的標籤", "5h ", v.LabelOf(v.ResetMeter));

                sd.Used = 92;
                v = View("Claude", fh, sd);
                t.Check("每週快用完：重置列改顯示每週（它才是卡住你的）", v.ResetMeter == sd);
                t.Equal("表情跟著最吃緊的額度", Mood.Critical, v.Mood);
                t.Equal("大數字仍是 5 小時", 70.0, v.HeadlineRemaining);

                var d1 = M("d", "每日", "日", 10, 1440, now.AddHours(3));
                var mo = M("m", "每月", "月", 80, 43200, now.AddDays(9));
                v = View("Other", mo, d1);
                t.Check("沒有 5 小時時：大數字是最短的額度（每日）", v.Headline == d1);

                v = View("Copilot", M("pr", "進階請求", "PR", 25, 43200, now.AddDays(7)));
                t.Equal("只有一個額度：沒有標籤", "", v.LabelOf(v.Headline));
                t.Equal("只有一個額度的摘要", "Copilot 75%", v.Summary);

                v = View("Ollama", new Meter { Key = "local", Label = "本機模型", ShortLabel = "∞", Unlimited = true });
                t.Check("無限制", v.Unlimited && v.HasData && Math.Abs(v.HeadlineRemaining - 100) < 0.01);
                t.Equal("無限制的摘要", "Ollama ∞", v.Summary);

                var empty = UsageService.MakeView(new StubProvider("x", "X"), Snapshot.Fail("壞掉了"));
                t.Check("沒有資料", !empty.HasData && empty.Mood == Mood.Unknown && empty.StatusText == "壞掉了" && empty.Summary == "X ?");
            });

            t.Section("快用掉提醒的等級");
            t.Run("UseIt", () =>
            {
                var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
                Func<int, double, double, int> level = (window, used, hours) =>
                    ProviderView.UseItLevelFor(new Meter { Key = "t", Used = used, WindowMinutes = window, ResetsAt = now.AddHours(hours) }, now);
                t.Equal("週剩 61%、還有 3 天：不催", 0, level(10080, 39, 72));
                t.Equal("週剩 61%、還有 40 小時：等級 1", 1, level(10080, 39, 40));
                t.Equal("週剩 25%、還有 40 小時：不催（剩不多）", 0, level(10080, 75, 40));
                t.Equal("週剩 61%、最後一天：等級 2", 2, level(10080, 39, 11));
                t.Equal("週剩 8%、最後一天：不催", 0, level(10080, 92, 11));
                t.Equal("週剩 20%、最後 3 小時：等級 3", 3, level(10080, 80, 3));
                t.Equal("週剩 4%、最後 3 小時：不催", 0, level(10080, 96, 3));
                t.Equal("月剩 50%、還有 30 小時：等級 1", 1, level(43200, 50, 30));
                t.Equal("5 小時額度：不催", 0, level(300, 10, 1));
                t.Equal("每日額度：不催", 0, level(1440, 10, 3));
                t.Equal("已過重置時間：不催", 0, level(10080, 10, -1));
                t.Equal("不知道重置時間：不催", 0, ProviderView.UseItLevelFor(new Meter { Used = 10, WindowMinutes = 10080 }, now));
                t.Equal("無限制：不催", 0, ProviderView.UseItLevelFor(new Meter { Unlimited = true, WindowMinutes = 10080, ResetsAt = now.AddHours(3) }, now));

                var real = DateTime.UtcNow;
                var fh = M("fh", "5 小時", "5h", 0, 300, null);
                var sd = M("sd", "每週", "週", 39, 10080, real.AddHours(11));
                var v = View("Claude", fh, sd);
                t.Check("畫面資料：最後一天、還剩 61% → 等級 2", v.UseItLevel == 2 && v.UseIt == sd);
                t.Contains("通知文字", Lines.UseItAlert(v), "最後一天！Claude 每週額度還剩 61%");
                fh.Used = 99;
                fh.ResetsAt = real.AddHours(2);
                v = View("Claude", fh, sd);
                t.Check("5 小時已用完：先不催（反正用不了）", v.UseItLevel == 0 && v.UseIt == null);
                var so = M("so", "每週 Opus", "Op", 20, 10080, real.AddHours(3));
                fh.Used = 10;
                v = View("Claude", fh, sd, so);
                t.Check("同時有好幾個：取最急的", v.UseItLevel == 3 && v.UseIt == so);
                t.Equal("額度名稱（英文結尾加空格）", "每週 Opus 額度", Lines.QuotaName(so));
            });
        }
    }

    /// <summary>Settings on disk (in the run's throwaway profile folder).</summary>
    static class SettingsTests
    {
        public static void Run(TestKit t)
        {
            t.Section("設定檔");
            t.Run("Settings", () =>
            {
                if (File.Exists(AppPaths.SettingsFile)) File.Delete(AppPaths.SettingsFile);
                var d = AppSettings.Load();
                t.Check("沒有設定檔：預設值", d.Theme == "pet" && d.Scale == 1.0 && d.AlwaysOnTop && d.UseItReminder && d.ClaudeEstimate && d.WarnAt == 80 && d.CriticalAt == 95);
                t.Check("預設所有 AI 都顯示", d.IsEnabled("claude"));

                var s = new AppSettings
                {
                    Theme = "neon", DailyRandomTheme = true, RandomThemeDate = "2026-09-24", Scale = 1.5, Opacity = 0.75, AlwaysOnTop = false,
                    ClickThrough = true, LowPower = true, HideOnFullscreen = false, ClaudeEstimate = false, Chatty = false, Notifications = false,
                    UseItReminder = false, WarnAt = 70, CriticalAt = 90, AutoStart = false, X = 3786, Y = 1032, Anchor = "tl",
                    CodexLiveMinutes = 0, ClaudeWeeklyReset = "週四 23:00", FirstRunDone = true, TerminalColor = "amber",
                };
                s.Enabled["codex"] = false;
                s.Order.AddRange(new[] { "codex", "claude" });
                s.UseItNotified["claude|sd"] = "2026-09-24T15:00:00.0000000Z#2";
                s.Save();
                t.Check("存檔時不留下暫存檔", !File.Exists(AppPaths.SettingsFile + ".tmp"));
                var r = AppSettings.Load();
                t.Check("讀回：造型與每日隨機", r.Theme == "neon" && r.DailyRandomTheme && r.RandomThemeDate == "2026-09-24");
                t.Check("讀回：大小、透明度、置頂、穿透、省電、全螢幕", r.Scale == 1.5 && r.Opacity == 0.75 && !r.AlwaysOnTop && r.ClickThrough && r.LowPower && !r.HideOnFullscreen);
                t.Check("讀回：提醒相關", !r.ClaudeEstimate && !r.Chatty && !r.Notifications && !r.UseItReminder && r.WarnAt == 70 && r.CriticalAt == 90);
                t.Check("讀回：位置", r.X == 3786 && r.Y == 1032 && r.Anchor == "tl" && !r.AutoStart);
                t.Check("讀回：Codex、Claude 重置時間、終端機顏色", r.CodexLiveMinutes == 0 && r.ClaudeWeeklyReset == "週四 23:00" && r.FirstRunDone && r.TerminalColor == "amber");
                t.Check("讀回：隱藏的 AI 與順序", !r.IsEnabled("codex") && r.IsEnabled("claude") && string.Join(",", r.Order) == "codex,claude");
                t.Equal("讀回：已通知過的提醒", "2026-09-24T15:00:00.0000000Z#2", r.UseItNotified.ContainsKey("claude|sd") ? r.UseItNotified["claude|sd"] : null);

                TestKit.WriteFile(AppPaths.SettingsFile, "{\"scale\": 9, \"opacity\": 0.01, \"warnAt\": 3, \"criticalAt\": 300, \"codexLiveMinutes\": 999, \"unknownField\": [1,2]}");
                var c = AppSettings.Load();
                t.Check("超出範圍的值會被夾住", c.Scale == 2.5 && c.Opacity == 0.2 && c.WarnAt == 10 && c.CriticalAt == 100 && c.CodexLiveMinutes == 120,
                    "scale " + c.Scale + " opacity " + c.Opacity + " warn " + c.WarnAt + " critical " + c.CriticalAt + " codex " + c.CodexLiveMinutes);

                TestKit.WriteFile(AppPaths.SettingsFile, "{ this is not json");
                var broken = AppSettings.Load();
                t.Check("壞掉的設定檔：用預設值、不當機", broken.Theme == "pet" && broken.Scale == 1.0);
                File.Delete(AppPaths.SettingsFile);
            });
        }
    }

    /// <summary>Threshold alerts, reset celebrations and the "use it before it resets" reminders.</summary>
    static class TrackerTests
    {
        static ProviderView View(string id, params Meter[] meters)
        {
            var snap = new Snapshot { ObservedAt = DateTime.UtcNow };
            snap.Meters.AddRange(meters);
            return UsageService.MakeView(new StubProvider(id, id == "claude" ? "Claude" : id), snap);
        }

        public static void Run(TestKit t)
        {
            t.Section("用量提醒與重置慶祝");
            t.Run("Alerts", () =>
            {
                var s = new AppSettings();
                var tracker = new AlertTracker();
                var log = new List<string>();
                Action<ProviderView, Meter, int> alert = (v, m, lv) => log.Add("alert " + m.Key + " " + lv);
                Action<ProviderView, Meter, double> reset = (v, m, prev) => log.Add("reset " + m.Key + " " + prev);
                var m5 = new Meter { Key = "fh", Used = 50, WindowMinutes = 300 };
                var inf = new Meter { Key = "inf", Unlimited = true };
                Action step = () => tracker.Check(s, new List<ProviderView> { View("claude", m5, inf) }, alert, reset);
                step();
                t.Equal("第一次只記錄，不提醒", 0, log.Count);
                m5.Used = 85; step();
                t.Equal("越過 80%：提醒一次", "alert fh 1", log.LastOrDefault());
                m5.Used = 86; step();
                t.Equal("還在同一級：不重複", 1, log.Count);
                m5.Used = 96; step();
                t.Equal("越過 95%：緊急提醒", "alert fh 2", log.LastOrDefault());
                m5.Used = 2; step();
                t.Equal("掉回 0：慶祝重置", "reset fh 96", log.LastOrDefault());
                m5.Used = 20; step();
                m5.Used = 1; step();
                t.Equal("用不到 30% 就重置：不慶祝", 3, log.Count);
                tracker.Check(s, new List<ProviderView> { UsageService.MakeView(new StubProvider("x", "X"), Snapshot.Fail("無")) }, alert, reset);
                t.Equal("沒有資料的 AI 不提醒", 3, log.Count);
            });

            t.Section("快用掉提醒：通知與催促的時機");
            t.Run("UseItTracker", () =>
            {
                var rng = new Random(1);
                var s = new AppSettings();
                var start = DateTime.UtcNow;
                var sd = new Meter { Key = "sd", Label = "每週", ShortLabel = "週", Used = 45, WindowMinutes = 10080, ResetsAt = start.AddHours(11) };
                var v = View("claude", new Meter { Key = "fh", Label = "5 小時", ShortLabel = "5h", Used = 10, WindowMinutes = 300, ResetsAt = start.AddHours(2) }, sd);
                var views = new List<ProviderView> { v };
                var tracker = new UseItTracker();

                var e = tracker.Check(s, views, start, true, rng);
                t.Check("進入最後一天：馬上通知一次", e.Count == 1 && e[0].Announce && e[0].Text.Contains("最後一天"), e.Count > 0 ? e[0].Text : "沒有事件");
                t.Check("記住已通知的等級", s.UseItNotified.ContainsKey("claude|sd") && s.UseItNotified["claude|sd"].EndsWith("#2"));
                t.Equal("一分鐘後：安靜", 0, tracker.Check(s, views, start.AddMinutes(1), true, rng).Count);
                e = tracker.Check(s, views, start.AddMinutes(26), true, rng);
                t.Check("約 25 分鐘後：催一次（不是通知）", e.Count == 1 && !e[0].Announce && e[0].Text.Length > 0, e.Count > 0 ? e[0].Text : "沒有事件");

                var restarted = new UseItTracker();
                t.Equal("重開程式：不重複通知", 0, restarted.Check(s, views, start.AddMinutes(30), true, rng).Count);
                e = restarted.Check(s, views, start.AddMinutes(37), true, rng);
                t.Check("重開後約 6 分鐘：繼續催", e.Count == 1 && !e[0].Announce);
                t.Equal("看不到的時候（藏起來、鎖定、選單開著）：先不說", 0, restarted.Check(s, views, start.AddMinutes(70), false, rng).Count);
                t.Equal("兩分鐘後再試", 1, restarted.Check(s, views, start.AddMinutes(73), true, rng).Count);

                v.Active = true;
                t.Equal("AI 正在工作：不打擾", 0, restarted.Check(s, views, start.AddMinutes(120), true, rng).Count);
                v.Active = false;

                // the same window a few hours later (the level normally comes from the clock, so set it here)
                v.UseItLevel = 3;
                e = restarted.Check(s, views, start.AddHours(8), true, rng);
                t.Check("升到最後 6 小時：再通知一次", e.Count == 1 && e[0].Announce, e.Count > 0 ? e[0].Text : "沒有事件");
                t.Check("記住新的等級", s.UseItNotified["claude|sd"].EndsWith("#3"));
                t.Equal("不同的一週（重置時間差很多）：等級重新算", 0, UseItTracker.AnnouncedLevel(s, "claude|sd", start.AddDays(7).AddHours(11)));
                t.Equal("同一週（推算的重置時間稍微移動）：記得等級", 3, UseItTracker.AnnouncedLevel(s, "claude|sd", start.AddHours(11.8)));

                s.UseItNotified["old|sd"] = start.AddDays(-5).ToString("o") + "#2";
                s.UseItNotified["junk|x"] = "garbage";
                UseItTracker.Prune(s, start);
                t.Check("清掉很久以前和壞掉的紀錄", !s.UseItNotified.ContainsKey("old|sd") && !s.UseItNotified.ContainsKey("junk|x") && s.UseItNotified.ContainsKey("claude|sd"));

                s.UseItReminder = false;
                t.Equal("關掉提醒：什麼都不做", 0, new UseItTracker().Check(s, views, start, true, rng).Count);
            });
        }
    }
}
