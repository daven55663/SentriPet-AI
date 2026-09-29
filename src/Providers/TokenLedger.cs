using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>The tokens of one Claude Code reply or one Codex turn — numbers, model and folder name only, never content.</summary>
    class TokenEntry
    {
        public DateTime At;              // UTC
        public string Source;            // "claude" | "codex" (the provider id)
        public string Project;           // the name of the folder it worked in
        public string Model;
        public long Input, Output, CacheWrite, CacheRead;   // Input: not from the cache

        public long Total { get { return Input + Output + CacheWrite + CacheRead; } }
    }

    /// <summary>
    /// The token counts of the last month (#18), from Claude Code's transcripts (~/.claude/projects/**/*.jsonl) and
    /// Codex's session logs (~/.codex/sessions/**/rollout-*.jsonl), read incrementally line by line. The first read of a
    /// month of logs takes a few seconds, so the report window reads in the background.
    /// </summary>
    class TokenLedger
    {
        public const int Days = 31;

        class FileState
        {
            public long Offset;
            public string Project, Model;                   // Codex: from the session and the current turn
            public long[] Totals;                           // Codex: the session's running totals so far
        }

        readonly object gate = new object();
        readonly string claudeRoot, codexSessions;
        readonly Dictionary<string, FileState> files = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, TokenEntry> claudeByKey = new Dictionary<string, TokenEntry>();   // message id|request id
        readonly List<TokenEntry> others = new List<TokenEntry>();                                   // Codex turns, Claude replies without an id

        public TokenLedger(string claudeRoot, string codexSessions)
        {
            this.claudeRoot = claudeRoot;
            this.codexSessions = codexSessions;
        }

        /// <summary>Claude Code's and Codex's folders on this computer.</summary>
        public static TokenLedger ForThisComputer()
        {
            return new TokenLedger(ClaudeCodeUsage.Root, Path.Combine(AgentHooks.CodexHome, "sessions"));
        }

        /// <summary>Adds an entry (sample data for the snapshots and the self-test).</summary>
        internal void Add(TokenEntry e) { lock (gate) others.Add(e); }

        /// <summary>Everything recorded in the last month, oldest first.</summary>
        public List<TokenEntry> Entries()
        {
            lock (gate) return claudeByKey.Values.Concat(others).OrderBy(e => e.At).ToList();
        }

        /// <summary>Reads what was written since the last call.</summary>
        public void Update(DateTime nowUtc)
        {
            var cutoff = nowUtc.AddDays(-Days);
            lock (gate)
            {
                Scan(claudeRoot, "*.jsonl", cutoff, false);
                Scan(codexSessions, "rollout-*.jsonl", cutoff, true);
                foreach (var k in claudeByKey.Where(kv => kv.Value.At < cutoff).Select(kv => kv.Key).ToList()) claudeByKey.Remove(k);
                others.RemoveAll(e => e.At < cutoff);
            }
        }

        void Scan(string root, string pattern, DateTime cutoff, bool codex)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            foreach (var fi in new DirectoryInfo(root).EnumerateFiles(pattern, SearchOption.AllDirectories))
            {
                try
                {
                    if (fi.LastWriteTimeUtc < cutoff) continue;
                    FileState st;
                    if (!files.TryGetValue(fi.FullName, out st)) { st = new FileState(); files[fi.FullName] = st; }
                    if (fi.Length < st.Offset) { st.Offset = 0; st.Totals = null; }   // rewritten
                    if (fi.Length == st.Offset) continue;
                    Read(fi.FullName, st, cutoff, codex);
                }
                catch (Exception ex) { Log.Warn("token ledger " + fi.Name + ": " + ex.Message); }
            }
        }

        /// <summary>Complete lines appended since the last call, one at a time (a half-written last line waits).</summary>
        void Read(string path, FileState st, DateTime cutoff, bool codex)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096))
            {
                long end = fs.Length, pos = st.Offset;
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
                        if (codex) CodexLine(line, st, cutoff);
                        else ClaudeLine(line, cutoff);
                        line.SetLength(0);
                        start = i + 1;
                        st.Offset = pos + i + 1;
                    }
                    line.Write(buf, start, n - start);
                    pos += n;
                }
            }
        }

        static readonly byte[] AssistantMark = Encoding.ASCII.GetBytes("\"type\":\"assistant\"");
        static readonly byte[] TokenCountMark = Encoding.ASCII.GetBytes("\"token_count\"");
        static readonly byte[] TurnMark = Encoding.ASCII.GetBytes("\"turn_context\"");
        static readonly byte[] MetaMark = Encoding.ASCII.GetBytes("\"session_meta\"");

        static bool Has(MemoryStream line, byte[] mark)
        {
            byte[] hay = line.GetBuffer();
            int len = (int)line.Length;
            for (int i = 0, last = len - mark.Length; i <= last; i++)
            {
                if (hay[i] != mark[0]) continue;
                int j = 1;
                while (j < mark.Length && hay[i + j] == mark[j]) j++;
                if (j == mark.Length) return true;
            }
            return false;
        }

        static string Text(MemoryStream line) { return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length); }

        static DateTime? Time(string s)
        {
            DateTime t;
            if (s == null || !DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return null;
            return DateTime.SpecifyKind(t, DateTimeKind.Utc);
        }

        /// <summary>"D:\\work\\app" / "/home/me/app" → "app".</summary>
        internal static string ProjectName(string cwd)
        {
            if (string.IsNullOrEmpty(cwd)) return null;
            string c = cwd.Replace("\\\\", "\\").TrimEnd('\\', '/');
            int i = Math.Max(c.LastIndexOf('\\'), c.LastIndexOf('/'));
            string n = i >= 0 ? c.Substring(i + 1) : c;
            return n.Length == 0 ? null : n;
        }

        // ------------------------------------------------------------------ Claude Code

        void ClaudeLine(MemoryStream raw, DateTime cutoff)
        {
            if (raw.Length < 20 || !Has(raw, AssistantMark)) return;
            string line = Text(raw);
            int u = line.LastIndexOf("\"usage\":{", StringComparison.Ordinal);
            if (u < 0) return;
            string model = Field(line, "\"model\":\"", false);
            if (model == null || model == "<synthetic>") return;
            var at = Time(Field(line, "\"timestamp\":\"", true));
            if (at == null || at.Value < cutoff) return;
            var usage = Json.TryParse(ObjectAt(line, u + 8));
            if (usage == null) return;
            var e = new TokenEntry
            {
                At = at.Value, Source = "claude", Model = model, Project = ProjectName(Field(line, "\"cwd\":\"", false)),
                Input = (long)(Json.Num(Json.Get(usage, "input_tokens")) ?? 0),
                Output = (long)(Json.Num(Json.Get(usage, "output_tokens")) ?? 0),
                CacheWrite = (long)(Json.Num(Json.Get(usage, "cache_creation_input_tokens")) ?? 0),
                CacheRead = (long)(Json.Num(Json.Get(usage, "cache_read_input_tokens")) ?? 0),
            };
            if (e.Total <= 0) return;
            string id = Field(line, "\"id\":\"msg_", false);
            string req = Field(line, "\"requestId\":\"", true);
            if (id == null && req == null) { others.Add(e); return; }
            // one line per content block with the same message id: the largest counts
            string key = (id ?? "") + "|" + (req ?? "");
            TokenEntry old;
            if (!claudeByKey.TryGetValue(key, out old) || e.Total > old.Total) claudeByKey[key] = e;
        }

        static string Field(string line, string marker, bool last)
        {
            int i = last ? line.LastIndexOf(marker, StringComparison.Ordinal) : line.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return null;
            i += marker.Length;
            int j = i;
            while (j < line.Length && line[j] != '"') j += line[j] == '\\' ? 2 : 1;   // (escaped quotes and backslashes)
            return j <= line.Length ? line.Substring(i, Math.Min(j, line.Length) - i) : null;
        }

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

        // ------------------------------------------------------------------ Codex

        void CodexLine(MemoryStream raw, FileState st, DateTime cutoff)
        {
            bool tokens = Has(raw, TokenCountMark), turn = !tokens && Has(raw, TurnMark), meta = !tokens && !turn && Has(raw, MetaMark);
            if (!tokens && !turn && !meta) return;
            var o = Json.TryParse(Text(raw));
            var payload = Json.Get(o, "payload");
            if (meta || turn)
            {
                string cwd = Json.Str(Json.Get(payload, "cwd"));
                if (cwd != null) st.Project = ProjectName(cwd);
                if (turn) st.Model = Json.Str(Json.Get(payload, "model")) ?? st.Model;
                return;
            }
            if (Json.Str(Json.Get(payload, "type")) != "token_count") return;
            var total = Json.Get(Json.Get(payload, "info"), "total_token_usage");
            if (total == null) return;   // (rate limits only)
            var now = new[]
            {
                (long)(Json.Num(Json.Get(total, "input_tokens")) ?? 0),
                (long)(Json.Num(Json.Get(total, "cached_input_tokens")) ?? 0),
                (long)(Json.Num(Json.Get(total, "cache_write_input_tokens")) ?? 0),
                (long)(Json.Num(Json.Get(total, "output_tokens")) ?? 0),
            };
            var before = st.Totals ?? new long[4];
            st.Totals = now;
            long input = now[0] - before[0], cached = now[1] - before[1], written = now[2] - before[2], output = now[3] - before[3];
            if (input < 0 || cached < 0 || output < 0) return;   // (a new session in the same file: start over)
            var at = Time(Json.Str(Json.Get(o, "timestamp")));
            if (at == null || at.Value < cutoff || input + output <= 0) return;
            others.Add(new TokenEntry
            {
                At = at.Value, Source = "codex", Project = st.Project, Model = st.Model ?? "codex",
                Input = Math.Max(0, input - cached - Math.Max(0, written)), CacheRead = cached, CacheWrite = Math.Max(0, written), Output = output,
            });
        }

        // ------------------------------------------------------------------ summaries

        /// <summary>Total tokens per local day and source, for the last <paramref name="days"/> days up to today.</summary>
        public static List<KeyValuePair<DateTime, Dictionary<string, long>>> ByDay(IEnumerable<TokenEntry> entries, int days, DateTime todayLocal)
        {
            var first = todayLocal.Date.AddDays(1 - days);
            var map = new Dictionary<DateTime, Dictionary<string, long>>();
            for (int i = 0; i < days; i++) map[first.AddDays(i)] = new Dictionary<string, long>();
            foreach (var e in entries)
            {
                var d = e.At.ToLocalTime().Date;
                Dictionary<string, long> row;
                if (!map.TryGetValue(d, out row)) continue;
                long v;
                row.TryGetValue(e.Source, out v);
                row[e.Source] = v + e.Total;
            }
            return map.OrderBy(kv => kv.Key).ToList();
        }

        /// <summary>The busiest folders ("source|project" → tokens), most first.</summary>
        public static List<KeyValuePair<string, long>> ByProject(IEnumerable<TokenEntry> entries, int top)
        {
            return entries.GroupBy(e => e.Source + "|" + (e.Project ?? "?"))
                          .Select(g => new KeyValuePair<string, long>(g.Key, g.Sum(e => e.Total)))
                          .OrderByDescending(kv => kv.Value).Take(top).ToList();
        }

        /// <summary>Tokens per model, most first.</summary>
        public static List<KeyValuePair<string, long>> ByModel(IEnumerable<TokenEntry> entries)
        {
            return entries.GroupBy(e => e.Model ?? "?")
                          .Select(g => new KeyValuePair<string, long>(g.Key, g.Sum(e => e.Total)))
                          .OrderByDescending(kv => kv.Value).ToList();
        }
    }
}
