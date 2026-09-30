using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentriPet
{
    /// <summary>The user's own themes (#24): reading theme.json, and why a theme can't be used.</summary>
    static class CustomThemeTests
    {
        public static void Run(TestKit t)
        {
            t.Section("自訂造型（#24）：theme.json");
            try
            {
                t.Run("Format", () => Format(t));
                t.Run("Mistakes", () => Mistakes(t));
                t.Run("Files", () => Files(t));
                t.Run("Example", () => Example(t));
            }
            finally { L.Use(L.Source); }
        }

        const string Minimal = "{ \"name\": \"Mini\", \"card\": { \"width\": 100, \"height\": 80, \"elements\": [ { \"type\": \"text\", \"text\": \"{name} {pct}\" } ] } }";

        static ProviderView Claude()
        {
            var v = MockData.A()[0];
            v.Active = false;
            return v;
        }

        static void Format(TestKit t)
        {
            var s = ThemeSpec.Parse(Minimal, "mini", null);
            t.Check("最小的造型：可以用、沒有問題", s.Usable && s.Problems.Count == 0 && s.Elements.Count == 1, string.Join(" | ", s.Problems));
            t.Equal("id 是 custom: 加資料夾名稱", "custom:mini", s.Id);
            t.Equal("預設排成一列、卡片 100×80", "row 100x80", s.Layout + " " + s.CardWidth + "x" + s.CardHeight);

            var v = Claude();
            var m = v.Headline ?? v.Primary;
            t.Equal("文字的記號", "Claude " + Fmt.Pct(m.Remaining), ThemeSpec.TextFor(s.Elements[0], v));

            string json = "{ \"name\": { \"en\": \"Cloud\", \"zh-TW\": \"雲\", \"ja\": \"雲だ\" }, \"layout\": \"grid\", \"columns\": 2, " +
                          "\"card\": { \"width\": 5000, \"elements\": [ " +
                          "{ \"type\": \"image\", \"width\": 10, \"height\": 10, \"states\": { \"great\": \"a.png\", \"working\": \"w.png\", \"default\": \"d.png\" } }, " +
                          "{ \"type\": \"bar\", \"width\": 50, \"height\": 4, \"meter\": 1, \"fill\": \"provider\" }, " +
                          "{ \"type\": \"ring\", \"width\": 40, \"when\": \"working\" }, " +
                          "{ \"type\": \"rect\", \"width\": 5, \"height\": 5, \"color\": \"#80FF0000\" } ] } }";
            L.Use("en");
            s = ThemeSpec.Parse(json, "cloud", null);
            t.Equal("名稱依語言（英文）", "Cloud", s.Name);
            L.Use("ko");
            t.Equal("沒有的語言：用英文", "Cloud", ThemeSpec.Parse(json, "cloud", null).Name);
            L.Use(L.Source);
            t.Equal("名稱依語言（繁中）", "雲", ThemeSpec.Parse(json, "cloud", null).Name);
            t.Check("太大的數字會被限制（卡片寬 600）、grid 2 欄", s.CardWidth == 600 && s.Layout == "grid" && s.Columns == 2);
            t.Check("每種元素都讀得懂", s.Usable && s.Problems.Count == 0 && string.Join(",", s.Elements.Select(e => e.Type)) == "image,bar,ring,rect", string.Join(" | ", s.Problems));
            t.Equal("meter 可以寫數字（第幾個額度）", "1", s.Elements[1].Meter);
            t.Check("meter 1 = 第二個額度", ThemeSpec.MeterFor(s.Elements[1], v) == v.Meters[1]);

            var img = s.Elements[0];
            v.Mood = Mood.Great;
            t.Equal("圖片依心情", "a.png", ThemeSpec.PictureFor(img, v));
            v.Mood = Mood.Worried;
            t.Equal("沒有那個心情的圖：用 default", "d.png", ThemeSpec.PictureFor(img, v));
            v.Active = true;
            t.Equal("工作中：用 working 的圖", "w.png", ThemeSpec.PictureFor(img, v));
            t.Check("when: working 只在工作中顯示", ThemeSpec.Shown(s.Elements[2], v) && !ThemeSpec.Shown(s.Elements[2], Claude()));
            var none = new ProviderView { Id = "x", Name = "X", Error = "no" };
            t.Equal("沒資料：unknown", "unknown", ThemeSpec.StateOf(none));
            t.Equal("沒資料時 {pct} 是 ?", "X ?", ThemeSpec.TextFor(ThemeSpec.Parse(Minimal, "mini", null).Elements[0], none));
        }

        static void Mistakes(TestKit t)
        {
            var s = ThemeSpec.Parse("{ \"name\": ", "broken", null);
            t.Check("JSON 壞掉：不能用、說出原因、不會當掉", !s.Usable && s.Problems.Count == 1 && s.Name == "broken", string.Join(" | ", s.Problems));
            s = ThemeSpec.Parse("{ \"name\": \"x\" }", "x", null);
            t.Check("沒有 card：不能用", !s.Usable && s.Problems.Count == 1);
            s = ThemeSpec.Parse("{ \"card\": { \"elements\": [] } }", "x", null);
            t.Check("沒有元素：不能用", !s.Usable && s.Problems.Count >= 1);
            s = ThemeSpec.Parse("{ \"card\": { \"elements\": [ { \"type\": \"video\" }, 3, { \"type\": \"text\", \"text\": \"{nmae}\", \"color\": \"blue\" }, { \"type\": \"bar\" } ] } }", "x", null);
            t.Check("不認得的元素、不是物件、少了尺寸的略過；可以用的留下", s.Usable && s.Elements.Count == 1 && s.Elements[0].Type == "text");
            t.Check("每個錯都說出來（type、非物件、記號、顏色、尺寸）", s.Problems.Count == 5, s.Problems.Count + ": " + string.Join(" | ", s.Problems));
            t.Check("看不懂的顏色：用預設的白色", s.Elements[0].Color == "#FFFFFF");
            s = ThemeSpec.Parse("{ \"layout\": \"diagonal\", \"version\": 9, \"card\": { \"elements\": [ { \"type\": \"text\", \"text\": \"a\", \"when\": \"sometimes\", \"meter\": \"third\" } ] } }", "x", null);
            t.Check("layout、when、meter 寫錯：說出來、用預設值；新版本的格式：提醒", s.Usable && s.Layout == "row" && s.Elements[0].When == null && s.Elements[0].Meter == "headline" && s.Problems.Count == 4,
                    string.Join(" | ", s.Problems));
            s = ThemeSpec.Parse("{ \"card\": { \"elements\": [ { \"type\": \"image\", \"width\": 9, \"height\": 9, \"image\": \"../../secret.png\" }, " +
                                "{ \"type\": \"image\", \"width\": 9, \"height\": 9, \"image\": \"C:/Windows/x.png\" }, " +
                                "{ \"type\": \"image\", \"width\": 9, \"height\": 9, \"image\": \"run.exe\" }, " +
                                "{ \"type\": \"image\", \"width\": 9, \"height\": 9, \"states\": { \"sad\": \"a.png\", \"good\": \"/etc/b.png\" } } ] } }", "x", null);
            t.Check("圖片只能在造型的資料夾裡、只能是圖片：全部略過", !s.Usable && s.Elements.Count == 0, string.Join(" | ", s.Problems));
            t.Check("每個都說出原因", s.Problems.Count >= 8, s.Problems.Count + "");
            var many = "{ \"card\": { \"elements\": [" + string.Join(",", Enumerable.Repeat("{ \"type\": \"text\", \"text\": \"a\" }", 70)) + "] } }";
            s = ThemeSpec.Parse(many, "x", null);
            t.Check("元素太多：只用前 60 個", s.Elements.Count == ThemeSpec.MaxElements && s.Problems.Count == 1);
        }

        static void Files(TestKit t)
        {
            string dir = t.TempDir("themes");
            Directory.CreateDirectory(Path.Combine(dir, "b-theme"));
            File.WriteAllText(Path.Combine(dir, "b-theme", "theme.json"), "{ \"card\": { \"elements\": [ { \"type\": \"image\", \"width\": 9, \"height\": 9, \"image\": \"face.png\" }, { \"type\": \"text\", \"text\": \"hi\" } ] } }");
            Directory.CreateDirectory(Path.Combine(dir, "a-theme"));
            File.WriteAllText(Path.Combine(dir, "a-theme", "theme.json"), Minimal);
            File.WriteAllBytes(Path.Combine(dir, "a-theme", "unused.png"), new byte[] { 1, 2, 3 });
            Directory.CreateDirectory(Path.Combine(dir, "not-a-theme"));
            var all = ThemeSpec.LoadAll(dir);
            t.Equal("有 theme.json 的資料夾才算，依名稱排序", "custom:a-theme,custom:b-theme", string.Join(",", all.Select(s => s.Id)));
            var b = all[1];
            t.Check("找不到圖片：那個元素略過、說出檔名，其他照樣用", b.Usable && b.Elements.Count == 1 && b.Problems.Any(x => x.Contains("face.png")), string.Join(" | ", b.Problems));
            File.WriteAllBytes(Path.Combine(dir, "b-theme", "face.png"), new byte[] { 137, 80, 78, 71 });
            t.Check("圖片檔存在：讀得到", ThemeSpec.Load(Path.Combine(dir, "b-theme")).Elements.Count == 2);
            t.Equal("沒有造型資料夾：空的", 0, ThemeSpec.LoadAll(Path.Combine(dir, "nothing-here")).Count);
        }

        /// <summary>examples/themes/cloud, when the tests run in the repository.</summary>
        static void Example(TestKit t)
        {
            string dir = AppContext.BaseDirectory;
            string example = null;
            for (int i = 0; i < 8 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            {
                var c = Path.Combine(dir, "examples", "themes", "cloud");
                if (File.Exists(Path.Combine(c, "theme.json"))) { example = c; break; }
            }
            if (example == null) { t.Skip("範例造型", "找不到 examples/themes/cloud（不是在原始碼資料夾裡執行）"); return; }
            var s = ThemeSpec.Load(example);
            t.Check("範例造型（小雲朵）：可以用、沒有問題", s.Usable && s.Problems.Count == 0, string.Join(" | ", s.Problems));
            t.Check("範例造型：七種狀態的圖都有", s.Elements.Any(e => e.Type == "image" && ThemeSpec.StateNames.All(n => e.States.ContainsKey(n))));
            foreach (var lang in L.Languages)
            {
                L.Use(lang.Code);
                var n = ThemeSpec.Load(example);
                t.Check("範例造型：" + lang.Native + " 有自己的名稱和說明", n.Name != "cloud" && !string.IsNullOrEmpty(n.Mood) && !string.IsNullOrEmpty(n.Blurb) && (lang.Code == "en" || n.Name != "Little Cloud"), n.Name);
            }
            L.Use(L.Source);
        }
    }
}
