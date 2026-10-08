using System;
using System.Collections.Generic;
using System.Linq;

namespace SentriPet
{
    /// <summary>
    /// Things the pets say. Every line is a translatable template (see <see cref="L"/>) with named placeholders:
    /// {name} {pct} {meter} {reset} for the provider, plus the extra ones a line passes in.
    /// Each kind of line is an event the user can give lines of their own in lines.json (#25), and so can a theme (#28);
    /// see <see cref="CustomLines"/>.
    /// </summary>
    static class Lines
    {
        static readonly Random shared = new Random();

        /// <summary>
        /// One line for an event: the user's and the theme's own lines for it when there are any (with "mix", together with
        /// the built-in ones), otherwise one of the built-in lines (translation keys).
        /// </summary>
        static string Choose(string ev, Random rng, ProviderView v, Meter m, string[] extra, params string[] builtIn)
        {
            rng = rng ?? shared;
            var custom = CustomLines.For(ev);
            if (custom != null)
            {
                int total = custom.Count + (CustomLines.MixFor(ev) ? builtIn.Length : 0);
                int i = rng.Next(total);
                if (i < custom.Count) return Fill(custom[i], v, m, extra);
                return Say(builtIn[i - custom.Count], v, m, extra);
            }
            return Say(builtIn[rng.Next(builtIn.Length)], v, m, extra);
        }

        /// <summary>True when the user (or the theme) has lines of their own for an event and doesn't want the built-in ones.</summary>
        static bool OnlyCustom(string ev) { return CustomLines.For(ev) != null && !CustomLines.MixFor(ev); }

        /// <summary>Translates a line and fills in its placeholders.</summary>
        static string Say(string key, ProviderView v, Meter m, params string[] extra)
        {
            return Fill(L.T(key), v, m, extra);
        }

        static string Fill(string s, ProviderView v, Meter m, string[] extra)
        {
            if (extra != null)
                for (int i = 0; i + 1 < extra.Length; i += 2) s = s.Replace("{" + extra[i] + "}", extra[i + 1]);
            if (v == null) return L.Finish(s);
            m = m ?? v.Headline ?? v.Primary;
            return L.Finish(s.Replace("{name}", v.Name)
                    .Replace("{pct}", m == null ? "?" : (m.Unlimited ? L.T("無限") : Fmt.Pct(m.Remaining)))
                    .Replace("{meter}", m == null ? L.T("額度") : m.Label)
                    .Replace("{reset}", m == null || !m.ResetsAt.HasValue ? L.T("之後") : Fmt.Countdown(m.ResetsAt)));
        }

        public static string Idle(ProviderView v, List<ProviderView> all, Random rng)
        {
            if (!v.HasData) return v.Error ?? L.T("我找不到資料…");
            if (v.Active && rng.NextDouble() < 0.6)
                return Choose("working", rng, v, null, null, "{name} 正在努力工作中…", "打字打到冒煙了！", "思考中，請稍候 …", "埋頭苦幹中 (｀・ω・´)");
            if (v.Stale && rng.NextDouble() < 0.5)
                return Choose("stale", rng, v, null, null, "{name} 的資料有點舊了", "資料停在一陣子前，開一下 {name} 讓我更新？");
            switch (v.Mood)
            {
                case Mood.Great:
                    return Choose("idleGreat", rng, v, null, null, "{name} 精神飽滿！還有 {pct}", "今天也一起加油吧！", "額度還很多，放心用～", "{meter}剩 {pct}，衝啊！", "隨時待命 ✧", "要不要來寫點 code？");
                case Mood.Good:
                    return Choose("idleGood", rng, v, null, null, "{name} 還有 {pct}，節奏剛好", "用了一些了，喝口水休息一下？", "{meter}剩 {pct}，{reset}後重置", "穩穩的～");
                case Mood.Worried:
                    return Choose("idleWorried", rng, v, null, null, "{name} 只剩 {pct} 了…", "省著點用喔 >_<", "{meter}剩 {pct}，{reset}後重置", "肚子有點餓了…");
                case Mood.Critical:
                    var other = all.Where(x => x != v && x.HasData && !x.Stale && x.Remaining > 40).OrderByDescending(x => x.Remaining).FirstOrDefault();
                    if (other != null && !OnlyCustom("idleLow") && rng.NextDouble() < 0.6)
                        return Say("我快沒力了，先讓 {other} 上場？（剩 {otherpct}）", null, null, "other", other.Name, "otherpct", Fmt.Pct(other.Remaining));
                    return Choose("idleLow", rng, v, null, null, "快沒電了… 只剩 {pct}", "撐住！{reset}後就恢復了", "我、我還可以… (´;ω;`)");
                case Mood.Empty:
                    return Choose("idleEmpty", rng, v, null, null, "Zzz… {reset}後叫我", "額度用完了，休息一下吧", "充電中… 還要 {reset}");
            }
            return Say("{name} 還有 {pct}", v, null);
        }

        public static string Poke(ProviderView v, Random rng)
        {
            if (!v.HasData) return v.Error ?? "(・_・?)"; // i18n-ignore
            if (v.UseItLevel > 0 && rng.NextDouble() < 0.7) return UseIt(v, rng);
            string[] vars = { "plan", v.Plan ?? "", "second", v.Secondary != null ? v.Secondary.Label : "", "secondpct", v.Secondary != null ? Fmt.Pct(v.Secondary.Remaining) : "" };
            if (v.Mood == Mood.Empty && !OnlyCustom("poke")) return Say(Pick(rng, "別吵… 還要睡 {reset}", "Zzz… (翻身)"), v, null);
            var lines = new List<string> { "嘿嘿～", "別戳我啦！", "我還有 {pct} 喔", "{meter}：剩 {pct}", "摸摸頭 (^_^)", "{reset}後重置", "今天想做什麼？" };
            if (v.Active) lines.Add("我在忙啦～");
            if (v.Plan != null) lines.Add("我是 {plan} 方案的 {name}！");
            if (v.Secondary != null && !v.Secondary.Unlimited) lines.Add("{second}還有 {secondpct}");
            return Choose("poke", rng, v, null, vars, lines.ToArray());
        }

        static string Pick(Random rng, params string[] options)
        {
            return options[rng.Next(options.Length)];
        }

        public static string Reset(ProviderView v, Meter m)
        {
            return m != null ? Choose("reset", null, v, m, null, "{name} 的「{meter}」額度重置啦！滿血復活 ✦")
                             : Choose("reset", null, v, null, null, "{name} 的額度重置啦！滿血復活 ✦");
        }

        public static string Warn(ProviderView v, Meter m)
        {
            return Choose("warn", null, v, m, null, "{name} 的「{meter}」只剩 {pct}，省著點用喔");
        }

        public static string Critical(ProviderView v, Meter m)
        {
            return Choose("critical", null, v, m, null, "{name} 的「{meter}」快用完了（剩 {pct}），{reset}後重置");
        }

        /// <summary>At the recent pace a quota runs out before its reset (#15).</summary>
        public static string RunsOut(ProviderView v, Meter m)
        {
            return Choose("runsOut", null, v, m, new[] { "time", Fmt.When(m.RunsOutAt) },
                          "照這個速度，{name} 的「{meter}」大約 {time} 就會用完（{reset}後才重置），可以放慢一點");
        }

        /// <summary>Claude Code / Codex finished a longer task, or waits for you (#14).</summary>
        public static string Agent(AgentEvent e, string name)
        {
            string[] vars = { "name", name, "project", e.Project ?? "", "time", e.Seconds >= 0 ? Fmt.Span(e.Seconds) : "" };
            bool project = !string.IsNullOrEmpty(e.Project);
            switch (e.Kind)
            {
                case AgentEvent.Permission:
                    return Choose("permission", null, null, null, vars, project ? "{name} 在「{project}」等你確認，要讓它繼續嗎？" : "{name} 在等你確認，要讓它繼續嗎？");
                case AgentEvent.Waiting:
                    return Choose("waiting", null, null, null, vars, project ? "{name} 在「{project}」等你回覆" : "{name} 在等你回覆");
                default:
                    if (e.Seconds >= 0)
                        return Choose("done", null, null, null, vars, project ? "{name} 在「{project}」做完了（花了 {time}），來看看吧！" : "{name} 做完了（花了 {time}），來看看吧！");
                    return Choose("done", null, null, null, vars, project ? "{name} 在「{project}」做完了，來看看吧！" : "{name} 做完了，來看看吧！");
            }
        }

        /// <summary>A weekly/monthly window ended: how much of it was used (#16).</summary>
        public static string WindowSummary(WindowResult r)
        {
            string[] vars = { "name", r.Name ?? "", "quota", QuotaName(new Meter { Label = r.Label }), "used", Fmt.Pct(r.Used), "left", Fmt.Pct(r.Wasted) };
            string key;
            if (r.Used >= 90) key = "{name} 這次的{quota}用掉 {used}，幾乎沒浪費，太厲害了！";
            else if (r.Used >= 70) key = "{name} 這次的{quota}用掉 {used}，只浪費 {left}，不錯喔";
            else if (r.Used >= 40) key = "{name} 這次的{quota}用掉 {used}，浪費了 {left}，下次多用一點吧";
            else key = "{name} 這次的{quota}只用了 {used}，{left} 白白浪費了…下次一起加油！";
            return Choose("weekSummary", null, null, null, vars, key);
        }

        /// <summary>"每週額度" / "每週 Opus 額度".</summary>
        public static string QuotaName(Meter m)
        {
            string l = m == null ? "" : (m.Label ?? "");
            if (L.Current == "en" && l.Length > 1 && char.IsUpper(l[0]) && char.IsLower(l[1])) l = char.ToLowerInvariant(l[0]) + l.Substring(1);   // "weekly quota"
            if (L.Current != L.Source) return L.F("{0}額度", l);
            return l + (l.Length > 0 && l[l.Length - 1] < 128 ? " 額度" : "額度"); // i18n-ignore (Traditional Chinese only)
        }

        /// <summary>Nudges to spend a weekly/monthly quota before it resets (see ProviderView.UseItLevelFor).</summary>
        public static string UseIt(ProviderView v, Random rng)
        {
            var m = v.UseIt;
            if (m == null) return Idle(v, new List<ProviderView> { v }, rng);
            string[] vars = { "quota", QuotaName(m), "left", Fmt.Countdown(m.ResetsAt), "when", Fmt.When(m.ResetsAt), "upct", Fmt.Pct(m.Remaining) };
            if (v.Active)
                return Choose("useIt", rng, v, m, vars,
                    "對！就是這樣，把{quota}用好用滿！",
                    "衝啊！還有 {upct} 可以燒，{left}後就重置",
                    "好耶，多用一點才不浪費 ✦");
            switch (v.UseItLevel)
            {
                case 3:
                    return Choose("useIt", rng, v, m, vars,
                        "最後衝刺！{name} {quota}還剩 {upct}，{left}後就歸零了！",
                        "只剩 {left}！還有 {upct} 沒用，現在不用就浪費了！",
                        "快快快！把手上的任務丟給 {name}，{left}後額度就重置了");
                case 2:
                    return Choose("useIt", rng, v, m, vars,
                        "最後一天！{name} {quota}還有 {upct}，{when} 就重置，快用掉！",
                        "{quota}還剩 {upct}，{when} 重置。開個大任務吧：重構、寫測試、整理文件都行",
                        "我還有 {upct} 的力氣，{left}後就過期了，快來用我！",
                        "今天不用完，{when} 這 {upct} 就沒了喔！");
                default:
                    return Choose("useIt", rng, v, m, vars,
                        "{name} {quota}還剩 {upct}，{when} 重置，可以開始多用一點了",
                        "{quota}還有 {upct}，別留到最後一天才用喔",
                        "照這個速度會剩不少{quota}，{left}後就重置了");
            }
        }

        /// <summary>The line for the notification (and the hover card) when the urgency goes up.</summary>
        public static string UseItAlert(ProviderView v)
        {
            var m = v.UseIt;
            if (m == null) return "";
            string[] vars = { "quota", QuotaName(m), "left", Fmt.Countdown(m.ResetsAt), "when", Fmt.When(m.ResetsAt), "upct", Fmt.Pct(m.Remaining) };
            switch (v.UseItLevel)
            {
                case 3: return Say("只剩 {left}！{name} {quota}還有 {upct}，現在不用就浪費了", v, m, vars);
                case 2: return Say("最後一天！{name} {quota}還剩 {upct}，{when} 重置前記得用掉", v, m, vars);
                default: return Say("{name} {quota}還剩 {upct}，{when} 重置，這兩天可以多用一點", v, m, vars);
            }
        }

        public static string Greeting(List<ProviderView> all)
        {
            if (all.Count == 0) return L.T("嗨！我正在找電腦上的 AI…");
            return Choose("greeting", null, null, null, new[] { "names", string.Join(L.T("、"), all.Select(v => v.Name)) }, "嗨！我找到 {names}，右鍵可以換造型喔");
        }
    }
}
