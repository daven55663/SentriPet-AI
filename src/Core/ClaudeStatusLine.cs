using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>
    /// Bridge to Claude Code's status line (#9). Claude Code hands its status-line command the official plan usage
    /// (<c>rate_limits.five_hour</c> / <c>seven_day</c>: used % and reset time) after every reply. When the user turns the
    /// bridge on, SentriPet registers itself as that command (<c>SentriPet --statusline</c>), keeps the numbers in a small
    /// file for <see cref="ClaudeProvider"/>, and still shows the status line the user had before (its command is run
    /// with the same input). This is the only source of Claude usage on Linux, where there is no Claude desktop app.
    /// </summary>
    static class ClaudeStatusLine
    {
        public class Window
        {
            public double Used;          // 0..100
            public DateTime ResetsAt;    // UTC
        }

        public class Data
        {
            public DateTime ObservedAt;   // UTC
            public Window FiveHour, SevenDay;
            public string Model;

            public Window For(string key) { return key == "fh" ? FiveHour : key == "sd" ? SevenDay : null; }
        }

        /// <summary>Test hooks: the bridge file and Claude's settings.json.</summary>
        internal static string FileOverride, SettingsOverride;

        /// <summary>
        /// Where the status line leaves the numbers: next to Claude's own settings in the home folder, not under AppData —
        /// processes started from the Claude desktop app (MSIX) get new AppData files redirected to a private copy.
        /// </summary>
        public static string DataFile
        {
            get { return FileOverride ?? Path.Combine(AppPaths.Home, ".claude", AppPaths.Dev ? "sentripet-status-dev.json" : "sentripet-status.json"); }
        }
        public static string ClaudeSettingsFile { get { return SettingsOverride ?? Path.Combine(AppPaths.Home, ".claude", "settings.json"); } }

        // ------------------------------------------------------------------ the status-line command

        /// <summary>
        /// <c>SentriPet --statusline</c>: reads Claude Code's JSON from stdin, saves the plan usage, and prints the user's
        /// own status line (or a short usage line when they had none).
        /// </summary>
        public static int Run(TextReader stdin, TextWriter stdout, AppSettings settings)
        {
            string input = stdin.ReadToEnd();
            var data = Extract(Json.TryParse(input), DateTime.UtcNow);
            if (data != null)
            {
                try { Save(data); }
                catch (Exception ex) { Log.Warn("status line: " + ex.Message); }
            }
            string chained = ChainedCommand(settings);
            if (!string.IsNullOrWhiteSpace(chained))
            {
                string output = RunChained(chained, input);
                if (output != null) { stdout.Write(output); return 0; }
            }
            stdout.WriteLine(OwnLine(data ?? Load()));
            return 0;
        }

        /// <summary>The plan usage in Claude Code's status-line input, or null when it has none (API key, first message not answered yet).</summary>
        internal static Data Extract(object root, DateTime nowUtc)
        {
            var rl = Json.Get(root, "rate_limits");
            if (rl == null) return null;
            var d = new Data { ObservedAt = nowUtc, FiveHour = WindowOf(Json.Get(rl, "five_hour")), SevenDay = WindowOf(Json.Get(rl, "seven_day")) };
            d.Model = Json.Str(Json.Path(root, "model.display_name"));
            return d.FiveHour != null || d.SevenDay != null ? d : null;
        }

        static Window WindowOf(object o)
        {
            double? used = Json.Num(Json.Get(o, "used_percentage"));
            double? resets = Json.Num(Json.Get(o, "resets_at"));
            if (!used.HasValue || !resets.HasValue) return null;
            return new Window { Used = Math.Max(0, Math.Min(100, used.Value)), ResetsAt = Json.FromUnix(resets.Value) };
        }

        /// <summary>A short line for users without a status line of their own: "5h 剩 76% · 週 剩 59%".</summary>
        internal static string OwnLine(Data d)
        {
            if (d == null) return AppInfo.Name;
            var parts = new List<string>();
            if (d.FiveHour != null) parts.Add(L.F("{0} 剩 {1}", "5h", Fmt.Pct(100 - d.FiveHour.Used)));
            if (d.SevenDay != null) parts.Add(L.F("{0} 剩 {1}", L.T("週"), Fmt.Pct(100 - d.SevenDay.Used)));
            return string.Join(" · ", parts);
        }

        // ------------------------------------------------------------------ the saved numbers

        public static void Save(Data d)
        {
            var o = new Dictionary<string, object>();
            o["observedAt"] = d.ObservedAt;
            if (d.Model != null) o["model"] = d.Model;
            if (d.FiveHour != null) o["fiveHour"] = WindowJson(d.FiveHour);
            if (d.SevenDay != null) o["sevenDay"] = WindowJson(d.SevenDay);
            string path = DataFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(o, true), new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }

        static Dictionary<string, object> WindowJson(Window w)
        {
            var o = new Dictionary<string, object>();
            o["used"] = w.Used;
            o["resetsAt"] = w.ResetsAt;
            return o;
        }

        /// <summary>The numbers the status line saved last, or null.</summary>
        public static Data Load()
        {
            try
            {
                string path = DataFile;
                if (!File.Exists(path)) return null;
                var o = Json.TryParse(AppPaths.ReadShared(path));
                var at = Json.Date(Json.Get(o, "observedAt"));
                if (!at.HasValue) return null;
                var d = new Data { ObservedAt = at.Value, Model = Json.Str(Json.Get(o, "model")) };
                d.FiveHour = SavedWindow(Json.Get(o, "fiveHour"));
                d.SevenDay = SavedWindow(Json.Get(o, "sevenDay"));
                return d.FiveHour != null || d.SevenDay != null ? d : null;
            }
            catch (Exception ex) { Log.Warn("status line data: " + ex.Message); return null; }
        }

        static Window SavedWindow(object o)
        {
            double? used = Json.Num(Json.Get(o, "used"));
            var resets = Json.Date(Json.Get(o, "resetsAt"));
            return used.HasValue && resets.HasValue ? new Window { Used = used.Value, ResetsAt = resets.Value } : null;
        }

        // ------------------------------------------------------------------ the user's own status line

        static string ChainedCommand(AppSettings s)
        {
            if (string.IsNullOrEmpty(s.ClaudeStatusLineChain)) return null;
            return Json.Str(Json.Get(Json.TryParse(s.ClaudeStatusLineChain), "command"));
        }

        /// <summary>
        /// Runs the user's status-line command the way Claude Code would (Git Bash or PowerShell on Windows, sh elsewhere)
        /// with the same input; its output, or null when it could not run.
        /// </summary>
        internal static string RunChained(string command, string input)
        {
            try
            {
                var psi = new ProcessStartInfo { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
                if (Os.Windows)
                {
                    string bash = GitBash();
                    if (bash != null) { psi.FileName = bash; psi.ArgumentList.Add("-c"); }
                    else { psi.FileName = "powershell.exe"; psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-Command"); }
                }
                else { psi.FileName = "/bin/sh"; psi.ArgumentList.Add("-c"); }
                psi.ArgumentList.Add(command);
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    try
                    {
                        var w = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false));
                        w.Write(input);
                        w.Close();
                    }
                    catch (IOException) { }   // the command did not read its input
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(10000)) { try { p.Kill(true); } catch { } return null; }
                    return outTask.Result;
                }
            }
            catch (Exception ex) { Log.Warn("chained status line: " + ex.Message); return null; }
        }

        /// <summary>Git Bash as Claude Code finds it (not WSL's bash.exe).</summary>
        static string GitBash()
        {
            string env = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            foreach (var p in new[] { @"%ProgramFiles%\Git\bin\bash.exe", @"%ProgramFiles(x86)%\Git\bin\bash.exe", @"%LOCALAPPDATA%\Programs\Git\bin\bash.exe" })
            {
                string f = Environment.ExpandEnvironmentVariables(p);
                if (File.Exists(f)) return f;
            }
            string git = AppPaths.Which("git");
            if (git != null)
            {
                // …\Git\cmd\git.exe → …\Git\bin\bash.exe
                string bash = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(git)), "bin", "bash.exe");
                if (File.Exists(bash)) return bash;
            }
            return null;
        }

        // ------------------------------------------------------------------ turning the bridge on and off

        /// <summary>How Claude Code should call us: forward slashes, "~/…" under the home folder (works in Git Bash and PowerShell).</summary>
        internal static string CommandFor(string exe)
        {
            string p = exe.Replace('\\', '/');
            string home = AppPaths.Home.Replace('\\', '/').TrimEnd('/');
            if (p.StartsWith(home + "/", StringComparison.OrdinalIgnoreCase) && p.IndexOf(' ', home.Length) < 0)
                return "~" + p.Substring(home.Length) + " --statusline";
            return (p.Contains(" ") ? "\"" + p + "\"" : p) + " --statusline";
        }

        internal static bool IsOurs(string command)
        {
            return command != null && command.Contains("--statusline") && command.IndexOf(AppInfo.Name, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static Dictionary<string, object> ReadClaudeSettings()
        {
            string path = ClaudeSettingsFile;
            if (!File.Exists(path)) return new Dictionary<string, object>();
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object>();
            var o = Json.Obj(Json.Parse(text));
            if (o == null) throw new InvalidDataException(L.T("Claude 的 settings.json 格式看不懂，沒有修改"));
            return o;
        }

        static void WriteClaudeSettings(Dictionary<string, object> root)
        {
            string path = ClaudeSettingsFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // keep the user's original once, before SentriPet ever touched it
            string backup = path + ".sentripet-backup";
            if (File.Exists(path) && !File.Exists(backup)) File.Copy(path, backup);
            string tmp = path + ".sentripet-tmp";
            File.WriteAllText(tmp, Json.Serialize(root, true) + "\n", new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }

        /// <summary>Is SentriPet the status-line command in Claude's settings right now?</summary>
        public static bool IsConnected()
        {
            try { return IsOurs(Json.Str(Json.Get(Json.Get(ReadClaudeSettings(), "statusLine"), "command"))); }
            catch { return false; }
        }

        /// <summary>
        /// Makes SentriPet Claude Code's status-line command. A status line the user already had is kept in the settings
        /// (and still shown), together with its other options (padding, refreshInterval…).
        /// </summary>
        public static void Connect(AppSettings s, string exe)
        {
            var root = ReadClaudeSettings();
            var existing = Json.Obj(Json.Get(root, "statusLine"));
            string existingCmd = Json.Str(Json.Get(existing, "command"));
            if (existing != null && !IsOurs(existingCmd)) s.ClaudeStatusLineChain = Json.Serialize(existing, false);
            else if (existing == null) s.ClaudeStatusLineChain = null;
            var line = new Dictionary<string, object>();
            if (existing != null) foreach (var kv in existing) line[kv.Key] = kv.Value;
            line["type"] = "command";
            line["command"] = CommandFor(exe);
            root["statusLine"] = line;
            WriteClaudeSettings(root);
            s.ClaudeStatusBridge = true;
        }

        /// <summary>Puts back the user's own status line (or none) and forgets it.</summary>
        public static void Disconnect(AppSettings s)
        {
            var root = ReadClaudeSettings();
            if (IsOurs(Json.Str(Json.Get(Json.Get(root, "statusLine"), "command"))))
            {
                var original = string.IsNullOrEmpty(s.ClaudeStatusLineChain) ? null : Json.Obj(Json.TryParse(s.ClaudeStatusLineChain));
                if (original != null) root["statusLine"] = original;
                else root.Remove("statusLine");
                WriteClaudeSettings(root);
            }
            s.ClaudeStatusLineChain = null;
            s.ClaudeStatusBridge = false;
        }

        /// <summary>At start: follows the program when it moved, and notices when the user removed the status line in Claude.</summary>
        public static void Repair(AppSettings s, string exe)
        {
            if (!s.ClaudeStatusBridge) return;
            try
            {
                var root = ReadClaudeSettings();
                string cmd = Json.Str(Json.Get(Json.Get(root, "statusLine"), "command"));
                if (!IsOurs(cmd)) { s.ClaudeStatusBridge = false; s.ClaudeStatusLineChain = null; Log.Info("status line bridge was removed in Claude's settings"); return; }
                if (cmd != CommandFor(exe)) { Connect(s, exe); Log.Info("status line bridge -> " + CommandFor(exe)); }
            }
            catch (Exception ex) { Log.Warn("status line bridge: " + ex.Message); }
        }
    }
}
