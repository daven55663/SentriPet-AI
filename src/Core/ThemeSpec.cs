using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>A box: the widget's background or a card's (colour and/or image, rounded, with a border).</summary>
    class BoxSpec
    {
        public string Color, Image, Border;
        public double Radius, BorderWidth, Padding;
    }

    /// <summary>One thing drawn on each AI's card.</summary>
    class ElementSpec
    {
        public string Type;                      // image, text, bar, ring, rect
        public double X, Y, Width, Height;
        public string When;                      // null = always; working, idle, data, nodata
        // image, text
        public Dictionary<string, string> States = new Dictionary<string, string>();   // a picture or text per state (great, good, worried, low, empty, unknown, working, default)
        // image
        public string Image;                     // one picture (or States)
        public string Animate;                   // none, bob, breathe
        // text
        public string Text, Align = "left", Font = "ui";   // Text: when States has nothing for the state
        public double Size = 12;
        public bool Bold;
        // bar, ring, rect, text
        public string Meter = "headline";        // headline, primary, secondary, or the meter's position (0, 1…)
        public string Color, Track, Fill;
        public double Radius, Thickness = 6;
    }

    /// <summary>
    /// A theme made by the user (#24): theme.json and pictures in a folder of the settings folder's "themes" directory.
    /// Only data — nothing is run. Problems are collected (in the current language) instead of thrown: a theme that
    /// can't be used says why, a broken element is left out. Format: docs/custom-themes.md.
    /// </summary>
    class ThemeSpec
    {
        public const int Version = 1;
        public const int MaxElements = 60;
        public static readonly string[] Types = { "image", "text", "bar", "ring", "rect" };
        public static readonly string[] StateNames = { "great", "good", "worried", "low", "empty", "unknown", "working" };
        public static readonly string[] Conditions = { "working", "idle", "data", "nodata" };
        public static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
        public static readonly string[] Placeholders = { "name", "pct", "used", "meter", "reset", "plan", "status" };

        public string Id, Folder, Name, Mood, Blurb;
        public string Layout = "row";            // row, column, grid
        public int Columns = 3;
        public double Gap = 8, CardWidth = 120, CardHeight = 150;
        public BoxSpec Background, Card;
        public readonly List<ElementSpec> Elements = new List<ElementSpec>();
        public readonly List<string> Problems = new List<string>();
        /// <summary>False when the theme can't be shown at all (Problems says why).</summary>
        public bool Usable;
        /// <summary>The theme's own lines (#28): lines.json in its folder, said while it is in use; null = none.</summary>
        public LineSet Lines;

        public static string ThemesDir { get { return Path.Combine(AppPaths.DataDir, "themes"); } }
        public const string LinesFile = "lines.json";

        /// <summary>Reads every theme folder (sorted by name). Folders without theme.json are ignored.</summary>
        public static List<ThemeSpec> LoadAll(string dir)
        {
            var list = new List<ThemeSpec>();
            try
            {
                if (!Directory.Exists(dir)) return list;
                foreach (var folder in Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    if (File.Exists(Path.Combine(folder, "theme.json"))) list.Add(Load(folder));
            }
            catch (Exception ex) { Log.Warn("themes: " + ex.Message); }
            return list;
        }

        public static ThemeSpec Load(string folder)
        {
            string text = null;
            string error = null;
            try { text = File.ReadAllText(Path.Combine(folder, "theme.json"), Encoding.UTF8); }
            catch (Exception ex) { error = ex.Message; }
            var s = Parse(text ?? "", Path.GetFileName(folder.TrimEnd('/', '\\')), folder);
            if (error != null)
            {
                s.Problems.Insert(0, L.F("讀不到 theme.json：{0}", error));
                s.Usable = false;
            }
            // its own lines (#28): problems are the theme's, the theme itself stays usable
            var lines = Path.Combine(folder, LinesFile);
            if (s.Usable && File.Exists(lines))
            {
                try
                {
                    s.Lines = LineSet.Parse(File.ReadAllText(lines, Encoding.UTF8), "themes/" + Path.GetFileName(folder.TrimEnd('/', '\\')) + "/" + LinesFile);
                    s.Problems.AddRange(s.Lines.Problems);
                }
                catch (Exception ex) { s.Problems.Add(L.F("讀不到 lines.json：{0}", ex.Message)); }
            }
            return s;
        }

        /// <summary>Parses theme.json (the folder is used to check the pictures exist; null skips that).</summary>
        public static ThemeSpec Parse(string json, string folderName, string folder)
        {
            var s = new ThemeSpec { Id = "custom:" + folderName, Folder = folder, Name = folderName };
            string err;
            var o = Json.Obj(Json.TryParse(json, out err));
            if (o == null)
            {
                s.Problems.Add(L.F("theme.json 讀不懂（{0}）", err ?? "?"));
                return s;
            }
            var v = Json.Num(Json.Get(o, "version"));
            if (v.HasValue && v.Value > Version) s.Problems.Add(L.F("這是給比較新的版本用的造型（格式版本 {0}），有些地方可能不會顯示", v.Value));
            s.Name = Localized(Json.Get(o, "name")) ?? folderName;
            s.Mood = Localized(Json.Get(o, "mood")) ?? "";
            s.Blurb = Localized(Json.Get(o, "blurb")) ?? "";

            string layout = Json.Str(Json.Get(o, "layout"));
            if (layout != null)
            {
                if (layout == "row" || layout == "column" || layout == "grid") s.Layout = layout;
                else s.Problems.Add(L.F("layout 只能是 row、column 或 grid，不是「{0}」", layout));
            }
            s.Columns = (int)Clamp(Json.Num(Json.Get(o, "columns")) ?? 3, 1, 8);
            s.Gap = Clamp(Json.Num(Json.Get(o, "gap")) ?? 8, 0, 100);
            s.Background = Box(Json.Obj(Json.Get(o, "background")), "background", s);

            var card = Json.Obj(Json.Get(o, "card"));
            if (card == null)
            {
                s.Problems.Add(L.T("缺少 card（每個 AI 一張卡片的版面）"));
                return s;
            }
            s.CardWidth = Clamp(Json.Num(Json.Get(card, "width")) ?? 120, 40, 600);
            s.CardHeight = Clamp(Json.Num(Json.Get(card, "height")) ?? 150, 40, 600);
            s.Card = Box(card, "card", s);
            var elements = Json.Arr(Json.Get(card, "elements"));
            if (elements == null || elements.Count == 0)
            {
                s.Problems.Add(L.T("card 裡沒有 elements（要畫的東西）"));
                return s;
            }
            int i = 0;
            foreach (var item in elements)
            {
                i++;
                if (s.Elements.Count >= MaxElements) { s.Problems.Add(L.F("元素太多了，只用前 {0} 個", MaxElements)); break; }
                var e = Element(Json.Obj(item), i, s);
                if (e != null) s.Elements.Add(e);
            }
            s.Usable = s.Elements.Count > 0;
            if (!s.Usable) s.Problems.Add(L.T("沒有可以畫的元素"));
            return s;
        }

        /// <summary>A text, or one per language ({"en": "…", "zh-TW": "…"}): the current language, then English, then any.</summary>
        static string Localized(object o, bool trim = true)
        {
            var s = o as string;
            if (s != null) return !trim ? s : s.Trim().Length > 0 ? s.Trim() : null;
            var d = Json.Obj(o);
            if (d == null || d.Count == 0) return null;
            object x;
            if (d.TryGetValue(L.Current, out x) && x is string) return (string)x;
            if (L.Current.StartsWith("zh") && d.TryGetValue(L.Current == "zh-TW" ? "zh-CN" : "zh-TW", out x) && x is string) return (string)x;
            if (d.TryGetValue("en", out x) && x is string) return (string)x;
            return d.Values.OfType<string>().FirstOrDefault();
        }

        static BoxSpec Box(Dictionary<string, object> o, string what, ThemeSpec s)
        {
            if (o == null) return null;
            var b = new BoxSpec
            {
                Color = ColorOf(Json.Get(o, "color"), what + ".color", s, false),
                Image = ImageOf(Json.Str(Json.Get(o, "image")), what + ".image", s),
                Border = ColorOf(Json.Get(o, "border"), what + ".border", s, false),
                Radius = Clamp(Json.Num(Json.Get(o, "radius")) ?? 0, 0, 300),
                BorderWidth = Clamp(Json.Num(Json.Get(o, "borderWidth")) ?? (Json.Get(o, "border") != null ? 1 : 0), 0, 20),
                Padding = Clamp(Json.Num(Json.Get(o, "padding")) ?? 0, 0, 200),
            };
            return b.Color == null && b.Image == null && b.Border == null && b.Padding == 0 ? null : b;
        }

        static ElementSpec Element(Dictionary<string, object> o, int n, ThemeSpec s)
        {
            string where = L.F("第 {0} 個元素", n);
            if (o == null) { s.Problems.Add(L.F("{0} 不是物件，略過", where)); return null; }
            string type = Json.Str(Json.Get(o, "type"));
            if (type == null || Array.IndexOf(Types, type) < 0)
            {
                s.Problems.Add(L.F("{0}：type 要是 {1}，不是「{2}」，略過", where, string.Join("、", Types), type ?? ""));
                return null;
            }
            where = L.F("第 {0} 個元素（{1}）", n, type);
            var e = new ElementSpec
            {
                Type = type,
                X = Clamp(Json.Num(Json.Get(o, "x")) ?? 0, -600, 1200),
                Y = Clamp(Json.Num(Json.Get(o, "y")) ?? 0, -600, 1200),
                Width = Clamp(Json.Num(Json.Get(o, "width")) ?? 0, 0, 1200),
                Height = Clamp(Json.Num(Json.Get(o, "height")) ?? 0, 0, 1200),
                Radius = Clamp(Json.Num(Json.Get(o, "radius")) ?? 0, 0, 300),
                Thickness = Clamp(Json.Num(Json.Get(o, "thickness")) ?? 6, 1, 100),
                Size = Clamp(Json.Num(Json.Get(o, "size")) ?? 12, 4, 120),
                Bold = Json.Bool(Json.Get(o, "bold")) ?? false,
                Meter = Json.Str(Json.Get(o, "meter")) ?? (Json.Num(Json.Get(o, "meter")).HasValue ? ((int)Json.Num(Json.Get(o, "meter")).Value).ToString(CultureInfo.InvariantCulture) : "headline"),
            };
            string when = Json.Str(Json.Get(o, "when"));
            if (when != null)
            {
                if (Array.IndexOf(Conditions, when) >= 0) e.When = when;
                else s.Problems.Add(L.F("{0}：when 只能是 {1}，不是「{2}」（先當成一直顯示）", where, string.Join("、", Conditions), when));
            }
            int dummy;
            if (e.Meter != "headline" && e.Meter != "primary" && e.Meter != "secondary" && !int.TryParse(e.Meter, out dummy))
            {
                s.Problems.Add(L.F("{0}：meter 要是 headline、primary、secondary 或數字，不是「{1}」", where, e.Meter));
                e.Meter = "headline";
            }
            switch (type)
            {
                case "image":
                    e.Image = ImageOf(Json.Str(Json.Get(o, "image")), where, s);
                    var states = Json.Obj(Json.Get(o, "states"));
                    if (states != null)
                        foreach (var kv in states)
                        {
                            if (Array.IndexOf(StateNames, kv.Key) < 0 && kv.Key != "default")
                            {
                                s.Problems.Add(L.F("{0}：沒有「{1}」這種狀態（可以用 {2}）", where, kv.Key, string.Join("、", StateNames)));
                                continue;
                            }
                            var file = ImageOf(Json.Str(kv.Value), where, s);
                            if (file != null) e.States[kv.Key] = file;
                        }
                    if (e.Image == null && e.States.Count == 0) { s.Problems.Add(L.F("{0}：沒有可以用的圖片，略過", where)); return null; }
                    if (e.Width <= 0 || e.Height <= 0) { s.Problems.Add(L.F("{0}：要寫 width 和 height，略過", where)); return null; }
                    e.Animate = Json.Str(Json.Get(o, "animate"));
                    if (e.Animate != null && e.Animate != "none" && e.Animate != "bob" && e.Animate != "breathe")
                    {
                        s.Problems.Add(L.F("{0}：animate 只能是 none、bob 或 breathe", where));
                        e.Animate = null;
                    }
                    break;
                case "text":
                    // one text, or one per state (#28); each can also be one per language
                    e.Text = Localized(Json.Get(o, "text"), false);
                    var texts = Json.Obj(Json.Get(o, "states"));
                    if (texts != null)
                        foreach (var kv in texts)
                        {
                            if (Array.IndexOf(StateNames, kv.Key) < 0 && kv.Key != "default")
                            {
                                s.Problems.Add(L.F("{0}：沒有「{1}」這種狀態（可以用 {2}）", where, kv.Key, string.Join("、", StateNames)));
                                continue;
                            }
                            var x = Localized(kv.Value, false);
                            if (x != null) e.States[kv.Key] = x;
                        }
                    if (e.Text == null && e.States.Count == 0) { s.Problems.Add(L.F("{0}：沒有 text，略過", where)); return null; }
                    foreach (var words in new[] { e.Text }.Concat(e.States.Values).Where(x => x != null))
                        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(words, @"\{([^{}\s]*)\}"))
                            if (Array.IndexOf(Placeholders, m.Groups[1].Value) < 0)
                                s.Problems.Add(L.F("{0}：不認得 {1}（可以用 {2}）", where, m.Value, string.Join(" ", Placeholders.Select(p => "{" + p + "}"))));
                    e.Align = Json.Str(Json.Get(o, "align")) ?? "left";
                    if (e.Align != "left" && e.Align != "center" && e.Align != "right") e.Align = "left";
                    e.Font = Json.Str(Json.Get(o, "font")) ?? "ui";
                    if (e.Font != "ui" && e.Font != "number" && e.Font != "mono") e.Font = "ui";
                    e.Color = ColorOf(Json.Get(o, "color"), where, s, true) ?? "#FFFFFF";
                    break;
                case "bar":
                case "ring":
                    if (type == "bar" && (e.Width <= 0 || e.Height <= 0)) { s.Problems.Add(L.F("{0}：要寫 width 和 height，略過", where)); return null; }
                    if (type == "ring" && e.Width <= 0) { s.Problems.Add(L.F("{0}：要寫 width（圓環的直徑），略過", where)); return null; }
                    e.Track = ColorOf(Json.Get(o, "track"), where, s, true) ?? "#33FFFFFF";
                    e.Fill = ColorOf(Json.Get(o, "fill"), where, s, true) ?? "level";
                    break;
                case "rect":
                    if (e.Width <= 0 || e.Height <= 0) { s.Problems.Add(L.F("{0}：要寫 width 和 height，略過", where)); return null; }
                    e.Color = ColorOf(Json.Get(o, "color"), where, s, true) ?? "#33FFFFFF";
                    break;
            }
            return e;
        }

        /// <summary>A colour: #RGB, #RRGGBB, #AARRGGBB, or (where allowed) level / provider / provider-light / provider-dark.</summary>
        static string ColorOf(object o, string where, ThemeSpec s, bool dynamic)
        {
            string c = Json.Str(o);
            if (c == null) return null;
            if (dynamic && (c == "level" || c == "provider" || c == "provider-light" || c == "provider-dark")) return c;
            Rgba parsed;
            if (c.StartsWith("#") && Rgba.TryParse(c, out parsed)) return c;
            s.Problems.Add(L.F("{0}：看不懂顏色「{1}」", where, c));
            return null;
        }

        /// <summary>A picture file inside the theme's folder (no "..", no absolute paths, a picture type, not too big).</summary>
        static string ImageOf(string file, string where, ThemeSpec s)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            string f = file.Replace('\\', '/').Trim();
            if (f.StartsWith("/") || f.Contains(":") || f.Split('/').Any(p => p == ".."))
            {
                s.Problems.Add(L.F("{0}：圖片要放在造型的資料夾裡（{1}）", where, file));
                return null;
            }
            if (Array.IndexOf(ImageTypes, Path.GetExtension(f).ToLowerInvariant()) < 0)
            {
                s.Problems.Add(L.F("{0}：{1} 不是 PNG、JPG、WebP 或 BMP 圖片", where, file));
                return null;
            }
            if (s.Folder != null)
            {
                var full = Path.Combine(s.Folder, f);
                if (!File.Exists(full)) { s.Problems.Add(L.F("{0}：找不到 {1}", where, file)); return null; }
                if (new FileInfo(full).Length > 8 * 1024 * 1024) { s.Problems.Add(L.F("{0}：{1} 太大了（超過 8 MB）", where, file)); return null; }
            }
            return f;
        }

        static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }

        /// <summary>The state picture for a pet: working (when it has one), its mood, then "default", then any.</summary>
        public static string StateOf(ProviderView v)
        {
            if (!v.HasData) return "unknown";
            switch (v.Mood)
            {
                case SentriPet.Mood.Great: return "great";
                case SentriPet.Mood.Good: return "good";
                case SentriPet.Mood.Worried: return "worried";
                case SentriPet.Mood.Critical: return "low";
                case SentriPet.Mood.Empty: return "empty";
                default: return "unknown";
            }
        }

        /// <summary>An element's picture or text for a pet's state: working (when it has one), its mood, then "default".</summary>
        static string ForState(ElementSpec e, ProviderView v, string otherwise)
        {
            string p;
            if (v.HasData && v.Active && e.States.TryGetValue("working", out p)) return p;
            if (e.States.TryGetValue(StateOf(v), out p)) return p;
            if (e.States.TryGetValue("default", out p)) return p;
            return otherwise;
        }

        public static string PictureFor(ElementSpec e, ProviderView v)
        {
            return ForState(e, v, e.Image ?? e.States.Values.FirstOrDefault());
        }

        /// <summary>The meter an element shows.</summary>
        public static Meter MeterFor(ElementSpec e, ProviderView v)
        {
            switch (e.Meter)
            {
                case "headline": return v.Headline ?? v.Primary;
                case "primary": return v.Primary;
                case "secondary": return v.Secondary;
            }
            int i;
            return int.TryParse(e.Meter, out i) && i >= 0 && i < v.Meters.Count ? v.Meters[i] : null;
        }

        /// <summary>A text element's words for a pet.</summary>
        public static string TextFor(ElementSpec e, ProviderView v)
        {
            var m = MeterFor(e, v);
            var reset = m != null && m.ResetsAt.HasValue ? m : v.ResetMeter;
            string s = ForState(e, v, e.Text ?? "")
                .Replace("{name}", v.Name ?? "")
                .Replace("{pct}", !v.HasData ? "?" : m == null ? "" : m.Unlimited ? "∞" : Fmt.Pct(m.Remaining))
                .Replace("{used}", !v.HasData || m == null || m.Unlimited ? "" : Fmt.Pct(m.Used))
                .Replace("{meter}", m == null ? "" : m.Label ?? "")
                .Replace("{reset}", reset == null || !reset.ResetsAt.HasValue ? "" : Fmt.Countdown(reset.ResetsAt))
                .Replace("{plan}", v.Plan ?? "")
                .Replace("{status}", v.HasData ? (v.StatusText ?? "") : (v.Error ?? ""));
            return L.Finish(s);
        }

        public static bool Shown(ElementSpec e, ProviderView v)
        {
            switch (e.When)
            {
                case "working": return v.HasData && v.Active;
                case "idle": return !(v.HasData && v.Active);
                case "data": return v.HasData;
                case "nodata": return !v.HasData;
                default: return true;
            }
        }
    }
}
