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
        /// <summary>
        /// Which replies count (#36). Every transcript line says where the session runs: the desktop app's Code tab
        /// writes "entrypoint":"claude-desktop", the terminal "cli", the SDK "sdk-cli", VS Code "claude-vscode". When
        /// the desktop app and the terminal are signed in to different accounts, the desktop app's replies belong to
        /// one and all the others to the other.
        /// </summary>
        public enum Origin { All, Desktop, Others }

        class FileState
        {
            public long Offset;
            public DateTime Written;
            public bool? Desktop;   // from the transcript's lines; null until one says
        }

        struct Event
        {
            public DateTime T;
            public double W;
            public bool Desktop;
        }

        static readonly TimeSpan Keep = TimeSpan.FromDays(ClaudeProvider.CalibrationDays);   // (the calibration looks as far back)
        readonly object gate = new object();
        readonly Dictionary<string, FileState> files = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Event> byKey = new Dictionary<string, Event>();   // message id|request id → reply
        readonly List<Event> keyless = new List<Event>();
        List<Event> events = new List<Event>();   // the counted ones, sorted by time, rebuilt when something changed
        bool dirty;
        Origin only;

        /// <summary>Which replies count (all by default). Changing it needs no new read.</summary>
        public Origin Only
        {
            get { lock (gate) return only; }
            set
            {
                lock (gate)
                {
                    if (only == value) return;
                    only = value;
                    Rebuild();
                }
            }
        }

        bool Counts(bool desktop) { return only == Origin.All || (only == Origin.Desktop) == desktop; }

        void Rebuild()
        {
            events = byKey.Values.Concat(keyless).Where(e => Counts(e.Desktop)).OrderBy(e => e.T).ToList();
            dirty = false;
        }

        /// <summary>A transcript of the counted origin was written to in the last <paramref name="seconds"/>: the AI is at work.</summary>
        public bool ActiveWithin(int seconds)
        {
            var since = DateTime.UtcNow.AddSeconds(-seconds);
            lock (gate) return files.Values.Any(f => f.Written >= since && Counts(f.Desktop == true));
        }

        /// <summary>Claude Code's transcript folder (honours CLAUDE_CONFIG_DIR).</summary>
        internal static string Root
        {
            get
            {
                string cfg = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                return Path.Combine(string.IsNullOrEmpty(cfg) ? Path.Combine(AppPaths.Home, ".claude") : cfg, "projects");
            }
        }

        readonly string root;

        /// <summary>The transcripts of the default account, or of another account's folder (#26).</summary>
        public ClaudeCodeUsage(string root = null) { this.root = root; }

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
            string root = this.root ?? Root;
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
                        st.Written = fi.LastWriteTimeUtc;
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
                    Rebuild();
                }
            }
        }

        static readonly byte[] AssistantMark = Encoding.ASCII.GetBytes("\"type\":\"assistant\"");

        /// <summary>
        /// Reads the complete lines appended since the last call, one line at a time: transcripts grow to tens of MB,
        /// and loading one whole made the process keep that much memory. A half-written last line is read next time.
        /// </summary>
        void ReadFrom(string path, FileState st, DateTime cutoff)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096))
            {
                long end = fs.Length;
                long pos = st.Offset;
                fs.Seek(pos, SeekOrigin.Begin);
                var buf = new byte[64 * 1024];
                var line = new MemoryStream();
                while (pos < end)
                {
                    int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, end - pos));
                    if (n <= 0) break;
                    int start = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (buf[i] != (byte)'\n') continue;
                        line.Write(buf, start, i - start);
                        Consume(line, st, cutoff);
                        line.SetLength(0);
                        start = i + 1;
                        st.Offset = pos + i + 1;   // everything up to here is done
                    }
                    line.Write(buf, start, n - start);
                    pos += n;
                }
            }
        }

        void Consume(MemoryStream line, FileState st, DateTime cutoff)
        {
            byte[] bytes = line.GetBuffer();
            int len = (int)line.Length;
            if (len < 20) return;
            bool assistant = IndexOf(bytes, len, AssistantMark) >= 0;
            // where the session runs (#36): a reply's own line says it (a session can be resumed elsewhere); other
            // lines only until the transcript's origin is known
            if ((assistant || st.Desktop == null) && IndexOf(bytes, len, EntryMark) >= 0) st.Desktop = IndexOf(bytes, len, DesktopMark) >= 0;
            bool desktop = st.Desktop == true;
            // only assistant replies carry usage: skip the rest (tool results, prompts) without decoding it
            if (!assistant) return;
            DateTime t;
            double w;
            string key;
            if (!TryParse(Encoding.UTF8.GetString(bytes, 0, len), out t, out w, out key) || t < cutoff) return;
            if (key == null)
            {
                keyless.Add(new Event { T = t, W = w, Desktop = desktop });
                dirty = true;
                return;
            }
            Event old;
            if (byKey.TryGetValue(key, out old))
            {
                // Claude Code writes one line per content block with the same message id; keep the largest
                if (w > old.W) { byKey[key] = new Event { T = old.T, W = w, Desktop = old.Desktop }; dirty = true; }
                return;
            }
            byKey[key] = new Event { T = t, W = w, Desktop = desktop };
            dirty = true;
        }

        static readonly byte[] EntryMark = Encoding.ASCII.GetBytes("\"entrypoint\":\"");
        static readonly byte[] DesktopMark = Encoding.ASCII.GetBytes("\"entrypoint\":\"claude-desktop\"");

        static int IndexOf(byte[] hay, int len, byte[] needle)
        {
            for (int i = 0, last = len - needle.Length; i <= last; i++)
            {
                if (hay[i] != needle[0]) continue;
                int j = 1;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
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
