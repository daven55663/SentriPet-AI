using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SentriPet
{
    /// <summary>
    /// Claude plan usage. Reads the history file the Claude desktop app keeps (plan-usage-history.json,
    /// a sample every ~15 min with "fh" = 5-hour % and "sd" = 7-day %). No credentials involved.
    /// Reset times are not in that file, so they are estimated from when the numbers dropped back to zero.
    /// </summary>
    class ClaudeProvider : Provider
    {
        internal class Sample
        {
            public long T;
            public string Org;
            public Dictionary<string, double> U = new Dictionary<string, double>();
        }

        /// <summary>Set by the self-test to read a sample history file instead of the desktop app's.</summary>
        internal static string HistoryOverride;

        ActivityWatcher activity;
        readonly ClaudeCodeUsage usage = new ClaudeCodeUsage();
        string cachedFile;
        DateTime cachedMtime;
        long cachedLen;
        List<Sample> cachedSamples;

        /// <summary>Last calibration, for the probe report: %-points per weighted token.</summary>
        public double? LastK5, LastK7;

        public ClaudeProvider()
        {
            Id = "claude";
            Name = "Claude";
            Mascot = "sparkle";
            Color = Rgba.Hex("#D97757");
        }

        public override int IntervalSeconds { get { return 10; } }

        static IEnumerable<string> HistoryCandidates()
        {
            // %APPDATA%\Claude on Windows, ~/Library/Application Support/Claude on macOS, ~/.config/Claude on Linux
            yield return Path.Combine(AppPaths.AppData, "Claude", "plan-usage-history.json");
            if (!Os.Windows) yield break;
            foreach (var p in AppPaths.Glob(@"%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\plan-usage-history.json"))
                yield return p;
        }

        static string FindHistory()
        {
            if (HistoryOverride != null) return File.Exists(HistoryOverride) ? HistoryOverride : null;
            string best = null;
            DateTime bt = DateTime.MinValue;
            foreach (var c in HistoryCandidates())
            {
                try
                {
                    var fi = new FileInfo(c);
                    if (fi.Exists && fi.LastWriteTimeUtc > bt) { best = c; bt = fi.LastWriteTimeUtc; }
                }
                catch { }
            }
            return best;
        }

        public override Detection Detect()
        {
            var d = new Detection();
            if (Directory.Exists(Path.Combine(AppPaths.AppData, "Claude")) || AppPaths.MsixInstalled("Claude"))
                d.Evidence.Add("Claude 桌面版");
            if (Directory.Exists(Path.Combine(AppPaths.Home, ".claude"))) d.Evidence.Add("Claude Code");
            if (AppPaths.EditorExtensions("anthropic.claude-code").Count > 0) d.Evidence.Add("VS Code 擴充");
            if (AppPaths.Which("claude") != null) d.Evidence.Add("claude 指令");
            d.Installed = d.Evidence.Count > 0;
            if (FindHistory() == null)
                d.Hint = Os.Linux ? "Claude 桌面版沒有 Linux 版，目前讀不到方案用量（之後會改讀 Claude Code 狀態列）" : "開啟 Claude 桌面版後就會開始記錄用量";
            return d;
        }

        public override Snapshot Fetch(bool force, AppSettings settings)
        {
            if (activity == null) activity = new ActivityWatcher(ClaudeCodeUsage.Root, "*.jsonl");
            activity.Ensure();

            string f = FindHistory();
            if (f == null) return Snapshot.Fail("找不到用量紀錄：請開啟 Claude 桌面版");
            var fi = new FileInfo(f);
            if (cachedSamples == null || f != cachedFile || fi.LastWriteTimeUtc != cachedMtime || fi.Length != cachedLen)
            {
                cachedSamples = ParseHistory(AppPaths.ReadShared(f));
                cachedFile = f;
                cachedMtime = fi.LastWriteTimeUtc;
                cachedLen = fi.Length;
            }
            var samples = cachedSamples;
            if (samples.Count == 0) return Snapshot.Fail("用量紀錄是空的：請開啟 Claude 桌面版");

            var last = samples[samples.Count - 1];
            var now = DateTime.UtcNow;
            var lastUtc = Json.FromUnix(last.T);
            bool estimate = settings.ClaudeEstimate;
            if (estimate)
            {
                try { usage.Update(); }
                catch (Exception ex) { Log.Warn("claude transcripts: " + ex.Message); }
            }
            var events = estimate ? usage : null;
            var snap = new Snapshot();
            snap.ObservedAt = lastUtc;
            snap.Source = "Claude 桌面版快取";
            bool estimated = false;

            foreach (var key in OrderedKeys(last.U.Keys))
            {
                int win = WindowFor(key);
                var m = new Meter
                {
                    Key = key,
                    WindowMinutes = win,
                    Used = Math.Max(0, Math.Min(100, last.U[key])),
                    Label = LabelFor(key, win),
                    ShortLabel = ShortFor(key, win),
                    ResetApprox = true,
                };
                DateTime? manual;
                if (win >= 1440 && TryParseWeekly(settings.ClaudeWeeklyReset, out manual))
                {
                    m.ResetsAt = manual;
                    m.ResetApprox = false;
                }
                else if (win >= 1440)
                {
                    // weekly limits reset on the hour
                    m.ResetsAt = RoundToHour(EstimateFixedSchedule(samples, key, win) ?? EstimateFromFirstUse(samples, key, win, events));
                }
                else
                {
                    m.ResetsAt = EstimateFromFirstUse(samples, key, win, events);
                }

                // usage recorded by Claude Code after this moment is added on top of the desktop app's number
                DateTime extrapolateFrom = lastUtc;
                if (m.ResetsAt.HasValue && m.ResetsAt.Value <= now && lastUtc < m.ResetsAt.Value)
                {
                    // the window ended after the last sample was written
                    var ended = m.ResetsAt.Value;
                    m.Used = 0;
                    m.WasReset = true;
                    extrapolateFrom = ended;
                    if (win >= 1440)
                        while (m.ResetsAt.Value <= now) m.ResetsAt = m.ResetsAt.Value.AddMinutes(win);
                    else
                    {
                        // a new 5-hour window opens with the first request after the old one ended
                        var start = events != null ? events.FirstAfter(ended, now) : null;
                        m.ResetsAt = start.HasValue ? FloorToMinute(start.Value).AddMinutes(win) : (DateTime?)null;
                    }
                }
                else if (win < 1440 && m.Used <= 0 && events != null)
                {
                    // idle (or just reset) at the last sample: the first request since then opens the window
                    var start = events.FirstAfter(lastUtc, now);
                    if (start.HasValue) m.ResetsAt = FloorToMinute(start.Value).AddMinutes(win);
                }

                double? k = events != null ? Calibrate(samples, key) : null;
                if (win < 1440) LastK5 = k; else if (key == "sd") LastK7 = k;
                if (k.HasValue)
                {
                    double extra = events.Sum(extrapolateFrom, now) * k.Value;
                    if (extra >= 0.1)
                    {
                        m.Used = Math.Min(100, m.Used + extra);
                        m.UsedApprox = true;
                        estimated = true;
                    }
                }
                snap.Meters.Add(m);
            }

            string baseTime = lastUtc.ToLocalTime().ToString("HH:mm");
            if (estimated)
            {
                snap.ObservedAt = now;
                snap.Source = "即時推算";
                snap.Note = "以桌面版 " + baseTime + " 的數字為基準，加上之後 Claude Code 用掉的 token 推算（≈）；桌面版約每 15 分鐘校正一次";
            }
            if ((now - lastUtc).TotalMinutes > 35)
            {
                snap.Stale = true;
                snap.Note = "Claude 桌面版沒在執行，基準停在 " + lastUtc.ToLocalTime().ToString("M/d HH:mm") + (estimated ? "，之後的用量為推算" : "");
            }
            else if (!estimated)
            {
                snap.Note = "桌面版約每 15 分鐘更新一次（這次是 " + baseTime + "）；重置時間為推算值";
            }
            snap.Active = activity.ActiveWithin(90);
            return snap;
        }

        /// <summary>
        /// %-points per weighted Claude Code token, fitted on the last 3 days: for consecutive desktop samples
        /// in the same window, the rise in the percentage against the tokens used in between.
        /// </summary>
        double? Calibrate(List<Sample> ss, string key)
        {
            long cutoff = Json.ToUnixMs(DateTime.UtcNow.AddDays(-3));
            double num = 0, den = 0, rise = 0;
            for (int i = 1; i < ss.Count; i++)
            {
                var a = ss[i - 1];
                var b = ss[i];
                if (b.T < cutoff || b.T - a.T > 3 * 3600 * 1000L) continue;
                double va = Val(a, key), vb = Val(b, key);
                if (vb < va || va >= 100 || vb >= 100) continue;   // a reset in between, or capped at the limit
                double w = usage.Sum(Json.FromUnix(a.T), Json.FromUnix(b.T));
                if (w <= 0) continue;
                num += w * (vb - va);
                den += w * w;
                rise += vb - va;
            }
            if (den <= 0 || rise < 4) return null;   // not enough evidence yet
            double k = num / den;
            return k > 0 ? k : (double?)null;
        }

        internal static DateTime? RoundToHour(DateTime? t)
        {
            if (!t.HasValue) return null;
            var v = t.Value.AddMinutes(30);
            return new DateTime(v.Year, v.Month, v.Day, v.Hour, 0, 0, DateTimeKind.Utc);
        }

        static DateTime FloorToMinute(DateTime t)
        {
            return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, DateTimeKind.Utc);
        }

        // ------------------------------------------------------------------ parsing

        internal static List<Sample> ParseHistory(string text)
        {
            var list = new List<Sample>();
            var root = Json.TryParse(text);
            var arr = Json.Arr(Json.Get(root, "samples"));
            if (arr == null) return list;
            foreach (var o in arr)
            {
                var t = Json.Num(Json.Get(o, "t"));
                var u = Json.Obj(Json.Get(o, "u"));
                if (!t.HasValue || u == null) continue;
                var s = new Sample { T = (long)t.Value, Org = Json.Str(Json.Get(o, "org")) };
                foreach (var kv in u)
                {
                    var n = Json.Num(kv.Value);
                    if (n.HasValue) s.U[kv.Key] = n.Value;
                }
                if (s.U.Count > 0) list.Add(s);
            }
            list.Sort((a, b) => a.T.CompareTo(b.T));
            if (list.Count > 0)
            {
                string org = list[list.Count - 1].Org;
                if (org != null) list = list.Where(x => x.Org == null || x.Org == org).ToList();
            }
            return list;
        }

        static IEnumerable<string> OrderedKeys(IEnumerable<string> keys)
        {
            var order = new[] { "fh", "sd", "so", "ss" };
            return keys.OrderBy(k => { int i = Array.IndexOf(order, k); return i < 0 ? 99 : i; }).ThenBy(k => k);
        }

        static int WindowFor(string key)
        {
            if (key == "fh" || key.StartsWith("f")) return 300;
            return 10080;
        }

        static string LabelFor(string key, int win)
        {
            switch (key)
            {
                case "fh": return "5 小時";
                case "sd": return "每週";
                case "so": return "每週 Opus";
                case "ss": return "每週 Sonnet";
                default: return Fmt.WindowLabel(win) + " (" + key + ")";
            }
        }

        static string ShortFor(string key, int win)
        {
            switch (key)
            {
                case "fh": return "5h";
                case "sd": return "週";
                case "so": return "Op";
                case "ss": return "So";
                default: return Fmt.WindowShort(win);
            }
        }

        static double Val(Sample s, string key)
        {
            double v;
            return s.U.TryGetValue(key, out v) ? v : 0;
        }

        /// <summary>
        /// Rolling window that starts with the first request after the previous one ended (the 5-hour limit).
        /// The start lies between the last zero sample and the first non-zero one; when Claude Code replies are
        /// known, the first one in that interval pins it down to the minute.
        /// </summary>
        internal static DateTime? EstimateFromFirstUse(List<Sample> ss, string key, int windowMin, ClaudeCodeUsage events)
        {
            int n = ss.Count;
            if (n == 0) return null;
            var last = ss[n - 1];
            if (Val(last, key) <= 0) return null;   // idle: nothing is counting down
            long win = windowMin * 60000L;
            int i = n - 1;
            while (i > 0)
            {
                double a = Val(ss[i - 1], key), b = Val(ss[i], key);
                if (a <= 0 || b < a - 0.5) break;
                if (ss[i].T - ss[i - 1].T > win) break;
                i--;
            }
            long hi = ss[i].T;
            long lo = i > 0 ? ss[i - 1].T : hi - 15 * 60000L;
            lo = Math.Max(lo, last.T - win);   // it is still running at the last sample
            if (lo > hi) lo = hi;
            if (events != null)
            {
                var first = events.FirstAfter(Json.FromUnix(lo), Json.FromUnix(hi));
                if (first.HasValue)
                {
                    var r = FloorToMinute(first.Value).AddMinutes(windowMin);
                    if (Json.ToUnixMs(r) > last.T) return r;
                }
            }
            long reset = (lo + hi) / 2 + win;
            if (reset <= last.T) reset = last.T + 60000;
            return Json.FromUnix(reset);
        }

        /// <summary>
        /// Weekly limit that resets at a fixed moment each week: intersect every observed
        /// "dropped back to ~0" interval modulo one period.
        /// </summary>
        internal static DateTime? EstimateFixedSchedule(List<Sample> ss, string key, int windowMin)
        {
            long period = windowMin * 60000L;
            var intervals = new List<long[]>();
            for (int i = 1; i < ss.Count; i++)
            {
                double a = Val(ss[i - 1], key), b = Val(ss[i], key);
                if (a >= 2 && b <= Math.Max(1, a * 0.25))
                {
                    long l = ss[i - 1].T, h = ss[i].T;
                    if (h - l < period / 2) intervals.Add(new[] { l, h });
                }
            }
            if (intervals.Count == 0) return null;
            long L = intervals[intervals.Count - 1][0], H = intervals[intervals.Count - 1][1];
            for (int k = intervals.Count - 2; k >= 0; k--)
            {
                long l = intervals[k][0], h = intervals[k][1];
                long shift = (long)Math.Round((double)(L - l) / period) * period;
                l += shift;
                h += shift;
                long nl = Math.Max(L, l), nh = Math.Min(H, h);
                if (nl <= nh) { L = nl; H = nh; }   // inconsistent observations (e.g. a global reset) are ignored
            }
            long est = (L + H) / 2;
            long lastT = ss[ss.Count - 1].T;
            while (est <= lastT) est += period;
            return Json.FromUnix(est);
        }

        static readonly Regex WeeklyRx = new Regex(@"^\s*(?:週|周|星期|禮拜)?\s*(?<d>[日天一二三四五六]|sun|mon|tue|wed|thu|fri|sat)[a-z]*\.?\s+(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?\s*$", RegexOptions.IgnoreCase);

        /// <summary>Parses "Thu 23:00", "週四 23:00", "fri 4pm" into the next such moment (UTC).</summary>
        public static bool TryParseWeekly(string text, out DateTime? next)
        {
            next = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var m = WeeklyRx.Match(text);
            if (!m.Success) return false;
            string d = m.Groups["d"].Value.ToLowerInvariant();
            int dow;
            switch (d)
            {
                case "日": case "天": case "sun": dow = 0; break;
                case "一": case "mon": dow = 1; break;
                case "二": case "tue": dow = 2; break;
                case "三": case "wed": dow = 3; break;
                case "四": case "thu": dow = 4; break;
                case "五": case "fri": dow = 5; break;
                default: dow = 6; break;
            }
            int h = int.Parse(m.Groups["h"].Value);
            int min = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value) : 0;
            string ap = m.Groups["ap"].Value.ToLowerInvariant();
            if (ap == "pm" && h < 12) h += 12;
            if (ap == "am" && h == 12) h = 0;
            if (h > 23 || min > 59) return false;
            var local = DateTime.Now;
            var cand = local.Date.AddDays((dow - (int)local.DayOfWeek + 7) % 7).AddHours(h).AddMinutes(min);
            if (cand <= local) cand = cand.AddDays(7);
            next = cand.ToUniversalTime();
            return true;
        }

        public override void Dispose()
        {
            if (activity != null) activity.Dispose();
        }
    }
}
