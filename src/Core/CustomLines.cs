using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentriPet
{
    /// <summary>
    /// Lines read from one lines.json, grouped by language ("zh-TW", "en"…, or "all" for every language) and then by event
    /// ("poke", "idleGreat", "warn"…). A line with a placeholder the event doesn't have is skipped, so a typo falls back to
    /// the built-in lines instead of showing "{nmae}". Problems are collected (in the current language) instead of thrown.
    /// </summary>
    class LineSet
    {
        readonly Dictionary<string, Dictionary<string, List<string>>> groups = new Dictionary<string, Dictionary<string, List<string>>>();   // language → event → lines

        /// <summary>"mix": these lines are said besides the built-in ones, not instead of them.</summary>
        public bool Mix;
        public readonly List<string> Problems = new List<string>();

        /// <summary>The lines for an event in the current language (then "all"); null when there are none.</summary>
        public List<string> For(string ev)
        {
            List<string> lines;
            Dictionary<string, List<string>> byEvent;
            if (groups.TryGetValue(L.Current, out byEvent) && byEvent.TryGetValue(ev, out lines) && lines.Count > 0) return lines;
            if (groups.TryGetValue(CustomLines.AllLanguages, out byEvent) && byEvent.TryGetValue(ev, out lines) && lines.Count > 0) return lines;
            return null;
        }

        /// <summary>How many lines can be said in the current language.</summary>
        public int Count
        {
            get { return CustomLines.Events.Keys.Sum(e => { var l = For(e); return l == null ? 0 : l.Count; }); }
        }

        static readonly Regex Placeholder = new Regex(@"\{([^{}\s]*)\}");

        /// <summary>The first placeholder in a line that the event doesn't have ("{nmae}"), or null.</summary>
        internal static string FirstUnknown(string line, string[] allowed)
        {
            foreach (Match m in Placeholder.Matches(line))
                if (Array.IndexOf(allowed, m.Groups[1].Value) < 0) return m.Value;
            return null;
        }

        static string Short(string s) { return s.Length > 24 ? s.Substring(0, 24) + "…" : s; }

        /// <summary>Reads a lines.json (null = no file: no lines). <paramref name="what"/> names the file in the log.</summary>
        public static LineSet Parse(string text, string what)
        {
            var set = new LineSet();
            if (text == null) return set;
            var p = set.Problems;
            string err;
            var root = Json.Obj(Json.TryParse(text, out err));
            if (root == null) p.Add(L.F("lines.json 讀不懂（{0}），先用內建的台詞", err ?? "?"));
            else
            {
                set.Mix = Json.Bool(Json.Get(root, "mix")) ?? false;
                string codes = string.Join(", ", L.Languages.Select(l => l.Code)) + ", " + CustomLines.AllLanguages;
                foreach (var kv in root)
                {
                    if (kv.Key == "version" || kv.Key == "mix" || kv.Key.StartsWith("_")) continue;
                    if (kv.Key != CustomLines.AllLanguages && !L.Languages.Any(l => l.Code == kv.Key))
                    {
                        p.Add(L.F("lines.json：沒有「{0}」這個語言（可以用 {1}）", kv.Key, codes));
                        continue;
                    }
                    var events = Json.Obj(kv.Value);
                    if (events == null) { p.Add(L.F("lines.json：「{0}」裡面應該是一組事件", kv.Key)); continue; }
                    var byEvent = new Dictionary<string, List<string>>();
                    foreach (var ev in events)
                    {
                        string[] allowed;
                        if (!CustomLines.Events.TryGetValue(ev.Key, out allowed))
                        {
                            p.Add(L.F("lines.json {0}：沒有「{1}」這種事件", kv.Key, ev.Key));
                            continue;
                        }
                        IEnumerable items = Json.Arr(ev.Value) ?? (IEnumerable)new[] { ev.Value };
                        var list = new List<string>();
                        foreach (var item in items)
                        {
                            string line = item as string;
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            string bad = FirstUnknown(line, allowed);
                            if (bad != null)
                            {
                                p.Add(L.F("lines.json {0} / {1}：「{2}」不能用在這裡，「{3}」這句先略過", kv.Key, ev.Key, bad, Short(line)));
                                continue;
                            }
                            list.Add(line.Trim());
                        }
                        if (list.Count > 0) byEvent[ev.Key] = list;
                    }
                    set.groups[kv.Key] = byEvent;
                }
            }
            if (p.Count > 0) Log.Warn(what + ": " + p.Count + " problem(s), e.g. " + p[0]);
            return set;
        }
    }

    /// <summary>
    /// The lines the pets say besides the built-in ones: the user's own (#25, lines.json in the settings folder, read again
    /// when it changes) and the ones the theme in use brings along (#28, lines.json in a custom theme's folder). For an
    /// event both are said; an event with lines of its own uses them instead of the built-in ones, unless every file that
    /// has lines for it says "mix": true. Format and examples: docs/custom-lines.md.
    /// </summary>
    static class CustomLines
    {
        static readonly string[] ProviderVars = { "name", "pct", "meter", "reset" };
        static string[] With(params string[] more) { return ProviderVars.Concat(more).ToArray(); }

        /// <summary>Every event and the placeholders its lines may use.</summary>
        public static readonly Dictionary<string, string[]> Events = new Dictionary<string, string[]>
        {
            { "greeting", new[] { "names" } },
            { "poke", With("plan", "second", "secondpct") },
            { "idleGreat", With() },
            { "idleGood", With() },
            { "idleWorried", With() },
            { "idleLow", With() },
            { "idleEmpty", With() },
            { "working", With() },
            { "stale", With() },
            { "warn", With() },
            { "critical", With() },
            { "reset", With() },
            { "runsOut", With("time") },
            { "useIt", With("quota", "left", "when", "upct") },
            { "done", new[] { "name", "project", "time" } },
            { "waiting", new[] { "name", "project" } },
            { "permission", new[] { "name", "project" } },
            { "weekSummary", new[] { "name", "quota", "used", "left" } },
        };

        public const string AllLanguages = "all";

        public static string FilePath { get { return Path.Combine(AppPaths.DataDir, "lines.json"); } }

        /// <summary>Test hook: the file's JSON instead of the file (null = read the file).</summary>
        internal static string Override;

        /// <summary>The lines of the theme in use (#28), set when the widget changes theme; null = none.</summary>
        public static LineSet Theme;

        static readonly object gate = new object();
        static LineSet user;
        static DateTime stamp, checkedAt;

        /// <summary>Forgets what was read (tests, or after writing the file).</summary>
        public static void Reload()
        {
            lock (gate) { user = null; checkedAt = DateTime.MinValue; }
        }

        /// <summary>What is wrong with the user's lines.json (empty when it is fine or absent), in the current language.</summary>
        public static List<string> Problems { get { return User.Problems; } }

        /// <summary>The user's "mix": their lines are said besides the built-in ones, not instead of them.</summary>
        public static bool Mix { get { return User.Mix; } }

        public static bool Exists { get { return Override != null || File.Exists(FilePath); } }

        /// <summary>How many of the user's lines can be said in the current language.</summary>
        public static int Count { get { return User.Count; } }

        /// <summary>The user's and the theme's lines for an event in the current language (then "all"); null when there are none.</summary>
        public static List<string> For(string ev)
        {
            var mine = User.For(ev);
            var theme = Theme;
            var theirs = theme == null ? null : theme.For(ev);
            if (theirs == null) return mine;
            if (mine == null) return theirs;
            return mine.Concat(theirs).ToList();
        }

        /// <summary>The built-in lines of an event are said too: every file that has lines for it says "mix".</summary>
        public static bool MixFor(string ev)
        {
            var mine = User;
            var theme = Theme;
            return (mine.For(ev) == null || mine.Mix) && (theme == null || theme.For(ev) == null || theme.Mix);
        }

        /// <summary>The user's lines.json, read again when it changed (checked at most every 2 seconds).</summary>
        static LineSet User
        {
            get
            {
                lock (gate)
                {
                    if (Override != null)
                    {
                        if (user == null) user = LineSet.Parse(Override, "lines.json");
                        return user;
                    }
                    var now = DateTime.UtcNow;
                    if (user != null && (now - checkedAt).TotalSeconds < 2) return user;
                    checkedAt = now;
                    DateTime written = DateTime.MinValue;
                    try { if (File.Exists(FilePath)) written = File.GetLastWriteTimeUtc(FilePath); } catch { }
                    if (user != null && written == stamp) return user;
                    stamp = written;
                    string text = null;
                    if (written != DateTime.MinValue)
                    {
                        try { text = File.ReadAllText(FilePath, Encoding.UTF8); }
                        catch (Exception ex) { Log.Warn("lines.json: " + ex.Message); }
                    }
                    user = LineSet.Parse(text, "lines.json");
                    return user;
                }
            }
        }

        /// <summary>The example written when the user opens the file for the first time (the resource SentriPet.lines.example.json).</summary>
        public static string Example
        {
            get
            {
                using (var s = typeof(CustomLines).Assembly.GetManifestResourceStream("SentriPet.lines.example.json"))
                    return s == null ? "{ \"version\": 1 }" : new StreamReader(s, Encoding.UTF8).ReadToEnd();
            }
        }

        /// <summary>lines.json, written from the example when there is none yet; returns its path.</summary>
        public static string EnsureFile()
        {
            if (!File.Exists(FilePath))
            {
                AppPaths.EnsureDataDirs();
                File.WriteAllText(FilePath, Example, new UTF8Encoding(false));
                Reload();
            }
            return FilePath;
        }
    }
}
