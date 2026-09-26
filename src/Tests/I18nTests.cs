using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentriPet
{
    /// <summary>
    /// Translations: every text in the program has an entry in every language, the placeholders match,
    /// the language picker understands system cultures, and switching changes what the pets say.
    /// </summary>
    static class I18nTests
    {
        public static void Run(TestKit t)
        {
            t.Section("多國語言");
            try
            {
                t.Run("I18n", () =>
                {
                    Resolve(t);
                    Tables(t);
                    Switching(t);
                });
            }
            finally
            {
                L.Use(L.Source);
            }
        }

        static void Resolve(TestKit t)
        {
            t.Equal("系統是繁中（台灣）→ 繁體中文", "zh-TW", L.Resolve("auto", "zh-TW"));
            t.Equal("系統是繁中（香港）→ 繁體中文", "zh-TW", L.Resolve("auto", "zh-HK"));
            t.Equal("系統是 zh-Hant → 繁體中文", "zh-TW", L.Resolve("auto", "zh-Hant-MO"));
            t.Equal("系統是簡中（中國）→ 簡體中文", "zh-CN", L.Resolve("auto", "zh-CN"));
            t.Equal("系統是簡中（新加坡、zh-Hans）→ 簡體中文", "zh-CN", L.Resolve("auto", "zh-Hans-SG"));
            t.Equal("系統是日文 → 日本語", "ja", L.Resolve("auto", "ja-JP"));
            t.Equal("系統是韓文 → 한국어", "ko", L.Resolve("auto", "ko-KR"));
            t.Equal("系統是英文 → English", "en", L.Resolve("auto", "en-US"));
            t.Equal("沒有翻譯的語言（法文）→ English", "en", L.Resolve("auto", "fr-FR"));
            t.Equal("系統語言未知 → English", "en", L.Resolve("auto", ""));
            t.Equal("手動選的語言優先於系統", "ja", L.Resolve("ja", "en-US"));
            t.Equal("設定壞掉的代碼 → 跟隨系統", "ko", L.Resolve("xx", "ko-KR"));
            t.Equal("沒有設定 → 跟隨系統", "zh-CN", L.Resolve(null, "zh-CN"));
            t.Equal("清單有 5 種語言，第一個是原文（繁中）", "zh-TW,zh-CN,en,ja,ko", string.Join(",", L.Languages.Select(x => x.Code)));
        }

        static readonly Regex Placeholder = new Regex(@"\{[a-z0-9]+\}");

        static string Placeholders(string s)
        {
            return string.Join(" ", Placeholder.Matches(s).Cast<Match>().Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal));
        }

        static void Tables(TestKit t)
        {
            string root = RepoRoot();
            var keys = root != null ? SourceTexts(root) : null;
            if (keys != null) t.Check("從原始碼找到要翻譯的文字", keys.Count > 300, keys.Count + " 句");
            else t.Skip("從原始碼找到要翻譯的文字", "找不到原始碼資料夾（只在原始碼旁執行時檢查）");

            foreach (var lang in L.Languages.Where(x => x.Code != L.Source))
            {
                var table = L.Load(lang.Code);
                t.Check(lang.Native + "：翻譯檔已內嵌在程式裡", table.Count > 300, table.Count + " 句");
                var badPh = table.Where(kv => !kv.Key.StartsWith("_") && Placeholders(kv.Key) != Placeholders(kv.Value)).Select(kv => kv.Key).ToList();
                t.Check(lang.Native + "：每句的 {0} {name} 等位置標記都和原文一致", badPh.Count == 0, string.Join(" | ", badPh.Take(5)));
                var empty = table.Where(kv => string.IsNullOrWhiteSpace(kv.Value) && !kv.Key.StartsWith("_")).Select(kv => kv.Key).ToList();
                t.Check(lang.Native + "：沒有空白的翻譯", empty.Count == 0, string.Join(" | ", empty.Take(5)));
                if (keys == null) continue;
                var missing = keys.Where(k => !table.ContainsKey(k)).ToList();
                t.Check(lang.Native + "：程式裡每一句都有翻譯", missing.Count == 0, missing.Count + " 句沒翻：" + string.Join(" | ", missing.Take(8)));
                var stale = table.Keys.Where(k => !k.StartsWith("_") && !keys.Contains(k)).ToList();
                t.Check(lang.Native + "：沒有已經用不到的舊翻譯", stale.Count == 0, stale.Count + " 句：" + string.Join(" | ", stale.Take(8)));
            }
        }

        static bool HasHan(string s)
        {
            return s.Any(c => c >= '\u4e00' && c <= '\u9fff');
        }

        static void Switching(TestKit t)
        {
            var soon = DateTime.UtcNow.AddHours(2).AddMinutes(3).AddSeconds(30);
            L.Use("en");
            t.Equal("切成英文：目前語言", "en", L.Current);
            t.Equal("英文：每週額度的名稱", "Weekly", Fmt.WindowLabel(10080));
            t.Equal("英文：3 小時的額度", "3-hour", Fmt.WindowLabel(180));
            t.Equal("英文：倒數", "2h 03m", Fmt.Countdown(soon));
            t.Equal("英文：幾分鐘前", "5 min ago", Fmt.Ago(DateTime.UtcNow.AddMinutes(-5.5)));
            t.Contains("英文：明天的時間", Fmt.When(DateTime.Today.AddDays(1).AddHours(9).ToUniversalTime()), "tomorrow 09:00");
            t.Equal("英文：星期幾", "Thu", Fmt.WeekDay(DayOfWeek.Thursday));

            // every line the pets can say, in every mood, comes out translated and filled in
            var rng = new Random(7);
            var sets = new[] { MockData.A(), MockData.B(), MockData.C() };
            var said = new List<string>();
            foreach (var views in sets)
                foreach (var v in views)
                    for (int i = 0; i < 40; i++)
                    {
                        said.Add(Lines.Idle(v, views, rng));
                        said.Add(Lines.Poke(v, rng));
                        if (v.UseIt != null) { said.Add(Lines.UseIt(v, rng)); said.Add(Lines.UseItAlert(v)); }
                    }
            foreach (var views in sets) said.Add(Lines.Greeting(views));
            var p = sets[1].First(x => x.HasData);
            said.Add(Lines.Warn(p, p.Primary));
            said.Add(Lines.Critical(p, p.Primary));
            said.Add(Lines.Reset(p, p.Primary));
            var han = said.Where(HasHan).Distinct().ToList();
            t.Check("英文：桌寵說的每一句都沒有中文", han.Count == 0, string.Join(" | ", han.Take(5)));
            var unfilled = said.Where(s => s.Contains("{")).Distinct().ToList();
            t.Check("英文：每一句的 {…} 都有填上", unfilled.Count == 0, string.Join(" | ", unfilled.Take(5)));
            var c3 = sets[2].FirstOrDefault(x => x.UseItLevel == 3 && x.UseIt != null);
            if (c3 != null) t.Contains("英文：週額度提醒寫成 weekly quota", Lines.UseItAlert(c3), "quota");
            t.Equal("英文：資料來源等文字跟著語言（沒偵測到）", "not detected", new Detection().EvidenceText);

            L.Use("ja");
            t.Equal("日文：每週額度的名稱", "週間", Fmt.WindowLabel(10080));
            t.Equal("日文：倒數", "2時間03分", Fmt.Countdown(soon));
            var jaFill = sets.SelectMany(views => views).Select(v => Lines.Idle(v, sets[0], rng)).Where(s => s.Contains("{")).ToList();
            t.Check("日文：每一句的 {…} 都有填上", jaFill.Count == 0, string.Join(" | ", jaFill.Take(3)));
            t.Check("日文：不加韓文用的斷行控制字元", !Fmt.Countdown(soon).Contains(L.WordJoiner) && !Lines.Idle(sets[0][0], sets[0], rng).Contains(L.WordJoiner));

            // Korean breaks lines only between words: neighbouring syllables get an invisible word joiner
            string wj = L.WordJoiner.ToString();
            t.Equal("韓文斷行：音節之間加上不斷行字元，空格照舊可以斷", "주" + wj + "간 한" + wj + "도 55% 남" + wj + "음", L.KeepWords("주간 한도 55% 남음"));
            t.Equal("韓文斷行：數字和音節連在一起（1일5시간 不會在 1 後面斷開）", "1" + wj + "일" + wj + "5" + wj + "시" + wj + "간 후", L.KeepWords("1일5시간 후"));
            t.Equal("韓文斷行：英文名字加助詞也連在一起", "Claude" + wj + "의 한" + wj + "도", L.KeepWords("Claude의 한도"));
            t.Equal("韓文斷行：做兩次結果一樣", L.KeepWords("1일5시간 후, 주간 한도"), L.KeepWords(L.KeepWords("1일5시간 후, 주간 한도")));
            t.Equal("韓文斷行：沒有韓文的字串原樣傳回", "5h 2h 03m", L.KeepWords("5h 2h 03m"));
            L.Use("ko");
            t.Equal("韓文：倒數", "2시간03분", Fmt.Countdown(soon).Replace(wj, ""));
            t.Equal("韓文：每月額度的名稱", "월간", Fmt.WindowLabel(43200).Replace(wj, ""));
            var koSaid = new List<string>();
            foreach (var views in sets)
                foreach (var v in views)
                    for (int i = 0; i < 30; i++)
                    {
                        koSaid.Add(Lines.Idle(v, views, rng));
                        koSaid.Add(Lines.Poke(v, rng));
                        if (v.UseIt != null) { koSaid.Add(Lines.UseIt(v, rng)); koSaid.Add(Lines.UseItAlert(v)); }
                    }
            koSaid.Add(Lines.Greeting(sets[0]));
            koSaid.Add(L.T("資料來源：Claude 讀取桌面版自己寫的用量快取（每 15 分鐘更新，重置時間為推算）；Codex 透過官方 codex app-server 即時查詢，並讀取本機對話紀錄；Copilot 讀取 CLI 的額度快取。本程式不讀取、也不傳送任何登入憑證。"));
            var split = koSaid.Where(s => s != L.KeepWords(s)).Distinct().ToList();
            t.Check("韓文：桌寵說的話、說明文字裡每個詞都不會被拆到兩行", split.Count == 0 && koSaid.Count > 50, string.Join(" | ", split.Take(3)));

            L.Use("zh-CN");
            t.Equal("簡體：每週額度的名稱", "每周", Fmt.WindowLabel(10080));
            t.Equal("簡體：沒有資料", "没有数据", L.T("沒有資料"));

            L.Use("zh-TW");
            t.Equal("切回繁中：原文不經過翻譯表", "每週", Fmt.WindowLabel(10080));
            t.Equal("繁中：倒數", "2時03分", Fmt.Countdown(soon));
            t.Equal("沒有翻譯的文字維持原文", "這句不在翻譯表裡", L.T("這句不在翻譯表裡"));
            t.Equal("翻譯文字裡的 {0} 會填入數值", "剩 42%", L.F("剩 {0}", "42%"));

            // the reset time can be typed in any of our languages
            DateTime? a, b, c, d;
            ClaudeProvider.TryParseWeekly("Thu 23:00", out a);
            ClaudeProvider.TryParseWeekly("木曜日 23:00", out b);
            ClaudeProvider.TryParseWeekly("목요일 23:00", out c);
            ClaudeProvider.TryParseWeekly("周四 23:00", out d);
            t.Check("每週重置時間：日文「木曜日 23:00」", b.HasValue && b == a, b + " vs " + a);
            t.Check("每週重置時間：韓文「목요일 23:00」", c.HasValue && c == a, c + " vs " + a);
            t.Check("每週重置時間：簡體「周四 23:00」", d.HasValue && d == a, d + " vs " + a);
        }

        // ------------------------------------------------------------------ the texts in the source code

        /// <summary>The repository folder (src/Core/I18n.cs next to it), or null when the program runs away from its source.</summary>
        static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; dir != null && i < 8; i++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "I18n.cs"))) return dir.FullName;
            return null;
        }

        static readonly Regex Literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");

        static bool IsCjk(char c)
        {
            return (c >= '\u3000' && c <= '\u9fff') || (c >= '\uff00' && c <= '\uffef') || (c >= '\uac00' && c <= '\ud7af');
        }

        /// <summary>
        /// Every string literal with Chinese/Japanese/Korean text in the program (tests excluded): the texts that need a translation.
        /// A line marked "i18n-ignore" is skipped (regular expressions, font names, language names).
        /// </summary>
        internal static HashSet<string> SourceTexts(string root)
        {
            var files = new List<string>();
            foreach (var sub in new[] { "src", "xplat" })
            {
                string dir = Path.Combine(root, sub);
                if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories));
            }
            var keys = new HashSet<string>();
            foreach (var f in files)
            {
                string n = f.Replace('\\', '/');
                if (n.Contains("/Tests/") || n.Contains("/obj/") || n.Contains("/bin/") || n.Contains("SentriPet.Tests")) continue;
                foreach (var raw in File.ReadAllLines(f, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("//") || line.StartsWith("[") || raw.Contains("i18n-ignore")) continue;
                    string code = StripComment(raw);
                    foreach (Match m in Literal.Matches(code))
                    {
                        if (m.Index > 0 && code[m.Index - 1] == '@') continue;
                        if (!m.Groups[1].Value.Any(IsCjk)) continue;
                        keys.Add(Unescape(m.Groups[1].Value));
                    }
                }
            }
            return keys;
        }

        static string StripComment(string line)
        {
            bool inString = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inString && c == '\\') { i++; continue; }
                if (c == '"') inString = !inString;
                else if (!inString && c == '/' && i + 1 < line.Length && line[i + 1] == '/') return line.Substring(0, i);
            }
            return line;
        }

        static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char e = s[++i];
                    if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'u' && i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(e);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
