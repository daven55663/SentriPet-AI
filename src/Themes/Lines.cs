using System;
using System.Collections.Generic;
using System.Linq;

namespace SentriPet
{
    /// <summary>Things the pets say.</summary>
    static class Lines
    {
        static string Pick(Random rng, params string[] options)
        {
            return options[rng.Next(options.Length)];
        }

        static string Fill(string s, ProviderView v, Meter m)
        {
            m = m ?? v.Primary;
            return s.Replace("{name}", v.Name)
                    .Replace("{pct}", m == null ? "?" : (m.Unlimited ? "無限" : Fmt.Pct(m.Remaining)))
                    .Replace("{meter}", m == null ? "額度" : m.Label)
                    .Replace("{reset}", m == null || !m.ResetsAt.HasValue ? "之後" : Fmt.Countdown(m.ResetsAt));
        }

        public static string Idle(ProviderView v, List<ProviderView> all, Random rng)
        {
            if (!v.HasData) return v.Error ?? "我找不到資料…";
            if (v.Active && rng.NextDouble() < 0.6)
                return Fill(Pick(rng, "{name} 正在努力工作中…", "打字打到冒煙了！", "思考中，請稍候 …", "埋頭苦幹中 (｀・ω・´)"), v, null);
            if (v.Stale && rng.NextDouble() < 0.5)
                return Fill(Pick(rng, "{name} 的資料有點舊了", "資料停在一陣子前，開一下 {name} 讓我更新？"), v, null);
            switch (v.Mood)
            {
                case Mood.Great:
                    return Fill(Pick(rng, "{name} 精神飽滿！還有 {pct}", "今天也一起加油吧！", "額度還很多，放心用～", "{meter}剩 {pct}，衝啊！", "隨時待命 ✧", "要不要來寫點 code？"), v, null);
                case Mood.Good:
                    return Fill(Pick(rng, "{name} 還有 {pct}，節奏剛好", "用了一些了，喝口水休息一下？", "{meter}剩 {pct}，{reset}後重置", "穩穩的～"), v, null);
                case Mood.Worried:
                    return Fill(Pick(rng, "{name} 只剩 {pct} 了…", "省著點用喔 >_<", "{meter}剩 {pct}，{reset}後重置", "肚子有點餓了…"), v, null);
                case Mood.Critical:
                    var other = all.Where(x => x != v && x.HasData && !x.Stale && x.Remaining > 40).OrderByDescending(x => x.Remaining).FirstOrDefault();
                    if (other != null && rng.NextDouble() < 0.6)
                        return "我快沒力了，先讓 " + other.Name + " 上場？（剩 " + Fmt.Pct(other.Remaining) + "）";
                    return Fill(Pick(rng, "快沒電了… 只剩 {pct}", "撐住！{reset}後就恢復了", "我、我還可以… (´;ω;`)"), v, null);
                case Mood.Empty:
                    return Fill(Pick(rng, "Zzz… {reset}後叫我", "額度用完了，休息一下吧", "充電中… 還要 {reset}"), v, null);
            }
            return Fill("{name} 還有 {pct}", v, null);
        }

        public static string Poke(ProviderView v, Random rng)
        {
            if (!v.HasData) return v.Error ?? "(・_・?)";
            if (v.Mood == Mood.Empty) return Fill(Pick(rng, "別吵… 還要睡 {reset}", "Zzz… (翻身)"), v, null);
            var lines = new List<string> { "嘿嘿～", "別戳我啦！", "我還有 {pct} 喔", "{meter}：剩 {pct}", "摸摸頭 (^_^)", "{reset}後重置", "今天想做什麼？" };
            if (v.Active) lines.Add("我在忙啦～");
            if (v.Plan != null) lines.Add("我是 " + v.Plan + " 方案的 {name}！");
            if (v.Secondary != null && !v.Secondary.Unlimited) lines.Add(v.Secondary.Label + "還有 " + Fmt.Pct(v.Secondary.Remaining));
            return Fill(lines[rng.Next(lines.Count)], v, null);
        }

        public static string Reset(ProviderView v, Meter m)
        {
            return v.Name + (m != null ? " 的「" + m.Label + "」" : " 的") + "額度重置啦！滿血復活 ✦";
        }

        public static string Warn(ProviderView v, Meter m)
        {
            return Fill("{name} 的「{meter}」只剩 {pct}，省著點用喔", v, m);
        }

        public static string Critical(ProviderView v, Meter m)
        {
            return Fill("{name} 的「{meter}」快用完了（剩 {pct}），{reset}後重置", v, m);
        }

        public static string Greeting(List<ProviderView> all)
        {
            if (all.Count == 0) return "嗨！我正在找電腦上的 AI…";
            return "嗨！我找到 " + string.Join("、", all.Select(v => v.Name)) + "，右鍵可以換造型喔";
        }
    }
}
