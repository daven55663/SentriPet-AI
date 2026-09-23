using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>
    /// A provider described by a JSON file in %APPDATA%\SentriPet\providers\*.json.
    /// Sources: "file" (read a JSON file), "command" (run a program that prints JSON), "http" (GET/POST a URL).
    /// The JSON can already be in the normalized form { "meters": [ { "label", "used", "resetsAt" } ] }
    /// or be mapped with "meters" / "metersFrom" rules (see examples\providers\README.md).
    /// </summary>
    class CustomProvider : Provider
    {
        readonly Dictionary<string, object> def;
        readonly int interval;

        public override int IntervalSeconds { get { return interval; } }

        CustomProvider(Dictionary<string, object> d, string file)
        {
            def = d;
            Origin = file;
            BuiltIn = false;
            Id = Json.Str(Json.Get(d, "id")) ?? Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            Name = Json.Str(Json.Get(d, "name")) ?? Id;
            Mascot = Json.Str(Json.Get(d, "mascot")) ?? "antenna";
            string color = Json.Str(Json.Get(d, "color"));
            Color = color != null ? Palette.Hex(color) : Palette.FromId(Id);
            interval = (int)Math.Max(10, Json.Num(Json.Get(d, "intervalSeconds")) ?? 120);
        }

        public static List<CustomProvider> LoadAll()
        {
            return LoadFrom(AppPaths.ProvidersDir);
        }

        internal static List<CustomProvider> LoadFrom(string dir)
        {
            var list = new List<CustomProvider>();
            try
            {
                if (!Directory.Exists(dir)) return list;
                foreach (var f in Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        var d = Json.Obj(Json.Parse(File.ReadAllText(f, Encoding.UTF8)));
                        if (d == null) continue;
                        if (Json.Bool(Json.Get(d, "enabled")) == false) continue;
                        list.Add(new CustomProvider(d, f));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("plugin " + Path.GetFileName(f) + " ignored: " + ex.Message);
                    }
                }
            }
            catch (Exception ex) { Log.Error("plugin load", ex); }
            return list;
        }

        public override Detection Detect()
        {
            var d = new Detection();
            var det = Json.Get(def, "detect");
            if (det == null)
            {
                d.Installed = true;
                d.Evidence.Add("外掛 " + Path.GetFileName(Origin));
                return d;
            }
            foreach (var p in Strings(Json.Get(det, "paths")))
                if (AppPaths.Glob(p).Count > 0) { d.Evidence.Add("找到 " + AppPaths.ShortPath(AppPaths.Expand(p))); break; }
            foreach (var c in Strings(Json.Get(det, "commands")))
                if (AppPaths.Which(c) != null) { d.Evidence.Add(c + " 指令"); break; }
            foreach (var e in Strings(Json.Get(det, "env")))
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(e))) { d.Evidence.Add("環境變數 " + e); break; }
            foreach (var x in Strings(Json.Get(det, "editorExtensions")))
                if (AppPaths.EditorExtensions(x).Count > 0) { d.Evidence.Add("編輯器擴充"); break; }
            if (Json.Bool(Json.Get(det, "always")) == true) d.Evidence.Add("外掛 " + Path.GetFileName(Origin));
            d.Installed = d.Evidence.Count > 0;
            return d;
        }

        static IEnumerable<string> Strings(object o)
        {
            var s = o as string;
            if (s != null) { yield return s; yield break; }
            var l = o as IList;
            if (l == null) yield break;
            foreach (var x in l) { var t = Json.Str(x); if (t != null) yield return t; }
        }

        public override Snapshot Fetch(bool force, AppSettings settings)
        {
            var src = Json.Get(def, "source");
            string type = (Json.Str(Json.Get(src, "type")) ?? "file").ToLowerInvariant();
            object root;
            DateTime observed = DateTime.UtcNow;
            string sourceText;
            try
            {
                if (type == "file")
                {
                    string path = AppPaths.Glob(Json.Str(Json.Get(src, "path")) ?? "")
                        .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    if (path == null) return Snapshot.Fail("找不到檔案 " + Json.Str(Json.Get(src, "path")));
                    root = Json.Parse(AppPaths.ReadShared(path));
                    observed = File.GetLastWriteTimeUtc(path);
                    sourceText = "檔案 " + Path.GetFileName(path);
                }
                else if (type == "command")
                {
                    string cmd = AppPaths.Expand(Json.Str(Json.Get(src, "command")) ?? "");
                    var args = Strings(Json.Get(src, "args")).Select(AppPaths.ExpandArg).ToList();
                    bool shell = Json.Bool(Json.Get(src, "shell")) ?? false;
                    int timeout = (int)((Json.Num(Json.Get(src, "timeoutSeconds")) ?? 20) * 1000);
                    int code;
                    string err;
                    string output = Net.RunCommand(cmd, args, shell, timeout, out code, out err);
                    if (code != 0 && string.IsNullOrWhiteSpace(output))
                        return Snapshot.Fail("指令失敗 (" + code + ")：" + FirstLine(err));
                    root = Json.Parse(ExtractJson(output));
                    sourceText = "指令 " + Path.GetFileName(cmd);
                }
                else if (type == "http")
                {
                    string url = AppPaths.ExpandVars(Json.Str(Json.Get(src, "url")) ?? "");
                    var headers = new Dictionary<string, string>();
                    var h = Json.Obj(Json.Get(src, "headers"));
                    if (h != null) foreach (var kv in h) headers[kv.Key] = AppPaths.ExpandVars(Json.Str(kv.Value) ?? "");
                    string body = Json.Get(src, "body") is string ? AppPaths.ExpandVars((string)Json.Get(src, "body")) :
                                  Json.Get(src, "body") != null ? Json.Serialize(Json.Get(src, "body"), false) : null;
                    int status;
                    string text = Net.Request(Json.Str(Json.Get(src, "method")) ?? "GET", url, headers, body, 15000, out status);
                    if (status >= 400) return Snapshot.Fail("HTTP " + status);
                    root = Json.Parse(text);
                    sourceText = new Uri(url).Host;
                }
                else return Snapshot.Fail("不支援的 source.type：" + type);
            }
            catch (Exception ex)
            {
                return Snapshot.Fail(ex.Message);
            }

            var snap = new Snapshot { ObservedAt = observed, Source = Json.Str(Json.Get(def, "sourceLabel")) ?? sourceText };
            snap.Plan = Json.Str(Resolve(root, Json.Get(def, "plan")));
            snap.Note = Json.Str(Resolve(root, Json.Get(def, "note")));
            var obs = Json.Get(def, "observedAt");
            if (obs != null) snap.ObservedAt = Json.Date(Resolve(root, obs)) ?? snap.ObservedAt;

            var rules = Json.Arr(Json.Get(def, "meters"));
            var from = Json.Get(def, "metersFrom");
            if (rules != null)
            {
                foreach (var r in rules)
                {
                    var m = MapMeter(root, r, null);
                    if (m != null) snap.Meters.Add(m);
                }
            }
            else if (from != null)
            {
                MapMetersFrom(root, from, snap.Meters);
            }
            else
            {
                // normalized output: { plan, note, meters: [ { label, used | remaining, resetsAt, windowMinutes, valueText } ] }
                if (snap.Plan == null) snap.Plan = Json.Str(Json.Get(root, "plan"));
                if (snap.Note == null) snap.Note = Json.Str(Json.Get(root, "note"));
                var arr = Json.Arr(Json.Get(root, "meters"));
                if (arr != null)
                    foreach (var item in arr)
                    {
                        var m = MapMeter(item, item, null);
                        if (m != null) snap.Meters.Add(m);
                    }
            }
            if (snap.Meters.Count == 0) return Snapshot.Fail("外掛沒有產生任何用量資料");
            return snap;
        }

        /// <summary>Strings starting with "$" are paths into the data; anything else is a literal.</summary>
        static object Resolve(object root, object spec)
        {
            var s = spec as string;
            if (s != null && s.StartsWith("$")) return Json.Path(root, s);
            return spec;
        }

        static Meter MapMeter(object root, object rule, string keyHint)
        {
            // when a rule field is a plain string inside "normalized" data, treat it as the value itself
            Func<string, object> get = name =>
            {
                var v = Json.Get(rule, name);
                return root == rule ? v : Resolve(root, v);
            };
            double scale = Json.Num(Json.Get(rule, "scale")) ?? 1;
            double? used = Json.Num(get("used"));
            double? remaining = Json.Num(get("remaining"));
            double? total = Json.Num(get("total"));
            bool unlimited = Json.Bool(get("unlimited")) ?? false;
            if (used.HasValue) used *= scale;
            if (remaining.HasValue) remaining *= scale;

            double? usedPct = null;
            if (total.HasValue && total.Value > 0)
            {
                if (used.HasValue) usedPct = used.Value / total.Value * 100;
                else if (remaining.HasValue) usedPct = (total.Value - remaining.Value) / total.Value * 100;
            }
            else
            {
                if (used.HasValue) usedPct = used.Value;
                else if (remaining.HasValue) usedPct = 100 - remaining.Value;
            }
            if (!usedPct.HasValue && !unlimited) return null;

            int win = (int)(Json.Num(Json.Get(rule, "windowMinutes")) ?? 0);
            var m = new Meter
            {
                Key = Json.Str(Json.Get(rule, "key")) ?? keyHint ?? Json.Str(Json.Get(rule, "label")) ?? "q",
                Label = Json.Str(Json.Get(rule, "label")) ?? keyHint ?? Fmt.WindowLabel(win),
                Used = Math.Max(0, Math.Min(100, usedPct ?? 0)),
                ResetsAt = Json.Date(get("resetsAt")),
                WindowMinutes = win,
                Unlimited = unlimited,
            };
            m.ShortLabel = Json.Str(Json.Get(rule, "shortLabel")) ?? (win > 0 ? Fmt.WindowShort(win) : (m.Label.Length > 2 ? m.Label.Substring(0, 2) : m.Label));
            string vt = Json.Str(Json.Get(rule, "valueText"));
            if (vt != null)
            {
                m.ValueText = vt.Replace("{used}", N(used)).Replace("{remaining}", N(remaining)).Replace("{total}", N(total))
                                .Replace("{percent}", Math.Round(m.Used).ToString(CultureInfo.InvariantCulture));
            }
            if (unlimited && m.ValueText == null) m.ValueText = "∞";
            return m;
        }

        static void MapMetersFrom(object root, object from, List<Meter> output)
        {
            var container = Json.Path(root, Json.Str(Json.Get(from, "path")) ?? "$");
            var labels = Json.Obj(Json.Get(from, "labels"));
            var windows = Json.Obj(Json.Get(from, "windowMinutes"));
            var items = new List<KeyValuePair<string, object>>();
            var dict = Json.Obj(container);
            if (dict != null) items.AddRange(dict);
            else
            {
                var arr = Json.Arr(container);
                if (arr != null)
                {
                    string keyField = Json.Str(Json.Get(from, "keyField")) ?? "name";
                    int i = 0;
                    foreach (var x in arr) items.Add(new KeyValuePair<string, object>(Json.Str(Json.Get(x, keyField)) ?? ("#" + (i++)), x));
                }
            }
            foreach (var kv in items)
            {
                if (!(kv.Value is Dictionary<string, object>)) continue;
                var rule = new Dictionary<string, object>();
                foreach (var field in new[] { "used", "remaining", "total", "resetsAt", "unlimited" })
                {
                    string path = Json.Str(Json.Get(from, field));
                    if (path != null) rule[field] = "$." + path;
                }
                foreach (var field in new[] { "scale", "valueText" })
                    if (Json.Get(from, field) != null) rule[field] = Json.Get(from, field);
                if (labels != null && labels.ContainsKey(kv.Key)) rule["label"] = labels[kv.Key];
                else if (Json.Bool(Json.Get(from, "onlyLabeled")) == true) continue;
                if (windows != null && windows.ContainsKey(kv.Key)) rule["windowMinutes"] = windows[kv.Key];
                var m = MapMeter(kv.Value, rule, kv.Key);
                if (m != null) output.Add(m);
            }
        }

        static string N(double? v)
        {
            if (!v.HasValue) return "?";
            return Math.Round(v.Value, 2).ToString(CultureInfo.InvariantCulture);
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var l = s.Trim().Split('\n')[0].Trim();
            return l.Length > 80 ? l.Substring(0, 80) + "…" : l;
        }

        /// <summary>Takes the JSON part of a program's output (skips log lines before it).</summary>
        static string ExtractJson(string output)
        {
            int a = output.IndexOf('{');
            int b = output.LastIndexOf('}');
            if (a >= 0 && b > a) return output.Substring(a, b - a + 1);
            return output;
        }
    }
}
