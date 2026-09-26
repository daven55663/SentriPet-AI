using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    class LangInfo
    {
        public string Code;       // zh-TW, zh-CN, en, ja, ko
        public string Native;     // the language's own name, shown in the picker
        public LangInfo(string code, string native) { Code = code; Native = native; }
    }

    /// <summary>
    /// Translations. The Traditional Chinese text in the code is the key; src/Lang/{code}.json maps it to the other
    /// languages (embedded in the program). Sentences with values use numbered placeholders: L.F("剩 {0}", pct).
    /// A missing translation falls back to the Traditional Chinese text.
    /// </summary>
    static class L
    {
        public const string Source = "zh-TW";

        public static readonly List<LangInfo> Languages = new List<LangInfo>
        {
            new LangInfo("zh-TW", "繁體中文"), // i18n-ignore
            new LangInfo("zh-CN", "简体中文"), // i18n-ignore
            new LangInfo("en", "English"), // i18n-ignore
            new LangInfo("ja", "日本語"), // i18n-ignore
            new LangInfo("ko", "한국어"), // i18n-ignore
        };

        static string current = Source;
        static Dictionary<string, string> table;
        static readonly Dictionary<string, Dictionary<string, string>> loaded = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>The language in use (a code from <see cref="Languages"/>).</summary>
        public static string Current { get { return current; } }

        /// <summary>Picks the language for a setting: a code, or "auto" (= the system's display language).</summary>
        public static void Use(string setting)
        {
            current = Resolve(setting, CultureInfo.CurrentUICulture.Name);
            table = current == Source ? null : Load(current);
        }

        /// <summary>"auto" and system culture names → one of our languages (English when we don't have it).</summary>
        public static string Resolve(string setting, string systemCulture)
        {
            if (!string.IsNullOrEmpty(setting) && setting != "auto" && Languages.Any(l => l.Code == setting)) return setting;
            string c = (systemCulture ?? "").ToLowerInvariant();
            if (c.StartsWith("zh"))
                return c.Contains("hans") || c.EndsWith("-cn") || c.EndsWith("-sg") || c == "zh" ? "zh-CN" : "zh-TW";
            if (c.StartsWith("ja")) return "ja";
            if (c.StartsWith("ko")) return "ko";
            return "en";
        }

        /// <summary>Translates a Traditional Chinese text.</summary>
        public static string T(string zh)
        {
            if (zh == null || table == null) return zh;
            string t;
            return table.TryGetValue(zh, out t) && !string.IsNullOrEmpty(t) ? t : zh;
        }

        /// <summary>Translates a sentence with placeholders {0}, {1}… and fills them in.</summary>
        public static string F(string zh, params object[] args)
        {
            string pattern = T(zh), s;
            try { s = string.Format(CultureInfo.InvariantCulture, pattern, args); }
            catch (FormatException) { s = string.Format(CultureInfo.InvariantCulture, zh, args); }
            return current == "ko" ? KeepWords(s) : s;
        }

        /// <summary>Invisible "no line break here" (U+2060 WORD JOINER).</summary>
        public const char WordJoiner = '\u2060';

        static bool IsHangul(char c)
        {
            return (c >= '\uAC00' && c <= '\uD7A3') || (c >= '\u1100' && c <= '\u11FF') || (c >= '\u3130' && c <= '\u318F');
        }

        /// <summary>
        /// Korean breaks lines only at spaces, but WPF and Avalonia break it between any two syllables (the way Chinese is broken),
        /// splitting words like 초기화 (or 1일5시간 after the digit) across lines. A word joiner between a syllable and any
        /// neighbouring character that isn't a space keeps each word together. Used for Korean only; running it twice changes nothing.
        /// </summary>
        public static string KeepWords(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder sb = null;
            for (int i = 1; i < s.Length; i++)
            {
                char a = s[i - 1], b = s[i];
                bool join = (IsHangul(a) || IsHangul(b)) && !char.IsWhiteSpace(a) && !char.IsWhiteSpace(b) && a != WordJoiner && b != WordJoiner;
                if (!join) { if (sb != null) sb.Append(b); continue; }
                if (sb == null) sb = new StringBuilder(s.Length + 16).Append(s, 0, i);
                sb.Append(WordJoiner).Append(s[i]);
            }
            return sb == null ? s : sb.ToString();
        }

        /// <summary>Text made outside T/F (templates filled in piece by piece) gets the Korean word joiners here.</summary>
        public static string Finish(string s)
        {
            return current == "ko" ? KeepWords(s) : s;
        }

        /// <summary>"語言 · Language": the language menu also says "Language", so it can be found whatever language is showing.</summary>
        public static string LanguageLabel
        {
            get
            {
                string t = T("語言");
                return t == "Language" ? t : t + " · Language";
            }
        }

        /// <summary>Marks a text as translatable where it is stored (arrays, tables) and translated later with T().</summary>
        public static string N(string zh) { return zh; }

        /// <summary>The translation table of a language (for the self-test).</summary>
        internal static Dictionary<string, string> Load(string code)
        {
            Dictionary<string, string> d;
            if (loaded.TryGetValue(code, out d)) return d;
            d = new Dictionary<string, string>();
            try
            {
                var asm = typeof(L).Assembly;
                using (var s = asm.GetManifestResourceStream("SentriPet.Lang." + code + ".json"))
                {
                    if (s != null)
                    {
                        string json;
                        using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                        var o = Json.Obj(Json.Parse(json));
                        if (o != null) foreach (var kv in o) { var v = Json.Str(kv.Value); if (v != null) d[kv.Key] = code == "ko" ? KeepWords(v) : v; }
                    }
                    else Log.Warn("no translation for " + code);
                }
            }
            catch (Exception ex) { Log.Error("load language " + code, ex); }
            loaded[code] = d;
            return d;
        }

        /// <summary>Which writing system the current language needs (the UIs pick fonts from it).</summary>
        public static string Script
        {
            get
            {
                switch (current)
                {
                    case "zh-CN": return "sc";
                    case "ja": return "ja";
                    case "ko": return "ko";
                    case "en": return "latin";
                    default: return "tc";
                }
            }
        }
    }
}
