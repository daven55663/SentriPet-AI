using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentriPet
{
    static class AppPaths
    {
        public static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        public static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        /// <summary>--dev runs with a separate profile so testing never touches the real settings or autostart.</summary>
        public static bool Dev { get; private set; }

        public static void UseDevProfile() { Dev = true; }

        /// <summary>Set by the self-test so settings and logs go to a throwaway folder.</summary>
        internal static string DataDirOverride;

        public static string DataDir { get { return DataDirOverride ?? Path.Combine(AppData, Dev ? "SentriPet-dev" : "SentriPet"); } }
        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.json"); } }
        public static string ProvidersDir { get { return Path.Combine(AppData, "SentriPet", "providers"); } }
        public static string LogDir { get { return Path.Combine(DataDir, "logs"); } }

        public static string ExeDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static void EnsureDataDirs()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                Directory.CreateDirectory(LogDir);
            }
            catch { }
        }

        /// <summary>Created on demand only (the settings button), so read-only runs never create it.</summary>
        public static void EnsureProvidersDir()
        {
            try { Directory.CreateDirectory(ProvidersDir); } catch { }
        }

        /// <summary>Versions before 1.1 were called "AI Usage Pet" and kept their data in %APPDATA%\AIUsagePet.</summary>
        public static void MigrateLegacyData()
        {
            try
            {
                string old = Path.Combine(AppData, Dev ? "AIUsagePet-dev" : "AIUsagePet");
                if (!Directory.Exists(old) || File.Exists(SettingsFile)) return;
                Directory.CreateDirectory(DataDir);
                string oldSettings = Path.Combine(old, "settings.json");
                if (File.Exists(oldSettings)) File.Copy(oldSettings, SettingsFile);
                string oldProviders = Path.Combine(old, "providers");
                if (!Dev && Directory.Exists(oldProviders) && Directory.GetFiles(oldProviders).Length > 0)
                {
                    Directory.CreateDirectory(ProvidersDir);
                    foreach (var f in Directory.GetFiles(oldProviders))
                    {
                        string dest = Path.Combine(ProvidersDir, Path.GetFileName(f));
                        if (!File.Exists(dest)) File.Copy(f, dest);
                    }
                }
                Log.Info("migrated settings from " + old);
            }
            catch (Exception ex) { Log.Error("migrate legacy data", ex); }
        }

        static readonly Regex EnvToken = new Regex(@"\$\{env:([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        /// <summary>Expands %VAR% and ${env:VAR} only — for URLs, headers, request bodies and arguments, whose slashes must stay.</summary>
        public static string ExpandVars(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = EnvToken.Replace(s, m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? "");
            return Environment.ExpandEnvironmentVariables(s);
        }

        /// <summary>A command-line argument: variables, plus a leading ~ for the home folder (slashes kept, so "/c" stays "/c").</summary>
        public static string ExpandArg(string a)
        {
            string s = ExpandVars(a);
            if (s == "~" || (s != null && (s.StartsWith("~/") || s.StartsWith("~\\")))) s = Home + s.Substring(1);
            return s;
        }

        /// <summary>Expands ~, %VAR% and ${env:VAR} in a file path (and turns / into \).</summary>
        public static string Expand(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            string s = ExpandVars(p);
            if (s == "~") return Home;
            if (s.StartsWith("~/") || s.StartsWith("~\\")) s = Path.Combine(Home, s.Substring(2));
            return s.Replace('/', '\\');
        }

        /// <summary>Expands a path whose segments may contain * or ? wildcards.</summary>
        public static List<string> Glob(string pattern)
        {
            var result = new List<string>();
            string p = Expand(pattern);
            if (string.IsNullOrEmpty(p)) return result;
            if (p.IndexOfAny(new[] { '*', '?' }) < 0)
            {
                if (File.Exists(p) || Directory.Exists(p)) result.Add(p);
                return result;
            }
            string[] parts = p.Split('\\');
            var current = new List<string> { parts[0] + "\\" };
            for (int i = 1; i < parts.Length; i++)
            {
                string seg = parts[i];
                if (seg.Length == 0) continue;
                bool last = i == parts.Length - 1;
                var next = new List<string>();
                foreach (var dir in current)
                {
                    try
                    {
                        if (!Directory.Exists(dir)) continue;
                        if (seg.IndexOfAny(new[] { '*', '?' }) >= 0)
                        {
                            next.AddRange(Directory.GetDirectories(dir, seg));
                            if (last) next.AddRange(Directory.GetFiles(dir, seg));
                        }
                        else
                        {
                            string c = Path.Combine(dir, seg);
                            if (Directory.Exists(c) || (last && File.Exists(c))) next.Add(c);
                        }
                    }
                    catch { }
                }
                current = next;
                if (current.Count == 0) break;
            }
            result.AddRange(current);
            return result;
        }

        public static bool AnyExists(params string[] patterns)
        {
            foreach (var p in patterns) if (Glob(p).Count > 0) return true;
            return false;
        }

        static readonly Dictionary<string, string> whichCache = new Dictionary<string, string>();

        /// <summary>Forgets PATH lookups (the self-test changes PATH).</summary>
        internal static void ClearWhichCache()
        {
            lock (whichCache) whichCache.Clear();
        }

        /// <summary>Finds an executable on PATH (honours PATHEXT). Returns null when missing.</summary>
        public static string Which(string name)
        {
            lock (whichCache)
            {
                string cached;
                if (whichCache.TryGetValue(name, out cached)) return cached;
            }
            string found = null;
            try
            {
                var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';');
                var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
                foreach (var d in dirs)
                {
                    if (string.IsNullOrWhiteSpace(d)) continue;
                    string dir = d.Trim().Trim('"');
                    foreach (var e in new[] { "" }.Concat(exts))
                    {
                        if (e.Length == 0 && !Path.HasExtension(name)) continue;
                        string c = Path.Combine(dir, name + e);
                        if (File.Exists(c)) { found = c; break; }
                    }
                    if (found != null) break;
                }
            }
            catch { }
            lock (whichCache) whichCache[name] = found;
            return found;
        }

        /// <summary>Editor extension folders (VS Code, Insiders, Cursor, Windsurf…) whose name starts with prefix. Newest first.</summary>
        public static List<string> EditorExtensions(string prefix)
        {
            var roots = new[] { ".vscode", ".vscode-insiders", ".cursor", ".windsurf", ".trae", ".kiro", ".vscodium" };
            var all = new List<DirectoryInfo>();
            foreach (var r in roots)
            {
                string d = Path.Combine(Home, r, "extensions");
                try
                {
                    if (!Directory.Exists(d)) continue;
                    all.AddRange(new DirectoryInfo(d).GetDirectories(prefix + "*"));
                }
                catch { }
            }
            return all.OrderByDescending(x => x.LastWriteTimeUtc).Select(x => x.FullName).ToList();
        }

        public static bool MsixInstalled(string packagePrefix)
        {
            return Glob(Path.Combine(LocalAppData, "Packages", packagePrefix + "_*")).Count > 0;
        }

        /// <summary>Reads a file even while another process keeps it open for writing.</summary>
        public static string ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                return sr.ReadToEnd();
        }

        public static string ShortPath(string p)
        {
            if (p == null) return null;
            if (p.StartsWith(Home, StringComparison.OrdinalIgnoreCase)) return "~" + p.Substring(Home.Length);
            return p;
        }
    }

    static class Log
    {
        static readonly object gate = new object();

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg, Exception ex)
        {
            Write("ERROR", msg + (ex != null ? " :: " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace : ""));
        }

        static void Write(string level, string msg)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    string f = Path.Combine(AppPaths.LogDir, "app.log");
                    var fi = new FileInfo(f);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = Path.Combine(AppPaths.LogDir, "app.old.log");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(f, old);
                    }
                    File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + msg + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
