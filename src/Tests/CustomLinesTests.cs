using System;
using System.Collections.Generic;
using System.Linq;

namespace SentriPet
{
    /// <summary>The user's own lines (#25): lines.json.</summary>
    static class CustomLinesTests
    {
        public static void Run(TestKit t)
        {
            t.Section("自訂台詞（#25）：lines.json");
            try
            {
                t.Run("Custom lines", () => Body(t));
                t.Run("Example file", () => Example(t));
            }
            finally
            {
                CustomLines.Override = null;
                CustomLines.Reload();
                L.Use(L.Source);
            }
        }

        static void Use(string json)
        {
            CustomLines.Override = json;
            CustomLines.Reload();
        }

        static ProviderView Claude(Mood mood)
        {
            var v = MockData.A()[0];
            v.Mood = mood;
            v.UseIt = null;
            v.UseItLevel = 0;
            v.Active = false;
            v.Stale = false;
            return v;
        }

        static void Body(TestKit t)
        {
            L.Use(L.Source);
            var v = Claude(Mood.Great);
            var m = v.Headline ?? v.Primary;
            var rng = new Random(7);

            Use("{ \"zh-TW\": { \"poke\": [\"戳 {name}，剩 {pct}\"] } }");
            t.Equal("有自訂台詞：用自訂的，記號照樣填入", "戳 Claude，剩 " + Fmt.Pct(m.Remaining), Lines.Poke(v, rng));
            t.Equal("沒有問題", 0, CustomLines.Problems.Count);
            t.Equal("算得出目前語言有幾句", 1, CustomLines.Count);

            Use("{ \"zh-TW\": { \"poke\": [\"哈囉 {nmae}\", \"   \"] } }");
            t.Check("記號寫錯：那一句略過，退回內建台詞", CustomLines.For("poke") == null && !Lines.Poke(v, rng).Contains("{"));
            t.Check("記號寫錯：說出是哪個記號", CustomLines.Problems.Count == 1 && CustomLines.Problems[0].Contains("{nmae}"), string.Join(" | ", CustomLines.Problems));
            Use("{ \"zh-TW\": { \"idleLow\": [\"換 {other} 上場\"] } }");
            t.Check("只有內建台詞才有的記號（{other}）也不能用", CustomLines.For("idleLow") == null && CustomLines.Problems.Count == 1);

            Use("{ \"zh-TW\": { \"poke\": [\"好\", \"{name} 錯 {x}\"] } }");
            t.Check("同一組裡寫對的照樣用", CustomLines.For("poke").SequenceEqual(new[] { "好" }) && CustomLines.Problems.Count == 1);

            Use("{ \"mix\": true, \"zh-TW\": { \"poke\": [\"自訂的一句\"] } }");
            var said = Enumerable.Range(0, 300).Select(i => Lines.Poke(v, rng)).ToList();
            t.Check("mix：自訂的和內建的都會說", said.Contains("自訂的一句") && said.Any(s => s != "自訂的一句"), said.Count(s => s == "自訂的一句") + " / 300");

            Use("{ \"en\": { \"poke\": [\"poke en\"] }, \"all\": { \"greeting\": [\"hi {names}\"] } }");
            t.Check("別的語言的台詞不會用在繁中", Lines.Poke(v, rng) != "poke en");
            L.Use("en");
            CustomLines.Reload();
            t.Equal("英文用 en 那一組", "poke en", Lines.Poke(v, rng));
            t.Check("all：每種語言都用", Lines.Greeting(MockData.A()).StartsWith("hi Claude"), Lines.Greeting(MockData.A()));
            L.Use("ja");
            CustomLines.Reload();
            t.Check("日文沒有自己的一組：poke 用內建的，greeting 用 all", Lines.Poke(v, rng) != "poke en" && Lines.Greeting(MockData.A()).StartsWith("hi "));
            L.Use("ko");
            Use("{ \"ko\": { \"poke\": [\"안녕하세요 {name}님\"] } }");
            string ko = Lines.Poke(v, rng);
            t.Check("韓文的自訂台詞也加上不斷行的記號（L.Finish）", ko.Contains(L.WordJoiner) && ko.Replace(L.WordJoiner.ToString(), "") == "안녕하세요 Claude님", ko);
            L.Use(L.Source);

            Use("{ \"zh-TW\": ");
            t.Check("JSON 壞掉：一個問題、全部用內建的", CustomLines.Problems.Count == 1 && CustomLines.For("poke") == null && CustomLines.Count == 0,
                    string.Join(" | ", CustomLines.Problems));
            Use("{ \"xx\": { \"poke\": [\"a\"] }, \"zh-TW\": { \"pokee\": [\"b\"], \"warn\": 3 } }");
            t.Equal("不認得的語言、不認得的事件都會說出來", 2, CustomLines.Problems.Count);

            Use("{ \"zh-TW\": { \"warn\": \"小心，{name} 的{meter}剩 {pct}\" } }");
            t.Equal("只寫一句（不是陣列）也可以；額度提醒", "小心，Claude 的" + m.Label + "剩 " + Fmt.Pct(m.Remaining), Lines.Warn(v, m));

            Use("{ \"zh-TW\": { \"done\": [\"{name} 好了（{time}，{project}）\"], \"waiting\": [\"{name} 等你\"] } }");
            t.Equal("AI 做完", "Claude 好了（" + Fmt.Span(90) + "，shop）",
                    Lines.Agent(new AgentEvent { Kind = AgentEvent.Done, Seconds = 90, Project = "shop" }, "Claude"));
            t.Equal("在等你", "Claude 等你", Lines.Agent(new AgentEvent { Kind = AgentEvent.Waiting }, "Claude"));
            t.Check("沒寫的事件（要你確認）用內建的", Lines.Agent(new AgentEvent { Kind = AgentEvent.Permission }, "Claude").Contains("確認"));

            var low = Claude(Mood.Critical);
            var others = new List<ProviderView> { low, Claude(Mood.Great) };
            others[1].Name = "Codex";
            others[1].Remaining = 90;
            Use("{ \"zh-TW\": { \"idleLow\": [\"沒力了\"] } }");
            t.Check("只用自訂的時候，不會混進內建的「讓別人上場」", Enumerable.Range(0, 60).All(i => Lines.Idle(low, others, rng) == "沒力了"));

            Use("{ \"zh-TW\": { \"weekSummary\": [\"{name} {quota}用了 {used}，剩 {left}\"] } }");
            var r = new WindowResult { Name = "Claude", Label = "每週", Used = 80 };
            t.Equal("週報總結", "Claude " + Lines.QuotaName(new Meter { Label = "每週" }) + "用了 " + Fmt.Pct(80) + "，剩 " + Fmt.Pct(r.Wasted), Lines.WindowSummary(r));
        }

        static void Example(TestKit t)
        {
            L.Use(L.Source);
            Use(CustomLines.Example);
            t.Check("範例檔：讀得懂、沒有問題", CustomLines.Problems.Count == 0, string.Join(" | ", CustomLines.Problems));
            t.Check("範例檔：繁中有自訂台詞、和內建的混著說", CustomLines.For("poke") != null && CustomLines.Mix);
            L.Use("en");
            t.Check("範例檔：英文也有", CustomLines.For("poke") != null && CustomLines.For("poke")[0].StartsWith("Hey"));
            L.Use(L.Source);
            var missing = CustomLines.Events.Keys.Where(e => !CustomLines.Example.Contains(e)).ToList();
            t.Check("每個事件都寫在範例檔的說明裡", missing.Count == 0, string.Join(", ", missing));
        }
    }
}
