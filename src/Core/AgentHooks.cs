using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentriPet
{
    /// <summary>One "done / waiting for you" moment of an AI tool (#14).</summary>
    class AgentEvent
    {
        public string Source;          // "claude" or "codex" (the provider id)
        public string Kind;            // Done, Permission, Waiting
        public DateTime At;            // UTC
        public double Seconds = -1;    // how long the turn took (done), -1 = unknown
        public string Project;         // the folder it worked in (its name only)

        public const string Done = "done", Permission = "permission", Waiting = "waiting";
    }

    /// <summary>
    /// "Done / waiting for you" (#14). When the user turns it on, SentriPet adds itself to Claude Code's hooks (Stop, and
    /// Notification for permission and idle prompts) and as Codex's notify program. The hook (<c>SentriPet --hook
    /// claude|codex</c>) appends a line to an events file next to the tool's own settings — not under AppData, see
    /// <see cref="ClaudeStatusLine.DataFile"/> — and the widget reads the new lines. Both edits keep a backup and
    /// everything the user had: their own hooks stay, and a notify program they had is still run (with the same input).
    /// </summary>
    static class AgentHooks
    {
        /// <summary>A reply this quick is not worth a "done" (a chat, not a task).</summary>
        public const double LongTurnSeconds = 30;
        const string NotificationTypes = "permission_prompt|idle_prompt|elicitation_dialog";
        const long MaxEventsFile = 64 * 1024;

        /// <summary>Test hooks: Claude's and Codex's folders.</summary>
        internal static string ClaudeDirOverride, CodexHomeOverride;

        static string EventsName { get { return AppPaths.Dev ? "sentripet-events-dev.jsonl" : "sentripet-events.jsonl"; } }
        public static string ClaudeDir
        {
            get
            {
                if (ClaudeDirOverride != null) return ClaudeDirOverride;
                string cfg = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                return string.IsNullOrEmpty(cfg) ? Path.Combine(AppPaths.Home, ".claude") : cfg;
            }
        }
        public static string CodexHome
        {
            get
            {
                if (CodexHomeOverride != null) return CodexHomeOverride;
                string h = Environment.GetEnvironmentVariable("CODEX_HOME");
                return string.IsNullOrEmpty(h) ? Path.Combine(AppPaths.Home, ".codex") : h;
            }
        }
        public static string ClaudeEventsFile { get { return Path.Combine(ClaudeDir, EventsName); } }
        public static string CodexEventsFile { get { return Path.Combine(CodexHome, EventsName); } }
        public static string ClaudeSettingsFile { get { return Path.Combine(ClaudeDir, "settings.json"); } }
        public static string CodexConfigFile { get { return Path.Combine(CodexHome, "config.toml"); } }

        // ------------------------------------------------------------------ the hook commands (print nothing: Claude
        // Code reads a Stop hook's JSON output as a decision, and a prompt hook's text as context)

        /// <summary><c>SentriPet --hook claude</c>: Claude Code's hook input on stdin.</summary>
        public static int RunClaude(TextReader stdin, DateTime now)
        {
            var e = FromClaude(Json.TryParse(stdin.ReadToEnd()), now);
            if (e != null) Append(ClaudeEventsFile, e);
            return 0;
        }

        internal static AgentEvent FromClaude(object input, DateTime now)
        {
            string evt = Json.Str(Json.Get(input, "hook_event_name"));
            var e = new AgentEvent { Source = "claude", At = now, Project = ProjectName(Json.Str(Json.Get(input, "cwd"))) };
            if (evt == "Stop")
            {
                if (Json.Bool(Json.Get(input, "stop_hook_active")) == true) return null;   // another hook made it continue
                e.Kind = AgentEvent.Done;
                e.Seconds = ClaudeTurnSeconds(Json.Str(Json.Get(input, "transcript_path")), now);
            }
            else if (evt == "Notification")
            {
                string type = Json.Str(Json.Get(input, "notification_type")) ?? "";
                if (type == "permission_prompt") e.Kind = AgentEvent.Permission;
                else if (type == "idle_prompt" || type == "elicitation_dialog") e.Kind = AgentEvent.Waiting;
                else if (type.Length == 0 && (Json.Str(Json.Get(input, "message")) ?? "").IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0) e.Kind = AgentEvent.Permission;
                else if (type.Length == 0) e.Kind = AgentEvent.Waiting;   // older Claude Code: no type
                else return null;
            }
            else return null;
            return e;
        }

        /// <summary>Set for the notify program SentriPet starts: a SentriPet started from inside it has already been run.</summary>
        const string ChainedVariable = "SENTRIPET_CODEX_CHAINED";

        /// <summary>
        /// <c>SentriPet --hook codex JSON</c>: Codex puts the event as the last argument. A notify program the user had
        /// before is started with the same argument (see <see cref="ChainToRun"/>).
        /// </summary>
        public static int RunCodex(string[] args, AppSettings s, DateTime now)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ChainedVariable))) return 0;   // (called again by our own chain: no loop, no second event)
            string json = args.Length > 0 ? args[args.Length - 1] : "";
            var e = FromCodex(Json.TryParse(json), now, CodexHome);
            if (e != null) Append(CodexEventsFile, e);
            var chain = ChainToRun(s.CodexNotifyChain, ReadCodexNotify());
            if (chain != null)
            {
                try
                {
                    var psi = new ProcessStartInfo { FileName = chain[0], UseShellExecute = false, CreateNoWindow = true };
                    foreach (var a in chain.Skip(1)) psi.ArgumentList.Add(a);
                    psi.ArgumentList.Add(json);
                    psi.Environment[ChainedVariable] = "1";
                    Process.Start(psi);
                }
                catch (Exception ex) { Log.Warn("chained codex notify: " + ex.Message); }
            }
            return 0;
        }

        /// <summary>
        /// What <c>--hook codex</c> runs after itself: the notify program the user had (<paramref name="remembered"/>), as
        /// it is now. Nothing when Codex runs that program itself — the Codex app puts its own notify first and calls the
        /// one before it (SentriPet) with <c>--previous-notify</c> — or when it would call SentriPet again. A program inside
        /// a version folder that a Codex update replaced is found in the new one; gone for good → nothing (and a warning).
        /// </summary>
        internal static List<string> ChainToRun(string remembered, List<string> notify)
        {
            var chain = ParseTomlArray(remembered);
            if (chain == null || chain.Count == 0 || IsOurs(chain) || WrappedAt(chain) > 0) return null;
            if (notify != null && !IsOurs(notify) && SameProgram(chain, notify)) return null;
            string missing = Missing(chain[0]);
            if (missing == null) return chain;
            string moved = FindMoved(missing);
            if (moved == null) { Log.Warn("chained codex notify is gone: " + missing); return null; }
            chain[0] = moved;
            return chain;
        }

        /// <summary>The notify program SentriPet should still run but cannot find (for the settings page), or null.</summary>
        public static string MissingChain(AppSettings s)
        {
            var chain = ParseTomlArray(s.CodexNotifyChain);
            if (chain == null || chain.Count == 0 || Missing(chain[0]) == null) return null;
            var notify = ReadCodexNotify();
            if (notify != null && !IsOurs(notify) && SameProgram(chain, notify)) return null;   // (Codex runs it)
            return FindMoved(chain[0]) == null ? chain[0] : null;
        }

        /// <summary>A program given by its full path that is not there (a bare name is looked up in PATH: never "missing").</summary>
        static string Missing(string program)
        {
            return Path.IsPathFullyQualified(program) && !File.Exists(program) ? program : null;
        }

        /// <summary>
        /// A program inside a version folder an update replaced (…\runtimes\cua_node\&lt;version&gt;\bin\…\x.exe): the same
        /// file in the newest folder next to the one that is gone, or null.
        /// </summary>
        internal static string FindMoved(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                var rest = new List<string> { Path.GetFileName(path) };
                while (dir != null && !Directory.Exists(dir)) { rest.Insert(0, Path.GetFileName(dir)); dir = Path.GetDirectoryName(dir); }
                // only a folder that is gone (not the file itself), and not one right under a drive
                if (dir == null || Path.GetDirectoryName(dir) == null || rest.Count < 2) return null;
                string tail = Path.Combine(rest.Skip(1).ToArray());
                return Directory.GetDirectories(dir).Take(200).Where(d => File.Exists(Path.Combine(d, tail)))
                                .OrderByDescending(Directory.GetLastWriteTimeUtc).Select(d => Path.Combine(d, tail)).FirstOrDefault();
            }
            catch { return null; }
        }

        internal static AgentEvent FromCodex(object input, DateTime now, string codexHome)
        {
            if (Json.Str(Json.Get(input, "type")) != "agent-turn-complete") return null;
            return new AgentEvent
            {
                Source = "codex", Kind = AgentEvent.Done, At = now,
                Project = ProjectName(Json.Str(Json.Get(input, "cwd"))),
                Seconds = CodexTurnSeconds(codexHome, Json.Str(Json.Get(input, "thread-id")), now),
            };
        }

        static string ProjectName(string cwd)
        {
            if (string.IsNullOrEmpty(cwd)) return null;
            string n = Path.GetFileName(cwd.TrimEnd('/', '\\'));
            return string.IsNullOrEmpty(n) ? null : n;
        }

        /// <summary>The last lines of a file (the tail of a long transcript), oldest first.</summary>
        static List<string> Tail(string path, int bytes)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long start = Math.Max(0, fs.Length - bytes);
                fs.Seek(start, SeekOrigin.Begin);
                var text = new StreamReader(fs, Encoding.UTF8).ReadToEnd();
                var lines = text.Split('\n').ToList();
                if (start > 0 && lines.Count > 0) lines.RemoveAt(0);   // (cut in the middle)
                return lines;
            }
        }

        /// <summary>Seconds since the user's last prompt in a Claude Code transcript, or -1.</summary>
        internal static double ClaudeTurnSeconds(string transcript, DateTime now)
        {
            try
            {
                if (string.IsNullOrEmpty(transcript) || !File.Exists(transcript)) return -1;
                var lines = Tail(transcript, 512 * 1024);
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    if (lines[i].IndexOf("\"user\"", StringComparison.Ordinal) < 0) continue;
                    var o = Json.TryParse(lines[i]);
                    if (Json.Str(Json.Get(o, "type")) != "user" || Json.Bool(Json.Get(o, "isMeta")) == true) continue;
                    var content = Json.Get(Json.Get(o, "message"), "content");
                    bool prompt = content is string;
                    var list = Json.Arr(content);
                    if (list != null)
                        prompt = list.Cast<object>().Any(x => Json.Str(Json.Get(x, "type")) == "text") &&
                                 !list.Cast<object>().Any(x => Json.Str(Json.Get(x, "type")) == "tool_result");
                    if (!prompt) continue;
                    var at = Json.Date(Json.Get(o, "timestamp"));
                    return at.HasValue ? Math.Max(0, (now - at.Value).TotalSeconds) : -1;
                }
            }
            catch (Exception ex) { Log.Warn("claude transcript: " + ex.Message); }
            return -1;
        }

        /// <summary>Seconds since the turn started in Codex's session log of this thread (~/.codex/sessions/…/rollout-…-THREAD.jsonl), or -1.</summary>
        internal static double CodexTurnSeconds(string codexHome, string threadId, DateTime now)
        {
            try
            {
                if (string.IsNullOrEmpty(threadId)) return -1;
                string root = Path.Combine(codexHome, "sessions");
                if (!Directory.Exists(root)) return -1;
                // the session may have started a few days ago: look in the latest day folders
                var days = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                                    .Where(d => Regex.IsMatch(d.Replace('\\', '/'), @"/\d{4}/\d{2}/\d{2}$"))
                                    .OrderByDescending(d => d.Replace('\\', '/'), StringComparer.Ordinal).Take(7);
                string file = days.SelectMany(d => Directory.GetFiles(d, "rollout-*.jsonl")).FirstOrDefault(f => Path.GetFileName(f).Contains(threadId));
                if (file == null) return -1;
                var lines = Tail(file, 512 * 1024);
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    if (lines[i].IndexOf("task_started", StringComparison.Ordinal) < 0) continue;
                    var o = Json.TryParse(lines[i]);
                    if (Json.Str(Json.Get(Json.Get(o, "payload"), "type")) != "task_started") continue;
                    var at = Json.Date(Json.Get(o, "timestamp"));
                    return at.HasValue ? Math.Max(0, (now - at.Value).TotalSeconds) : -1;
                }
            }
            catch (Exception ex) { Log.Warn("codex session: " + ex.Message); }
            return -1;
        }

        // ------------------------------------------------------------------ the events files

        internal static void Append(string file, AgentEvent e)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var o = new Dictionary<string, object> { { "at", e.At.ToString("o", CultureInfo.InvariantCulture) }, { "source", e.Source }, { "kind", e.Kind } };
                if (e.Seconds >= 0) o["seconds"] = Math.Round(e.Seconds, 1);
                if (e.Project != null) o["project"] = e.Project;
                string line = Json.Serialize(o, false) + "\n";
                if (File.Exists(file) && new FileInfo(file).Length > MaxEventsFile)
                {
                    // keep it small: only the last lines matter (the widget reads what is new)
                    var keep = File.ReadAllLines(file, Encoding.UTF8).Where(l => l.Length > 0).Reverse().Take(20).Reverse().ToList();
                    File.WriteAllText(file, string.Join("\n", keep) + "\n" + line, new UTF8Encoding(false));
                }
                else File.AppendAllText(file, line, new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Warn("agent event: " + ex.Message); }
        }

        internal static AgentEvent ParseLine(string line)
        {
            var o = Json.TryParse(line);
            var at = Json.Date(Json.Get(o, "at"));
            string kind = Json.Str(Json.Get(o, "kind"));
            if (!at.HasValue || kind == null) return null;
            return new AgentEvent
            {
                Source = Json.Str(Json.Get(o, "source")), Kind = kind, At = at.Value,
                Seconds = Json.Num(Json.Get(o, "seconds")) ?? -1, Project = Json.Str(Json.Get(o, "project")),
            };
        }

        /// <summary>Reads what the hooks wrote since the last look (what was there when the widget started is old news).</summary>
        public class Reader
        {
            readonly string file;
            long position = -1;

            public Reader(string file) { this.file = file; }

            public List<AgentEvent> ReadNew()
            {
                var list = new List<AgentEvent>();
                try
                {
                    long length = File.Exists(file) ? new FileInfo(file).Length : 0;
                    if (position < 0 || length < position) { position = position < 0 ? length : 0; if (length == position) return list; }
                    if (length == position) return list;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        fs.Seek(position, SeekOrigin.Begin);
                        var text = new StreamReader(fs, Encoding.UTF8).ReadToEnd();
                        int end = text.LastIndexOf('\n');
                        if (end < 0) return list;          // (a line still being written)
                        position += Encoding.UTF8.GetByteCount(text.Substring(0, end + 1));
                        foreach (var l in text.Substring(0, end).Split('\n'))
                        {
                            var e = ParseLine(l);
                            if (e != null) list.Add(e);
                        }
                    }
                }
                catch (Exception ex) { Log.Warn("agent events: " + ex.Message); }
                return list;
            }
        }

        // ------------------------------------------------------------------ Claude Code's hooks

        internal static string HookCommand(string exe, string source) { return ClaudeStatusLine.ExeCommand(exe) + " --hook " + source; }

        static bool IsOursCommand(string command)
        {
            return command != null && command.Contains("--hook") && command.IndexOf(AppInfo.Name, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>A hook group (matcher + hooks) that runs SentriPet.</summary>
        static bool IsOurGroup(object group)
        {
            var hooks = Json.Arr(Json.Get(group, "hooks"));
            return hooks != null && hooks.Cast<object>().Any(h => IsOursCommand(Json.Str(Json.Get(h, "command"))));
        }

        /// <summary>SentriPet's commands among Claude Code's Stop hooks.</summary>
        static List<string> ClaudeHookCommands()
        {
            var stop = Json.Arr(Json.Get(Json.Get(ClaudeStatusLine.ReadClaudeSettings(ClaudeSettingsFile), "hooks"), "Stop"));
            if (stop == null) return new List<string>();
            return stop.Cast<object>().SelectMany(g => (Json.Arr(Json.Get(g, "hooks")) ?? new List<object>()).Cast<object>())
                       .Select(h => Json.Str(Json.Get(h, "command"))).Where(IsOursCommand).ToList();
        }

        public static bool IsClaudeConnected()
        {
            try
            {
                var hooks = Json.Get(ClaudeStatusLine.ReadClaudeSettings(ClaudeSettingsFile), "hooks");
                var stop = Json.Arr(Json.Get(hooks, "Stop"));
                return stop != null && stop.Cast<object>().Any(IsOurGroup);
            }
            catch { return false; }
        }

        static void SetClaudeHooks(string exe)
        {
            var root = ClaudeStatusLine.ReadClaudeSettings(ClaudeSettingsFile);
            var hooks = Json.Obj(Json.Get(root, "hooks"));
            if (hooks == null && Json.Get(root, "hooks") != null) throw new InvalidDataException(L.T("Claude 的 settings.json 格式看不懂，沒有修改"));
            if (hooks == null) hooks = new Dictionary<string, object>();
            foreach (var evt in new[] { "Stop", "Notification" })
            {
                var list = Json.Arr(Json.Get(hooks, evt));
                var keep = list == null ? new List<object>() : list.Cast<object>().Where(g => !IsOurGroup(g)).ToList();
                if (exe != null)
                {
                    var group = new Dictionary<string, object>();
                    if (evt == "Notification") group["matcher"] = NotificationTypes;
                    group["hooks"] = new List<object>
                    {
                        new Dictionary<string, object> { { "type", "command" }, { "command", HookCommand(exe, "claude") }, { "timeout", 10 } },
                    };
                    keep.Add(group);
                }
                if (keep.Count > 0) hooks[evt] = keep;
                else hooks.Remove(evt);
            }
            if (hooks.Count > 0) root["hooks"] = hooks;
            else root.Remove("hooks");
            ClaudeStatusLine.WriteClaudeSettings(ClaudeSettingsFile, root);
        }

        // ------------------------------------------------------------------ Codex's notify program (config.toml)

        /// <summary>The top-level <c>notify = [...]</c> of a config.toml: where it is and its value, or null.</summary>
        internal class TomlKey { public int Start, End; public string Value; }

        internal static TomlKey FindNotify(string toml)
        {
            if (toml == null) return null;
            int pos = 0;
            while (pos < toml.Length)
            {
                int eol = toml.IndexOf('\n', pos);
                string line = toml.Substring(pos, (eol < 0 ? toml.Length : eol) - pos);
                string t = line.TrimStart();
                if (t.StartsWith("[")) return null;   // a table: no top-level keys after it
                var m = Regex.Match(line, @"^\s*notify\s*=\s*");
                if (m.Success)
                {
                    // the value: an array, possibly over several lines (strings may hold brackets)
                    int i = pos + m.Length, depth = 0;
                    bool started = false;
                    char quote = '\0';
                    for (; i < toml.Length; i++)
                    {
                        char c = toml[i];
                        if (quote != '\0')
                        {
                            if (quote == '"' && c == '\\') { i++; continue; }
                            if (c == quote) quote = '\0';
                            continue;
                        }
                        if (c == '"' || c == '\'') { quote = c; continue; }
                        if (c == '#' && depth == 0) break;
                        if (c == '[') { depth++; started = true; }
                        else if (c == ']') { depth--; if (depth == 0 && started) { i++; break; } }
                        else if (c == '\n' && depth == 0) break;
                    }
                    int end = toml.IndexOf('\n', i);
                    return new TomlKey { Start = pos, End = end < 0 ? toml.Length : end + 1, Value = toml.Substring(pos + m.Length, i - pos - m.Length).Trim() };
                }
                if (eol < 0) break;
                pos = eol + 1;
            }
            return null;
        }

        /// <summary>A TOML array of strings (<c>["a", 'b']</c>) → the strings, or null when it is not one.</summary>
        internal static List<string> ParseTomlArray(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var list = new List<string>();
            string v = value.Trim();
            if (!v.StartsWith("[") || !v.EndsWith("]")) return null;
            int i = 1;
            while (i < v.Length - 1)
            {
                char c = v[i];
                if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
                if (c == '#') { while (i < v.Length && v[i] != '\n') i++; continue; }
                if (c == '\'')
                {
                    int end = v.IndexOf('\'', i + 1);
                    if (end < 0) return null;
                    list.Add(v.Substring(i + 1, end - i - 1));
                    i = end + 1;
                }
                else if (c == '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    for (; i < v.Length && v[i] != '"'; i++)
                    {
                        if (v[i] != '\\') { sb.Append(v[i]); continue; }
                        char n = ++i < v.Length ? v[i] : '\\';
                        if (n == 'n') sb.Append('\n');
                        else if (n == 't') sb.Append('\t');
                        else if (n == 'u' && i + 4 < v.Length) { sb.Append((char)Convert.ToInt32(v.Substring(i + 1, 4), 16)); i += 4; }
                        else sb.Append(n);
                    }
                    if (i >= v.Length) return null;
                    list.Add(sb.ToString());
                    i++;
                }
                else return null;
            }
            return list;
        }

        static string TomlString(string s)
        {
            // a literal string when possible (Windows paths need no escaping)
            if (s.IndexOf('\'') < 0 && s.IndexOf('\n') < 0) return "'" + s + "'";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        internal static string NotifyLine(string exe)
        {
            return "notify = [" + TomlString(exe) + ", '--hook', 'codex']";
        }

        static string NotifyLine(List<string> notify)
        {
            return "notify = [" + string.Join(", ", notify.Select(TomlString)) + "]";
        }

        /// <summary>SentriPet's command as the Codex app writes it after <c>--previous-notify</c> (a JSON array).</summary>
        static string OurJson(string exe)
        {
            return Json.Serialize(new List<object> { exe, "--hook", "codex" }, false);
        }

        /// <summary>A notify command that is SentriPet's.</summary>
        static bool IsOurs(List<string> notify)
        {
            return notify != null && notify.Count >= 2 && notify[0].IndexOf(AppInfo.Name, StringComparison.OrdinalIgnoreCase) >= 0 && notify.Contains("--hook");
        }

        /// <summary>
        /// Where in another program's notify command SentriPet's own command is, as a JSON array — the Codex app's
        /// <c>[codex-computer-use.exe, "turn-ended", "--previous-notify", "[\"…SentriPet.exe\",\"--hook\",\"codex\"]"]</c>
        /// runs the notify program that was there before it — or -1.
        /// </summary>
        static int WrappedAt(List<string> notify)
        {
            if (notify == null) return -1;
            for (int i = 1; i < notify.Count; i++)
            {
                if (!notify[i].TrimStart().StartsWith("[")) continue;
                var inner = Json.Arr(Json.TryParse(notify[i]));
                if (inner != null && inner.Cast<object>().All(x => x is string) && IsOurs(inner.Cast<string>().ToList())) return i;
            }
            return -1;
        }

        static List<string> Unwrapped(List<string> notify, int at)
        {
            var list = notify.ToList();
            list.RemoveAt(at);
            if (at >= 2 && list[at - 1].StartsWith("--")) list.RemoveAt(at - 1);   // (the --previous-notify before it)
            return list;
        }

        /// <summary>
        /// The same notify program, perhaps of another version: the same file name and arguments (what one of them runs
        /// before or after itself aside). <c>…\b63ee…\codex-computer-use.exe turn-ended</c> is the program the Codex app
        /// now runs as <c>…\3dd31…\codex-computer-use.exe turn-ended --previous-notify […SentriPet…]</c>.
        /// </summary>
        static bool SameProgram(List<string> a, List<string> b)
        {
            int wa = WrappedAt(a), wb = WrappedAt(b);
            if (wa > 0) a = Unwrapped(a, wa);
            if (wb > 0) b = Unwrapped(b, wb);
            Func<string, string> name = p => Path.GetFileName(p.Replace('\\', '/'));
            return a.Count > 0 && a.Count == b.Count && string.Equals(name(a[0]), name(b[0]), StringComparison.OrdinalIgnoreCase) && a.Skip(1).SequenceEqual(b.Skip(1));
        }

        /// <summary>Codex's notify command now, or null.</summary>
        internal static List<string> ReadCodexNotify()
        {
            try
            {
                if (!File.Exists(CodexConfigFile)) return null;
                var k = FindNotify(File.ReadAllText(CodexConfigFile, Encoding.UTF8));
                return k == null ? null : ParseTomlArray(k.Value);
            }
            catch { return null; }
        }

        /// <summary>SentriPet is Codex's notify program, or the one the Codex app's notify program runs after itself.</summary>
        public static bool IsCodexConnected()
        {
            var notify = ReadCodexNotify();
            return IsOurs(notify) || WrappedAt(notify) > 0;
        }

        const string OurComment = "# SentriPet: \"done / waiting for you\" (turn off in SentriPet's settings)";

        static void WriteCodexConfig(string text)
        {
            string path = CodexConfigFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string backup = path + ".sentripet-backup";
            if (File.Exists(path) && !File.Exists(backup)) File.Copy(path, backup);
            string tmp = path + ".sentripet-tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }

        /// <summary>The SentriPet program inside the Codex app's notify command (see <see cref="WrappedAt"/>).</summary>
        static string WrappedExe(List<string> notify, int at)
        {
            return Json.Str(Json.Arr(Json.TryParse(notify[at]))[0]);
        }

        static void ConnectCodex(AppSettings s, string exe)
        {
            string path = CodexConfigFile;
            string text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
            var k = FindNotify(text);
            var notify = k == null ? null : ParseTomlArray(k.Value);
            int at = WrappedAt(notify);
            if (at > 0)
            {
                // the Codex app runs its own notify program and then SentriPet: stay there, only follow a move (never
                // remember that program — it would run SentriPet again)
                if (WrappedExe(notify, at) != exe)
                {
                    notify[at] = OurJson(exe);
                    WriteCodexConfig(text.Substring(0, k.Start) + NotifyLine(notify) + "\n" + text.Substring(k.End));
                }
                TidyChain(s, notify);
                return;
            }
            string line = NotifyLine(exe) + "\n";
            if (k != null)
            {
                if (!IsOurs(notify)) s.CodexNotifyChain = k.Value;   // the user's own notify program keeps running
                text = text.Substring(0, k.Start) + line + text.Substring(k.End);
            }
            else
            {
                s.CodexNotifyChain = null;
                text = OurComment + "\n" + line + (text.Length > 0 && !text.StartsWith("\n") ? "\n" : "") + text;
            }
            WriteCodexConfig(text);
            TidyChain(s, new List<string> { exe, "--hook", "codex" });
        }

        /// <summary>
        /// The notify program SentriPet remembers: forgotten when Codex runs it itself now (the Codex app put its own
        /// notify first) or it would run SentriPet; followed into the new version folder when a Codex update moved it.
        /// </summary>
        static void TidyChain(AppSettings s, List<string> notify)
        {
            var chain = ParseTomlArray(s.CodexNotifyChain);
            if (chain == null || chain.Count == 0) return;
            if (IsOurs(chain) || WrappedAt(chain) > 0 || (notify != null && !IsOurs(notify) && SameProgram(chain, notify)))
            {
                s.CodexNotifyChain = null;
                Log.Info("SentriPet no longer chains codex notify " + chain[0] + " (Codex runs it itself, or it would run SentriPet)");
                return;
            }
            string missing = Missing(chain[0]), moved = missing == null ? null : FindMoved(missing);
            if (moved == null) return;
            chain[0] = moved;
            s.CodexNotifyChain = "[" + string.Join(", ", chain.Select(TomlString)) + "]";
            Log.Info("codex notify program moved -> " + moved);
        }

        static void DisconnectCodex(AppSettings s)
        {
            string path = CodexConfigFile;
            if (!File.Exists(path)) { s.CodexNotifyChain = null; return; }
            string text = File.ReadAllText(path, Encoding.UTF8);
            var k = FindNotify(text);
            var notify = k == null ? null : ParseTomlArray(k.Value);
            int at = WrappedAt(notify);
            if (IsOurs(notify))
            {
                string original = string.IsNullOrEmpty(s.CodexNotifyChain) ? "" : "notify = " + s.CodexNotifyChain + "\n";
                text = text.Substring(0, k.Start) + original + text.Substring(k.End);
                text = text.Replace(OurComment + "\n", "");
                if (original.Length == 0 && text.StartsWith("\n")) text = text.Substring(1);
                WriteCodexConfig(text);
            }
            else if (at > 0)
            {
                // the Codex app's notify runs SentriPet after itself: it runs the program SentriPet ran instead, or nothing
                var chain = ParseTomlArray(s.CodexNotifyChain);
                List<string> rest;
                if (chain != null && chain.Count > 0 && !IsOurs(chain) && WrappedAt(chain) < 0 && !SameProgram(chain, notify))
                {
                    rest = notify.ToList();
                    rest[at] = Json.Serialize(chain.Cast<object>().ToList(), false);
                }
                else rest = Unwrapped(notify, at);
                text = text.Substring(0, k.Start) + NotifyLine(rest) + "\n" + text.Substring(k.End);
                WriteCodexConfig(text.Replace(OurComment + "\n", ""));
            }
            s.CodexNotifyChain = null;
        }

        // ------------------------------------------------------------------ turning it on and off

        /// <summary>Claude Code's hooks when Claude Code is here, Codex's notify when Codex is here (at least one).</summary>
        public static void Connect(AppSettings s, string exe, bool claude, bool codex)
        {
            if (claude) SetClaudeHooks(exe);
            if (codex) ConnectCodex(s, exe);
            s.AgentHooks = true;
        }

        public static void Disconnect(AppSettings s)
        {
            Exception first = null;
            try { if (File.Exists(ClaudeSettingsFile)) SetClaudeHooks(null); } catch (Exception ex) { first = ex; }
            try { DisconnectCodex(s); } catch (Exception ex) { if (first == null) first = ex; }
            s.AgentHooks = false;
            if (first != null) throw first;
        }

        /// <summary>Is Claude Code / Codex installed here (its folder exists)?</summary>
        public static bool HasClaude { get { return Directory.Exists(ClaudeDir); } }
        public static bool HasCodex { get { return Directory.Exists(CodexHome); } }

        /// <summary>
        /// At start: follows the program when it moved; notices when the user took SentriPet out of both; tidies the
        /// notify program SentriPet runs after itself for Codex (<see cref="TidyChain"/>).
        /// </summary>
        public static void Repair(AppSettings s, string exe)
        {
            if (!s.AgentHooks) return;
            try
            {
                bool claude = IsClaudeConnected(), codex = IsCodexConnected();
                if (!claude && !codex) { s.AgentHooks = false; s.CodexNotifyChain = null; Log.Info("agent hooks were removed in Claude Code and Codex"); return; }
                if (claude && !ClaudeHookCommands().Contains(HookCommand(exe, "claude"))) { SetClaudeHooks(exe); Log.Info("claude hooks -> " + exe); }
                if (codex)
                {
                    var notify = ReadCodexNotify();
                    int at = WrappedAt(notify);
                    if ((at > 0 ? WrappedExe(notify, at) : notify[0]) != exe) { ConnectCodex(s, exe); Log.Info("codex notify -> " + exe); }
                    else TidyChain(s, notify);
                }
            }
            catch (Exception ex) { Log.Warn("agent hooks: " + ex.Message); }
        }
    }
}
