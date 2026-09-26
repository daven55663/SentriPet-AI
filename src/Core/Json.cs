using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SentriPet
{
    /// <summary>
    /// Tolerant JSON reader/writer (objects become Dictionary&lt;string, object&gt;, arrays object[]).
    /// Accepts comments, trailing commas, NaN/Infinity, a BOM and JSON Lines (several top-level values → object[]).
    /// Error messages carry positions only, never file content.
    /// </summary>
    static class Json
    {
        public static object Parse(string text)
        {
            return new Reader(text ?? "").ParseDocument();
        }

        public static object TryParse(string text)
        {
            string err;
            return TryParse(text, out err);
        }

        public static object TryParse(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text)) { error = L.T("空白內容"); return null; }
            try { return Parse(text); }
            catch (FormatException ex) { error = ex.Message; return null; }
        }

        sealed class Reader
        {
            readonly string s;
            int i;

            public Reader(string text) { s = text; }

            public object ParseDocument()
            {
                Skip();
                if (i >= s.Length) throw Fail(L.T("沒有內容"));
                var first = Value();
                Skip();
                if (i >= s.Length) return first;
                var many = new List<object> { first };   // JSON Lines / concatenated values
                while (i < s.Length)
                {
                    many.Add(Value());
                    Skip();
                }
                return many.ToArray();
            }

            FormatException Fail(string what)
            {
                string code = i < s.Length ? L.F(" (字元碼 {0})", (int)s[i]) : "";
                int line = 1;
                for (int k = 0; k < i && k < s.Length; k++) if (s[k] == '\n') line++;
                return new FormatException(L.F("JSON 第 {0} 行、位置 {1}：{2}", line, i, what) + code);
            }

            void Skip()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '﻿' || c == ' ') { i++; continue; }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                    {
                        while (i < s.Length && s[i] != '\n') i++;
                        continue;
                    }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                    {
                        int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        i = end < 0 ? s.Length : end + 2;
                        continue;
                    }
                    break;
                }
            }

            object Value()
            {
                Skip();
                if (i >= s.Length) throw Fail(L.T("內容提早結束"));
                char c = s[i];
                switch (c)
                {
                    case '{': return Obj();
                    case '[': return Arr();
                    case '"': case '\'': return Str();
                    case 't': Word("true"); return true;
                    case 'f': Word("false"); return false;
                    case 'n': Word("null"); return null;
                    case 'N': Word("NaN"); return double.NaN;
                    case 'I': Word("Infinity"); return double.PositiveInfinity;
                }
                if (c == '-' || c == '+' || c == '.' || (c >= '0' && c <= '9')) return Number();
                throw Fail(L.T("無法辨識的字元"));
            }

            void Word(string w)
            {
                if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) throw Fail(L.F("預期 {0}", w));
                i += w.Length;
            }

            Dictionary<string, object> Obj()
            {
                i++;
                var d = new Dictionary<string, object>();
                while (true)
                {
                    Skip();
                    if (i >= s.Length) throw Fail(L.T("物件沒有結尾"));
                    if (s[i] == '}') { i++; return d; }
                    string key = (s[i] == '"' || s[i] == '\'') ? Str() : Ident();
                    Skip();
                    if (i >= s.Length || s[i] != ':') throw Fail(L.T("預期 ':'"));
                    i++;
                    d[key] = Value();
                    Skip();
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw Fail(L.T("預期 ',' 或 '}'"));
                }
            }

            object[] Arr()
            {
                i++;
                var l = new List<object>();
                while (true)
                {
                    Skip();
                    if (i >= s.Length) throw Fail(L.T("陣列沒有結尾"));
                    if (s[i] == ']') { i++; return l.ToArray(); }
                    l.Add(Value());
                    Skip();
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l.ToArray(); }
                    throw Fail(L.T("預期 ',' 或 ']'"));
                }
            }

            string Ident()
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$' || s[i] == '-')) i++;
                if (i == start) throw Fail(L.T("預期屬性名稱"));
                return s.Substring(start, i - start);
            }

            string Str()
            {
                char q = s[i++];
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw Fail(L.T("字串沒有結尾"));
                    char c = s[i++];
                    if (c == q) return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw Fail(L.T("字串沒有結尾"));
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Fail(L.T("\\u 跳脫不完整"));
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }

            object Number()
            {
                int start = i;
                if (s[i] == '-' || s[i] == '+') i++;
                if (i < s.Length && s[i] == 'I') { Word("Infinity"); return s[start] == '-' ? double.NegativeInfinity : double.PositiveInfinity; }
                bool frac = false;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c >= '0' && c <= '9') { i++; continue; }
                    if (c == '.' || c == 'e' || c == 'E' || ((c == '-' || c == '+') && (s[i - 1] == 'e' || s[i - 1] == 'E'))) { frac = true; i++; continue; }
                    break;
                }
                string t = s.Substring(start, i - start);
                if (!frac)
                {
                    long lv;
                    if (long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out lv))
                        return (lv >= int.MinValue && lv <= int.MaxValue) ? (object)(int)lv : lv;
                }
                double dv;
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) return dv;
                throw Fail(L.T("數字格式錯誤"));
            }
        }

        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }

        public static IList Arr(object o) { return o as IList; }

        public static object Get(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d == null || key == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>Path lookup: "$.a.b[0].c", "a.b.0.c" or "a['x y']".</summary>
        public static object Path(object root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            string p = path.Trim();
            if (p.StartsWith("$")) p = p.Substring(1);
            object cur = root;
            int i = 0;
            while (i < p.Length && cur != null)
            {
                char c = p[i];
                if (c == '.') { i++; continue; }
                if (c == '[')
                {
                    int end = p.IndexOf(']', i);
                    if (end < 0) return null;
                    string idx = p.Substring(i + 1, end - i - 1).Trim().Trim('\'', '"');
                    i = end + 1;
                    cur = Step(cur, idx);
                    continue;
                }
                int j = i;
                while (j < p.Length && p[j] != '.' && p[j] != '[') j++;
                cur = Step(cur, p.Substring(i, j - i));
                i = j;
            }
            return cur;
        }

        static object Step(object cur, string key)
        {
            var list = cur as IList;
            int n;
            if (list != null && int.TryParse(key, out n))
            {
                if (n < 0) n += list.Count;
                return (n >= 0 && n < list.Count) ? list[n] : null;
            }
            return Get(cur, key);
        }

        public static double? Num(object o)
        {
            if (o == null || o is bool) return null;
            if (o is int) return (int)o;
            if (o is long) return (long)o;
            if (o is decimal) return (double)(decimal)o;
            if (o is double) return (double)o;
            if (o is float) return (float)o;
            var s = o as string;
            if (s != null)
            {
                double d;
                s = s.Trim().TrimEnd('%').Replace(",", "");
                if (s.StartsWith("$")) s = s.Substring(1);
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            }
            return null;
        }

        public static string Str(object o)
        {
            if (o == null) return null;
            var s = o as string;
            if (s != null) return s;
            if (o is bool) return ((bool)o) ? "true" : "false";
            var n = Num(o);
            if (n.HasValue) return n.Value.ToString(CultureInfo.InvariantCulture);
            return o.ToString();
        }

        public static bool? Bool(object o)
        {
            if (o is bool) return (bool)o;
            var s = o as string;
            if (s != null)
            {
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return null;
        }

        /// <summary>Depth-first search for the first value stored under <paramref name="key"/>.</summary>
        public static object FindKey(object root, string key)
        {
            return FindKey(root, key, 0);
        }

        static object FindKey(object root, string key, int depth)
        {
            if (root == null || depth > 64) return null;
            var d = root as Dictionary<string, object>;
            if (d != null)
            {
                object v;
                if (d.TryGetValue(key, out v) && v != null) return v;
                foreach (var kv in d)
                {
                    var r = FindKey(kv.Value, key, depth + 1);
                    if (r != null) return r;
                }
                return null;
            }
            var l = root as IList;
            if (l != null && !(root is string))
            {
                foreach (var item in l)
                {
                    var r = FindKey(item, key, depth + 1);
                    if (r != null) return r;
                }
            }
            return null;
        }

        /// <summary>ISO-8601 string, unix seconds or unix milliseconds → UTC.</summary>
        public static DateTime? Date(object o)
        {
            if (o == null) return null;
            var s = o as string;
            if (s != null)
            {
                s = s.Trim();
                if (s.Length == 0) return null;
                double num;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out num)) return FromUnix(num);
                DateTimeOffset dto;
                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
                    return dto.UtcDateTime;
                return null;
            }
            var n = Num(o);
            if (n.HasValue && n.Value > 0) return FromUnix(n.Value);
            return null;
        }

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime FromUnix(double v)
        {
            if (v > 1e12) return Epoch.AddMilliseconds(v);
            return Epoch.AddSeconds(v);
        }

        public static long ToUnixMs(DateTime utc)
        {
            return (long)(utc.ToUniversalTime() - Epoch).TotalMilliseconds;
        }

        // ---------------------------------------------------------------- writer

        public static string Serialize(object o, bool pretty)
        {
            var sb = new StringBuilder();
            Write(sb, o, pretty, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object o, bool pretty, int indent)
        {
            if (o == null) { sb.Append("null"); return; }
            var s = o as string;
            if (s != null) { WriteString(sb, s); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is DateTime) { WriteString(sb, ((DateTime)o).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)); return; }
            if (o is int || o is long || o is short || o is byte)
            {
                sb.Append(Convert.ToInt64(o).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (o is double || o is float || o is decimal)
            {
                double d = Convert.ToDouble(o, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = o as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (pretty) { sb.Append('\n'); sb.Append(' ', (indent + 1) * 2); }
                    WriteString(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(pretty ? ": " : ":");
                    Write(sb, kv.Value, pretty, indent + 1);
                }
                if (pretty && !first) { sb.Append('\n'); sb.Append(' ', indent * 2); }
                sb.Append('}');
                return;
            }
            var list = o as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (pretty) { sb.Append('\n'); sb.Append(' ', (indent + 1) * 2); }
                    Write(sb, item, pretty, indent + 1);
                }
                if (pretty && !first) { sb.Append('\n'); sb.Append(' ', indent * 2); }
                sb.Append(']');
                return;
            }
            WriteString(sb, o.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
