using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace SentriPet
{
    /// <summary>
    /// OpenAI Codex rate limits from two credential-free sources:
    ///  1. the official <c>codex app-server</c> JSON-RPC (account/rateLimits/read) — live numbers;
    ///  2. the rate_limits snapshot Codex writes into ~/.codex/sessions/**/rollout-*.jsonl after every turn.
    /// Whichever is newer wins.
    /// </summary>
    class CodexProvider : Provider
    {
        class Window
        {
            public double Used;
            public int Minutes;
            public DateTime? ResetsAt;
        }

        class Bucket
        {
            public string Id;
            public string Name;
            public Window Primary, Secondary;
        }

        class RateSnap
        {
            public DateTime ObservedAt;
            public List<Bucket> Buckets = new List<Bucket>();
            public string Plan;
            public string Credits;
            public string Reached;
            public string Source;
        }

        class TailCache
        {
            public long Length;
            public DateTime Mtime;
            public RateSnap Result;
        }

        ActivityWatcher activity;
        volatile bool logsDirty = true;
        DateTime lastLogScan = DateTime.MinValue;
        RateSnap logSnap, liveSnap;
        DateTime lastLiveTry = DateTime.MinValue;
        string liveError;
        readonly Dictionary<string, TailCache> tailCache = new Dictionary<string, TailCache>(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<string, bool> hotFiles = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        string codexExe;
        DateTime codexExeCheckedAt = DateTime.MinValue;

        /// <summary>Set by the self-test to talk to a fake app-server instead of the installed codex.</summary>
        internal static string ExeOverride, ArgsOverride;

        public CodexProvider()
        {
            Id = "codex";
            Name = "Codex";
            Mascot = "prompt";
            Color = Palette.Hex("#6C7BFF");
        }

        public override int IntervalSeconds { get { return 15; } }

        static string CodexHome()
        {
            string h = Environment.GetEnvironmentVariable("CODEX_HOME");
            return string.IsNullOrEmpty(h) ? Path.Combine(AppPaths.Home, ".codex") : h;
        }

        public override Detection Detect()
        {
            var d = new Detection();
            if (Directory.Exists(Path.Combine(AppPaths.LocalAppData, "OpenAI", "Codex")) || AppPaths.MsixInstalled("OpenAI.Codex"))
                d.Evidence.Add("Codex 桌面版");
            if (Directory.Exists(CodexHome())) d.Evidence.Add("Codex 資料夾");
            if (AppPaths.EditorExtensions("openai.chatgpt-").Count > 0) d.Evidence.Add("VS Code 擴充");
            if (AppPaths.Which("codex") != null) d.Evidence.Add("codex 指令");
            d.Installed = d.Evidence.Count > 0;
            if (FindCodexExe() == null) d.Hint = "找不到 codex 執行檔，只能讀本機紀錄";
            return d;
        }

        public override Snapshot Fetch(bool force, AppSettings settings)
        {
            if (activity == null)
            {
                activity = new ActivityWatcher(Path.Combine(CodexHome(), "sessions"), "*.jsonl");
                activity.Changed += path => { hotFiles[path] = true; logsDirty = true; };
            }
            activity.Ensure();

            var now = DateTime.UtcNow;
            if (force || logsDirty || (now - lastLogScan).TotalSeconds > 120)
            {
                logsDirty = false;
                lastLogScan = now;
                try
                {
                    var r = ScanLogs();
                    if (r != null) logSnap = r;
                }
                catch (Exception ex) { Log.Error("codex log scan", ex); }
            }

            int liveMin = settings.CodexLiveMinutes;
            if (liveMin > 0 && (force || (now - lastLiveTry).TotalMinutes >= liveMin))
            {
                lastLiveTry = now;
                string err;
                var r = QueryLive(out err);
                if (r != null) { liveSnap = r; liveError = null; }
                else { liveError = err; Log.Warn("codex live query: " + err); }
            }

            RateSnap best = liveSnap;
            if (logSnap != null && (best == null || logSnap.ObservedAt > best.ObservedAt)) best = logSnap;
            if (best == null) return Snapshot.Fail(liveError ?? "還沒有用量紀錄：用 Codex 問一句話就會出現");

            var snap = ToSnapshot(best);
            snap.Active = activity.ActiveWithin(60);
            if (liveError != null && best == logSnap)
            {
                snap.Note = "即時查詢失敗（" + liveError + "），顯示本機紀錄";
                if ((now - best.ObservedAt).TotalHours > 6) snap.Stale = true;
            }
            return snap;
        }

        Snapshot ToSnapshot(RateSnap best)
        {
            var now = DateTime.UtcNow;
            var s = new Snapshot { ObservedAt = best.ObservedAt, Source = best.Source, Plan = PlanName(best.Plan) };
            bool multi = best.Buckets.Count > 1;
            foreach (var b in best.Buckets.OrderBy(x => x.Id == "codex" ? 0 : 1))
            {
                string prefix = (multi && b.Id != "codex") ? (b.Name ?? b.Id) + " " : "";
                foreach (var w in new[] { b.Primary, b.Secondary })
                {
                    if (w == null) continue;
                    var m = new Meter
                    {
                        Key = b.Id + ":" + w.Minutes,
                        Used = Math.Max(0, Math.Min(100, w.Used)),
                        WindowMinutes = w.Minutes,
                        ResetsAt = w.ResetsAt,
                        Label = prefix + Fmt.WindowLabel(w.Minutes),
                        ShortLabel = Fmt.WindowShort(w.Minutes),
                    };
                    if (m.ResetsAt.HasValue && m.ResetsAt.Value <= now)
                    {
                        m.Used = 0;
                        m.WasReset = true;
                        if (w.Minutes >= 1440)
                        {
                            while (m.ResetsAt.Value <= now) m.ResetsAt = m.ResetsAt.Value.AddMinutes(w.Minutes);
                            m.ResetApprox = true;
                        }
                        else m.ResetsAt = null;
                    }
                    s.Meters.Add(m);
                }
            }
            var notes = new List<string>();
            if (best.Credits != null) notes.Add(best.Credits);
            if (best.Reached != null) notes.Add("已達上限：" + best.Reached);
            if (notes.Count > 0) s.Note = string.Join("；", notes);
            return s;
        }

        static string PlanName(string plan)
        {
            if (string.IsNullOrEmpty(plan) || plan == "unknown") return null;
            return Fmt.Title(plan.Replace('_', ' '));
        }

        // ------------------------------------------------------------ JSON → model

        static Window ParseWindow(object o, DateTime observed)
        {
            if (o == null) return null;
            var used = Json.Num(Json.Get(o, "used_percent") ?? Json.Get(o, "usedPercent"));
            if (!used.HasValue) return null;
            var w = new Window { Used = used.Value };
            w.Minutes = (int)(Json.Num(Json.Get(o, "window_minutes") ?? Json.Get(o, "windowDurationMins") ?? Json.Get(o, "limit_window_seconds")) ?? 0);
            if (Json.Get(o, "limit_window_seconds") != null && Json.Get(o, "window_minutes") == null) w.Minutes /= 60;
            var ra = Json.Get(o, "resets_at") ?? Json.Get(o, "resetsAt") ?? Json.Get(o, "reset_at");
            if (ra != null) w.ResetsAt = Json.Date(ra);
            else
            {
                var rin = Json.Num(Json.Get(o, "resets_in_seconds") ?? Json.Get(o, "reset_after_seconds"));
                if (rin.HasValue) w.ResetsAt = observed.AddSeconds(rin.Value);
            }
            return w;
        }

        static Bucket ParseBucket(object o, DateTime observed)
        {
            var b = new Bucket
            {
                Id = Json.Str(Json.Get(o, "limit_id") ?? Json.Get(o, "limitId")) ?? "codex",
                Name = Json.Str(Json.Get(o, "limit_name") ?? Json.Get(o, "limitName")),
                Primary = ParseWindow(Json.Get(o, "primary") ?? Json.Get(o, "primary_window"), observed),
                Secondary = ParseWindow(Json.Get(o, "secondary") ?? Json.Get(o, "secondary_window"), observed),
            };
            return (b.Primary == null && b.Secondary == null) ? null : b;
        }

        static void FillExtras(RateSnap rs, object o)
        {
            if (rs.Plan == null) rs.Plan = Json.Str(Json.Get(o, "plan_type") ?? Json.Get(o, "planType"));
            var credits = Json.Get(o, "credits");
            if (credits != null && rs.Credits == null)
            {
                bool has = Json.Bool(Json.Get(credits, "has_credits") ?? Json.Get(credits, "hasCredits")) ?? false;
                bool unl = Json.Bool(Json.Get(credits, "unlimited")) ?? false;
                string bal = Json.Str(Json.Get(credits, "balance"));
                if (unl) rs.Credits = "額外點數：無限";
                else if (has && !string.IsNullOrEmpty(bal) && bal != "0") rs.Credits = "額外點數：" + bal;
            }
            var reached = Json.Str(Json.Get(o, "rate_limit_reached_type") ?? Json.Get(o, "rateLimitReachedType"));
            if (!string.IsNullOrEmpty(reached) && rs.Reached == null) rs.Reached = reached.Replace('_', ' ');
        }

        // ------------------------------------------------------------ session logs

        RateSnap ScanLogs()
        {
            string root = Path.Combine(CodexHome(), "sessions");
            if (!Directory.Exists(root)) return null;
            var cutoff = DateTime.UtcNow.AddDays(-10);
            var files = new DirectoryInfo(root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .Where(f => f.LastWriteTimeUtc > cutoff)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(8)
                .ToList();
            foreach (var hot in hotFiles.Keys.ToList())
            {
                bool dummy;
                hotFiles.TryRemove(hot, out dummy);
                if (File.Exists(hot) && !files.Any(f => string.Equals(f.FullName, hot, StringComparison.OrdinalIgnoreCase)))
                    files.Add(new FileInfo(hot));
            }
            RateSnap best = null;
            foreach (var f in files)
            {
                RateSnap r = null;
                try { r = TailScan(f); } catch (Exception ex) { Log.Warn("codex tail scan " + f.Name + ": " + ex.Message); }
                if (r != null && (best == null || r.ObservedAt > best.ObservedAt)) best = r;
            }
            return best;
        }

        RateSnap TailScan(FileInfo f)
        {
            f.Refresh();
            if (!f.Exists) return null;
            TailCache c;
            if (tailCache.TryGetValue(f.FullName, out c) && c.Length == f.Length && c.Mtime == f.LastWriteTimeUtc) return c.Result;

            long len = f.Length;
            const long chunk = 512 * 1024;
            const long maxRead = 6 * 1024 * 1024;
            long start = Math.Max(0, len - chunk);
            RateSnap result;
            while (true)
            {
                string text = ReadRange(f.FullName, start, len - start);
                result = FindLastRateLimits(text, start > 0);
                if (result != null || start == 0 || len - start >= maxRead) break;
                start = Math.Max(0, start - chunk * 3);
            }
            tailCache[f.FullName] = new TailCache { Length = len, Mtime = f.LastWriteTimeUtc, Result = result };
            return result;
        }

        static string ReadRange(string path, long start, long count)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(start, SeekOrigin.Begin);
                var buf = new byte[count];
                int read = 0;
                while (read < count)
                {
                    int n = fs.Read(buf, read, (int)(count - read));
                    if (n <= 0) break;
                    read += n;
                }
                return Encoding.UTF8.GetString(buf, 0, read);
            }
        }

        static RateSnap FindLastRateLimits(string text, bool firstLinePartial)
        {
            var lines = text.Split('\n');
            int stop = firstLinePartial ? 1 : 0;
            RateSnap rs = null;
            var seen = new HashSet<string>();
            int parsed = 0;
            for (int i = lines.Length - 1; i >= stop && parsed < 200; i--)
            {
                string line = lines[i];
                if (line.IndexOf("\"rate_limits\"", StringComparison.Ordinal) < 0) continue;
                if (line.IndexOf("\"rate_limits\":{", StringComparison.Ordinal) < 0 &&
                    line.IndexOf("\"rate_limits\": {", StringComparison.Ordinal) < 0) continue;
                parsed++;
                var o = Json.TryParse(line.Trim());
                if (o == null) continue;
                var payload = Json.Get(o, "payload") ?? o;
                var rl = Json.Get(payload, "rate_limits");
                if (rl == null) continue;
                var ts = Json.Date(Json.Get(o, "timestamp")) ?? DateTime.UtcNow;
                var b = ParseBucket(rl, ts);
                if (b == null || seen.Contains(b.Id)) continue;
                seen.Add(b.Id);
                if (rs == null) rs = new RateSnap { ObservedAt = ts, Source = "Codex 本機紀錄" };
                rs.Buckets.Add(b);
                FillExtras(rs, rl);
                if (seen.Count >= 4) break;
            }
            return rs;
        }

        // ------------------------------------------------------------ app-server

        string FindCodexExe()
        {
            if (ExeOverride != null) return ExeOverride;
            if (codexExe != null && (DateTime.UtcNow - codexExeCheckedAt).TotalMinutes < 30 && File.Exists(codexExe)) return codexExe;
            codexExeCheckedAt = DateTime.UtcNow;
            var cands = new List<FileInfo>();
            foreach (var p in AppPaths.Glob(@"%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe")) cands.Add(new FileInfo(p));
            foreach (var ext in AppPaths.EditorExtensions("openai.chatgpt-"))
            {
                var p = Path.Combine(ext, "bin", "windows-x86_64", "codex.exe");
                if (File.Exists(p)) cands.Add(new FileInfo(p));
            }
            var best = cands.Where(f => f.Exists).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            codexExe = best != null ? best.FullName : AppPaths.Which("codex");
            return codexExe;
        }

        RateSnap QueryLive(out string error)
        {
            error = null;
            string exe = FindCodexExe();
            if (exe == null) { error = "找不到 codex 執行檔"; return null; }
            Process p = null;
            try
            {
                var psi = new ProcessStartInfo();
                if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                {
                    psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                    psi.Arguments = "/d /s /c \"\"" + exe + "\" app-server\"";
                }
                else
                {
                    psi.FileName = exe;
                    psi.Arguments = ArgsOverride ?? "app-server";
                }
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = new UTF8Encoding(false);
                psi.StandardErrorEncoding = new UTF8Encoding(false);
                psi.WorkingDirectory = Path.GetTempPath();
                p = Process.Start(psi);
                p.ErrorDataReceived += delegate { };
                p.BeginErrorReadLine();

                var lines = new BlockingCollection<string>();
                var proc = p;
                var reader = new Thread(() =>
                {
                    try
                    {
                        string l;
                        while ((l = proc.StandardOutput.ReadLine()) != null) lines.Add(l);
                    }
                    catch { }
                    finally { lines.CompleteAdding(); }
                });
                reader.IsBackground = true;
                reader.Start();

                // Process.StandardInput may already have written a BOM; an empty first line keeps it out of our JSON.
                var input = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false));
                input.NewLine = "\n";
                input.AutoFlush = true;
                input.WriteLine();
                input.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"sentripet\",\"title\":\"SentriPet\",\"version\":\"" + App.Version + "\"}}}");
                var init = WaitFor(lines, 1, 20000);
                if (init == null) { error = "codex app-server 沒有回應"; return null; }
                if (Json.Get(init, "error") != null) { error = ErrorText(init); return null; }

                input.WriteLine("{\"method\":\"initialized\"}");
                input.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{\"excludeResetCreditDetails\":true}}");
                var resp = WaitFor(lines, 2, 25000);
                if (resp == null) { error = "查詢逾時"; return null; }
                if (Json.Get(resp, "error") != null) { error = ErrorText(resp); return null; }

                var result = Json.Get(resp, "result");
                var rs = new RateSnap { ObservedAt = DateTime.UtcNow, Source = "Codex 官方 app-server" };
                var byId = Json.Obj(Json.Get(result, "rateLimitsByLimitId"));
                if (byId != null && byId.Count > 0)
                {
                    foreach (var kv in byId)
                    {
                        var b = ParseBucket(kv.Value, rs.ObservedAt);
                        if (b != null) { if (b.Id == null) b.Id = kv.Key; rs.Buckets.Add(b); }
                        FillExtras(rs, kv.Value);
                    }
                }
                else
                {
                    var single = Json.Get(result, "rateLimits");
                    var b = ParseBucket(single, rs.ObservedAt);
                    if (b != null) rs.Buckets.Add(b);
                    FillExtras(rs, single);
                }
                if (rs.Buckets.Count == 0) { error = "帳號沒有回傳用量（可能未用 ChatGPT 登入 Codex）"; return null; }
                return rs;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
            finally
            {
                if (p != null)
                {
                    try { p.StandardInput.Close(); } catch { }
                    try
                    {
                        if (!p.WaitForExit(3000)) KillTree(p.Id);
                    }
                    catch { }
                    p.Dispose();
                }
            }
        }

        static object WaitFor(BlockingCollection<string> lines, int id, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                string l;
                if (!lines.TryTake(out l, 500))
                {
                    if (lines.IsCompleted) return null;
                    continue;
                }
                var o = Json.TryParse(l);
                if (o == null || Json.Get(o, "method") != null) continue;
                var oid = Json.Num(Json.Get(o, "id"));
                if (oid.HasValue && (int)oid.Value == id) return o;
            }
            return null;
        }

        static string ErrorText(object resp)
        {
            var e = Json.Get(resp, "error");
            return Json.Str(Json.Get(e, "message")) ?? "未知錯誤";
        }

        static void KillTree(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F") { CreateNoWindow = true, UseShellExecute = false };
                using (var k = Process.Start(psi)) k.WaitForExit(3000);
            }
            catch { }
        }

        public override void Dispose()
        {
            if (activity != null) activity.Dispose();
        }
    }
}
