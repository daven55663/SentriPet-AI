using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>
    /// Token usage recorded in Claude Code's local transcripts (~/.claude/projects/**/*.jsonl), read incrementally.
    /// Only the timestamp, model name and "usage" numbers of assistant replies are read — never message content.
    /// Used to extrapolate the Claude plan usage between the desktop app's ~15-minute samples.
    /// </summary>
    class ClaudeCodeUsage
    {
        class FileState
        {
            public long Offset;
        }

        struct Event
        {
            public DateTime T;
            public double W;
        }

        static readonly TimeSpan Keep = TimeSpan.FromDays(3);
        readonly object gate = new object();
        readonly Dictionary<string, FileState> files = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Event> byKey = new Dictionary<string, Event>();   // message id|request id → reply
        readonly List<Event> keyless = new List<Event>();
        List<Event> events = new List<Event>();   // sorted by time, rebuilt when something changed
        bool dirty;

        static string Root
        {
            get
            {
                string cfg = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                return Path.Combine(string.IsNullOrEmpty(cfg) ? Path.Combine(AppPaths.Home, ".claude") : cfg, "projects");
            }
        }

        public int Count { get { lock (gate) return events.Count; } }

        /// <summary>
        /// Plan-usage weight of one reply: price-like ratios (output 5×, cache writes 1.25×) with cache reads at 2%.
        /// Fitted against the desktop app's own 5-hour percentages: mean error ≈ 0.7 percentage points.
        /// </summary>
        public static double Weight(string model, double input, double output, double cacheWrite, double cacheRead)
        {
            string m = (model ?? "").ToLowerInvariant();
            double mult = m.Contains("haiku") ? 0.2 : m.Contains("sonnet") ? 0.6 : 1.0;
            return mult * (input + 5 * output + 1.25 * cacheWrite + 0.02 * cacheRead);
        }

        /// <summary>Reads what was appended since the last call. Cheap when nothing changed.</summary>
        public void Update()
        {
            string root = Root;
            if (!Directory.Exists(root)) return;
            var cutoff = DateTime.UtcNow - Keep;
            lock (gate)
            {
                foreach (var fi in new DirectoryInfo(root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (fi.LastWriteTimeUtc < cutoff) continue;
                        FileState st;
                        if (!files.TryGetValue(fi.FullName, out st)) { st = new FileState(); files[fi.FullName] = st; }
                        fi.Refresh();
                        if (fi.Length < st.Offset) st.Offset = 0;   // rewritten
                        if (fi.Length == st.Offset) continue;
                        ReadFrom(fi.FullName, st, cutoff);
                    }
                    catch (Exception ex) { Log.Warn("claude transcript " + fi.Name + ": " + ex.Message); }
                }
                if (dirty)
                {
                    foreach (var k in byKey.Where(kv => kv.Value.T < cutoff).Select(kv => kv.Key).ToList()) byKey.Remove(k);
                    keyless.RemoveAll(e => e.T < cutoff);
                    events = byKey.Values.Concat(keyless).OrderBy(e => e.T).ToList();
                    dirty = false;
                }
            }
        }

        void ReadFrom(string path, FileState st, DateTime cutoff)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(st.Offset, SeekOrigin.Begin);
                var buf = new byte[fs.Length - st.Offset];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                // only complete lines; a half-written last line is read next time
                int end = Array.LastIndexOf(buf, (byte)'\n', Math.Max(0, read - 1));
                if (end < 0) return;
                string text = Encoding.UTF8.GetString(buf, 0, end + 1);
                st.Offset += end + 1;
                foreach (var line in text.Split('\n'))
                {
                    DateTime t;
                    double w;
                    string key;
                    if (!TryParse(line, out t, out w, out key) || t < cutoff) continue;
                    if (key == null)
                    {
                        keyless.Add(new Event { T = t, W = w });
                        dirty = true;
                        continue;
                    }
                    Event old;
                    if (byKey.TryGetValue(key, out old))
                    {
                        // Claude Code writes one line per content block with the same message id; keep the largest
                        if (w > old.W) { byKey[key] = new Event { T = old.T, W = w }; dirty = true; }
                        continue;
                    }
                    byKey[key] = new Event { T = t, W = w };
                    dirty = true;
                }
            }
        }

        static bool TryParse(string line, out DateTime t, out double w, out string key)
        {
            t = DateTime.MinValue;
            w = 0;
            key = null;
            if (line.Length < 20 || line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0) return false;
            int u = line.LastIndexOf("\"usage\":{", StringComparison.Ordinal);
            if (u < 0) return false;
            string model = Field(line, "\"model\":\"", false);
            if (model == "<synthetic>") return false;
            string ts = Field(line, "\"timestamp\":\"", true);
            if (ts == null || !DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return false;
            string usageJson = ObjectAt(line, u + 8);
            var usage = Json.TryParse(usageJson);
            if (usage == null) return false;
            double input = Json.Num(Json.Get(usage, "input_tokens")) ?? 0;
            double output = Json.Num(Json.Get(usage, "output_tokens")) ?? 0;
            double cw = Json.Num(Json.Get(usage, "cache_creation_input_tokens")) ?? 0;
            double cr = Json.Num(Json.Get(usage, "cache_read_input_tokens")) ?? 0;
            w = Weight(model, input, output, cw, cr);
            if (w <= 0) return false;
            string id = Field(line, "\"id\":\"msg_", false);
            string req = Field(line, "\"requestId\":\"", true);
            if (id != null || req != null) key = (id ?? "") + "|" + (req ?? "");
            return true;
        }

        /// <summary>Value of the first (or last) occurrence of a simple string field.</summary>
        static string Field(string line, string marker, bool last)
        {
            int i = last ? line.LastIndexOf(marker, StringComparison.Ordinal) : line.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return null;
            i += marker.Length;
            int j = line.IndexOf('"', i);
            return j < 0 ? null : line.Substring(i, j - i);
        }

        /// <summary>The JSON object starting at <paramref name="start"/> (brace matching, strings skipped).</summary>
        static string ObjectAt(string s, int start)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') inStr = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return s.Substring(start, i - start + 1);
            }
            return null;
        }

        /// <summary>Total weight of replies with from &lt; T ≤ to (UTC).</summary>
        public double Sum(DateTime fromUtc, DateTime toUtc)
        {
            lock (gate)
            {
                double s = 0;
                foreach (var e in events)
                    if (e.T > fromUtc && e.T <= toUtc) s += e.W;
                return s;
            }
        }

        /// <summary>Time of the first reply after <paramref name="utc"/> (and not later than <paramref name="notAfter"/>).</summary>
        public DateTime? FirstAfter(DateTime utc, DateTime notAfter)
        {
            lock (gate)
            {
                foreach (var e in events)
                    if (e.T > utc && e.T <= notAfter) return e.T;
                return null;
            }
        }
    }
}
