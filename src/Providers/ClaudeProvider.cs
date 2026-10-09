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
    /// Reset times are not in that file, so they are estimated from when the numbers dropped back to zero —
    /// unless the Claude Code status-line bridge is on (<see cref="ClaudeStatusLine"/>): its official numbers and reset
    /// times win whenever they are newer, and they are the only source where there is no desktop app (Linux).
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
        readonly ClaudeCodeUsage usage;
        readonly Account account;   // another account (#26): only its folder's status line and transcripts
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
            usage = new ClaudeCodeUsage();
        }

        /// <summary>Another Claude Code account (#26): its CLAUDE_CONFIG_DIR folder.</summary>
        public ClaudeProvider(Account a) : this()
        {
            account = a;
            Id = a.Id;
            Name = a.Name;
            Rgba c;
            if (a.Color != null && Rgba.TryParse(a.Color, out c)) Color = c;
            usage = new ClaudeCodeUsage(Path.Combine(a.Home, "projects"));
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
            if (account != null)
            {
                if (Directory.Exists(account.Home)) d.Evidence.Add(L.F("Claude Code 資料夾 {0}", AppPaths.ShortPath(account.Home)));
                d.Installed = d.Evidence.Count > 0;
                if (!d.Installed) d.Hint = L.T("找不到這個資料夾");
                else if (!account.StatusLine) d.Hint = L.T("在設定頁連接這個帳號的 Claude Code 狀態列，才讀得到它的用量");
                return d;
            }
            if (Directory.Exists(Path.Combine(AppPaths.AppData, "Claude")) || AppPaths.MsixInstalled("Claude"))
                d.Evidence.Add(L.T("Claude 桌面版"));
            if (Directory.Exists(Path.Combine(AppPaths.Home, ".claude"))) d.Evidence.Add("Claude Code");
            if (AppPaths.EditorExtensions("anthropic.claude-code").Count > 0) d.Evidence.Add(L.T("VS Code 擴充"));
            if (AppPaths.Which("claude") != null) d.Evidence.Add(L.T("claude 指令"));
            d.Installed = d.Evidence.Count > 0;
            if (FindHistory() == null)
                d.Hint = Os.Linux ? L.T("在 設定 → AI 服務 開啟「連接 Claude Code 狀態列」就能讀到 Claude 的用量") : L.T("開啟 Claude 桌面版後就會開始記錄用量");
            return d;
        }

        public override Snapshot Fetch(bool force, AppSettings settings)
        {
            if (activity == null) activity = new ActivityWatcher(account != null ? Path.Combine(account.Home, "projects") : ClaudeCodeUsage.Root, "*.jsonl");
            activity.Ensure();

            var now = DateTime.UtcNow;
            if (account != null) return FetchAccount(settings, now);
            // official numbers from Claude Code's status line (when the bridge is on), ignored once a week old
            var bridge = settings.ClaudeStatusBridge ? ClaudeStatusLine.Load() : null;
            if (bridge != null && (now - bridge.ObservedAt).TotalDays > 7) bridge = null;
            bool estimate = settings.ClaudeEstimate;
            if (estimate)
            {
                try { usage.Update(); }
                catch (Exception ex) { Log.Warn("claude transcripts: " + ex.Message); }
            }
            var events = estimate ? usage : null;

            string f = FindHistory();
            if (f == null)
            {
                if (bridge != null) return FromBridge(bridge, events, now);
                return Snapshot.Fail(Os.Linux ? L.T("讀不到 Claude 用量：請在 設定 → AI 服務 開啟「連接 Claude Code 狀態列」")
                                              : L.T("找不到用量紀錄：請開啟 Claude 桌面版"));
            }
            var fi = new FileInfo(f);
            if (cachedSamples == null || f != cachedFile || fi.LastWriteTimeUtc != cachedMtime || fi.Length != cachedLen)
            {
                cachedSamples = ParseHistory(AppPaths.ReadShared(f));
                cachedFile = f;
                cachedMtime = fi.LastWriteTimeUtc;
                cachedLen = fi.Length;
            }
            var samples = cachedSamples;
            if (samples.Count == 0)
            {
                if (bridge != null) return FromBridge(bridge, events, now);
                return Snapshot.Fail(L.T("用量紀錄是空的：請開啟 Claude 桌面版"));
            }

            var last = samples[samples.Count - 1];
            var lastUtc = Json.FromUnix(last.T);
            bool officialReset = false, officialUsed = false;
            var snap = new Snapshot();
            snap.ObservedAt = lastUtc;
            snap.Source = L.T("Claude 桌面版快取");
            bool estimated = false, uncalibrated = false;
            bool startFromReply = false, weeklyUnknown = false;   // (#31) for the hints below

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

                // the status line's official numbers: the reset time always, the percentage when it is the newer reading
                DateTime baseUtc = lastUtc;
                var bw = bridge != null ? bridge.For(key) : null;
                if (bw != null)
                {
                    if (bw.ResetsAt > now)
                    {
                        m.ResetsAt = bw.ResetsAt;
                        m.ResetApprox = false;
                        officialReset = true;
                        if (bridge.ObservedAt >= lastUtc)
                        {
                            m.Used = bw.Used;
                            baseUtc = bridge.ObservedAt;
                            officialUsed = true;
                        }
                    }
                    else if (lastUtc < bw.ResetsAt)
                    {
                        // that window ended after both readings: handled as a reset just below
                        m.ResetsAt = bw.ResetsAt;
                    }
                    else if (win >= 1440)
                    {
                        // the desktop app already saw the new week: it ends a week after the official reset
                        var next = bw.ResetsAt;
                        while (next <= now) next = next.AddMinutes(win);
                        m.ResetsAt = next;
                        m.ResetApprox = false;
                        officialReset = true;
                    }
                }

                // usage recorded by Claude Code after this moment is added on top of the latest reading
                DateTime extrapolateFrom = baseUtc;
                if (m.ResetsAt.HasValue && m.ResetsAt.Value <= now && baseUtc < m.ResetsAt.Value)
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
                        // a new 5-hour window opens with the first request after the old one ended (and so on): only
                        // the one running now counts
                        DateTime opened;
                        var start = CurrentWindow(events, ended, now, win, out opened);
                        m.ResetsAt = start.HasValue ? start.Value.AddMinutes(win) : (DateTime?)null;
                        extrapolateFrom = start.HasValue ? opened.AddTicks(-1) : now;
                        m.ResetApprox = true;
                        if (start.HasValue) startFromReply = true;
                    }
                }
                else if (win < 1440 && m.Used <= 0 && events != null && m.ResetApprox)
                {
                    // idle (or just reset) at the last sample: the first request since then opens a window, and when that
                    // one is over too, the next request the next one (user report: "about to reset" all afternoon, 86% while 22% was used)
                    DateTime opened;
                    var start = CurrentWindow(events, baseUtc, now, win, out opened);
                    if (start.HasValue)
                    {
                        m.ResetsAt = start.Value.AddMinutes(win);
                        extrapolateFrom = opened.AddTicks(-1);
                        m.WasReset = FloorToMinute(events.FirstAfter(baseUtc, now).Value) != start.Value;   // an earlier window ran out
                        startFromReply = true;
                    }
                    else if (events.FirstAfter(baseUtc, now).HasValue)
                    {
                        // windows opened and ended since: nothing is counting down now
                        m.ResetsAt = null;
                        m.WasReset = true;
                        extrapolateFrom = now;
                    }
                }

                double? k = events != null ? Calibrate(samples, key, win) : null;
                if (win < 1440) LastK5 = k; else if (key == "sd") LastK7 = k;
                if (events != null && !k.HasValue) uncalibrated = true;
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
                if (win >= 1440 && !m.ResetsAt.HasValue) weeklyUnknown = true;
                snap.Meters.Add(m);
            }

            // when the desktop app wrote that sample: the time today, with the day otherwise
            var lastLocal = lastUtc.ToLocalTime();
            string baseTime = lastLocal.ToString(lastLocal.Date == DateTime.Now.Date ? "HH:mm" : "M/d HH:mm");
            if (officialUsed)
            {
                snap.ObservedAt = bridge.ObservedAt;
                snap.Source = L.T("Claude Code 狀態列（官方）");
            }
            bool unexplained = uncalibrated && events.Sum(lastUtc, now) > 0;   // Claude Code was used, but a window can't be estimated
            if (estimated && !unexplained)
            {
                snap.ObservedAt = now;
                snap.Source = L.T("即時推算");
                snap.Note = officialUsed
                    ? L.F("以 Claude Code 狀態列 {0} 的官方數字為基準，加上之後用掉的 token 推算（≈）", bridge.ObservedAt.ToLocalTime().ToString("HH:mm"))
                    : L.F("以桌面版 {0} 記錄的數字為基準，加上之後 Claude Code 用掉的 token 推算（≈）", baseTime);
            }
            else if (officialUsed)
                snap.Note = L.F("官方數字，來自 Claude Code 狀態列（{0}）；只有用 Claude Code 時才會更新", bridge.ObservedAt.ToLocalTime().ToString("M/d HH:mm"));
            else if (unexplained)
            {
                // Claude Code was used since, but there is nothing to turn its tokens into a percentage with yet
                snap.Stale = true;
                snap.Note = L.F("桌面版最近一次記錄是 {0}；之後用了 Claude Code，但還沒有足夠的紀錄可以推算，要等桌面版再記錄一次（它不定時才記錄）", baseTime);
            }
            else
            {
                snap.Stale = (now - lastUtc).TotalHours > 12;
                snap.Note = officialReset ? L.F("桌面版 {0} 記錄的數字；重置時間是 Claude Code 狀態列的官方時間", baseTime)
                                          : L.F("桌面版 {0} 記錄的數字；重置時間為推算值", baseTime);
            }

            // (#31) what the numbers can't see, and what the user can do about it
            var hints = new List<string>();
            if (unexplained && !settings.ClaudeStatusBridge && AppPaths.Which("claude") != null)
                hints.Add(L.T("在終端機用 claude 的話，在 設定 → AI 服務 開啟「連接 Claude Code 狀態列」就能看到官方數字"));   // (only the terminal's claude runs it)
            if (startFromReply) hints.Add(L.T("5 小時從 Claude Code 第一則回覆起算，聊天或網頁的用量看不到，實際可能更早重置"));
            if (weeklyUnknown) hints.Add(L.T("每週重置時間推算不出來，可以在 設定 → AI 服務 填一次，例如「週四 23:00」"));
            if (hints.Count > 0) snap.Note = string.Join(L.T("；"), new[] { snap.Note }.Concat(hints));
            snap.Active = activity.ActiveWithin(90);
            return snap;
        }

        /// <summary>
        /// Another account (#26): the Claude desktop app only records the account it is signed in to, so the numbers come
        /// from the status line connected in this account's folder (and its transcripts fill the gap after a reset).
        /// </summary>
        Snapshot FetchAccount(AppSettings settings, DateTime now)
        {
            var bridge = account.StatusLine ? ClaudeStatusLine.Load(ClaudeStatusLine.DataFileFor(account.Home)) : null;
            if (bridge != null && (now - bridge.ObservedAt).TotalDays > 7) bridge = null;
            if (bridge == null)
                return Snapshot.Fail(account.StatusLine ? L.T("還沒有這個帳號的用量：用這個帳號的 Claude Code 問一句話就會出現")
                                                        : L.T("在設定頁連接這個帳號的 Claude Code 狀態列，才讀得到它的用量"));
            if (settings.ClaudeEstimate)
            {
                try { usage.Update(); }
                catch (Exception ex) { Log.Warn("claude transcripts " + account.Id + ": " + ex.Message); }
            }
            return FromBridge(bridge, settings.ClaudeEstimate ? usage : null, now);
        }

        /// <summary>Only the status line's numbers (no desktop app, e.g. Linux).</summary>
        Snapshot FromBridge(ClaudeStatusLine.Data bridge, ClaudeCodeUsage events, DateTime now)
        {
            var snap = new Snapshot { Source = L.T("Claude Code 狀態列（官方）"), ObservedAt = bridge.ObservedAt };
            foreach (var key in new[] { "fh", "sd" })
            {
                var bw = bridge.For(key);
                if (bw == null) continue;
                int win = WindowFor(key);
                var m = new Meter
                {
                    Key = key,
                    WindowMinutes = win,
                    Used = bw.Used,
                    Label = LabelFor(key, win),
                    ShortLabel = ShortFor(key, win),
                    ResetsAt = bw.ResetsAt,
                    ResetApprox = false,
                };
                if (bw.ResetsAt <= now)
                {
                    // the window ended since Claude Code last reported it
                    m.Used = 0;
                    m.WasReset = true;
                    if (win >= 1440)
                        while (m.ResetsAt.Value <= now) m.ResetsAt = m.ResetsAt.Value.AddMinutes(win);
                    else
                    {
                        DateTime opened;
                        var start = CurrentWindow(events, bw.ResetsAt, now, win, out opened);
                        m.ResetsAt = start.HasValue ? start.Value.AddMinutes(win) : (DateTime?)null;
                        m.ResetApprox = true;
                    }
                }
                snap.Meters.Add(m);
            }
            snap.Note = L.F("官方數字，來自 Claude Code 狀態列（{0}）；只有用 Claude Code 時才會更新", bridge.ObservedAt.ToLocalTime().ToString("M/d HH:mm"));
            if ((now - bridge.ObservedAt).TotalHours > 6) snap.Stale = true;
            snap.Active = activity.ActiveWithin(90);
            return snap;
        }

        /// <summary>How far back the calibration looks (as long as ClaudeCodeUsage keeps the transcripts).</summary>
        internal const int CalibrationDays = 8;

        /// <summary>
        /// %-points per weighted Claude Code token, fitted on the last 8 days: for consecutive desktop samples
        /// in the same window, the rise in the percentage against the tokens used in between. A 5-hour window needs
        /// samples less than 3 hours apart; a weekly one can use samples up to a day apart — recent versions of the
        /// desktop app write a sample only now and then (mostly when it starts), not every 15 minutes any more.
        /// When there is nothing new to fit on, the last fit is used (kept in claude-calibration.json): without it
        /// the estimate stopped, and the pet showed the last desktop sample for hours.
        /// </summary>
        double? Calibrate(List<Sample> ss, string key, int window)
        {
            long cutoff = Json.ToUnixMs(DateTime.UtcNow.AddDays(-CalibrationDays));
            long maxGap = (window >= 1440 ? 24 : 3) * 3600 * 1000L;
            double num = 0, den = 0, rise = 0;
            for (int i = 1; i < ss.Count; i++)
            {
                var a = ss[i - 1];
                var b = ss[i];
                if (b.T < cutoff || b.T - a.T > maxGap) continue;
                double va = Val(a, key), vb = Val(b, key);
                if (vb < va || va >= 100 || vb >= 100) continue;   // a reset in between, or capped at the limit
                double w = usage.Sum(Json.FromUnix(a.T), Json.FromUnix(b.T));
                if (w <= 0) continue;
                num += w * (vb - va);
                den += w * w;
                rise += vb - va;
            }
            double k = den > 0 && rise >= 4 ? num / den : 0;   // (not enough evidence yet: 0)
            if (k > 0)
            {
                SaveCalibration(key, k);
                return k;
            }
            return SavedCalibration(key);
        }

        /// <summary>The last calibration per window (fh, sd…), so the estimate goes on when the desktop app writes no new samples.</summary>
        internal static string CalibrationFile { get { return Path.Combine(AppPaths.DataDir, "claude-calibration.json"); } }

        static Dictionary<string, double> savedCalibration;

        static Dictionary<string, double> ReadCalibration()
        {
            if (savedCalibration != null) return savedCalibration;
            var d = new Dictionary<string, double>();
            try
            {
                if (File.Exists(CalibrationFile))
                {
                    var o = Json.Obj(Json.TryParse(File.ReadAllText(CalibrationFile, System.Text.Encoding.UTF8)));
                    if (o != null)
                        foreach (var kv in o)
                        {
                            double? k = Json.Num(Json.Get(kv.Value, "k"));
                            if (k.HasValue && k.Value > 0) d[kv.Key] = k.Value;
                        }
                }
            }
            catch (Exception ex) { Log.Warn("claude calibration: " + ex.Message); }
            return savedCalibration = d;
        }

        static double? SavedCalibration(string key)
        {
            double k;
            return ReadCalibration().TryGetValue(key, out k) ? k : (double?)null;
        }

        static void SaveCalibration(string key, double k)
        {
            var d = ReadCalibration();
            double old;
            if (d.TryGetValue(key, out old) && Math.Abs(old - k) <= old * 0.01) return;   // (unchanged: not written every few seconds)
            d[key] = k;
            try
            {
                AppPaths.EnsureDataDirs();
                var o = d.ToDictionary(kv => kv.Key, kv => (object)new Dictionary<string, object> { { "k", kv.Value }, { "at", DateTime.UtcNow } });
                File.WriteAllText(CalibrationFile, Json.Serialize(o, true), new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Warn("claude calibration: " + ex.Message); }
        }

        /// <summary>Forgets the calibration read from the file (tests).</summary>
        internal static void ForgetCalibration() { savedCalibration = null; }

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
                case "fh": return L.T("5 小時");
                case "sd": return L.T("每週");
                case "so": return L.T("每週 Opus");
                case "ss": return L.T("每週 Sonnet");
                default: return Fmt.WindowLabel(win) + " (" + key + ")";
            }
        }

        static string ShortFor(string key, int win)
        {
            switch (key)
            {
                case "fh": return "5h";
                case "sd": return L.T("週");
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
        /// The 5-hour window running now, once the one known at <paramref name="from"/> is over: each window opens with the
        /// first Claude Code reply after the previous one ended, so a busy day chains one window after another. Returns
        /// the current window's start (to the minute), or null when nothing was asked since the last one ended.
        /// <paramref name="opened"/> is the reply that opened it: its usage counts from just before that reply, as the
        /// start is cut to the minute and a reply right on the minute would otherwise be left out.
        /// </summary>
        internal static DateTime? CurrentWindow(ClaudeCodeUsage events, DateTime from, DateTime now, int windowMin, out DateTime opened)
        {
            opened = default(DateTime);
            if (events == null) return null;
            var first = events.FirstAfter(from, now);
            while (first.HasValue)
            {
                var start = FloorToMinute(first.Value);
                var end = start.AddMinutes(windowMin);
                if (end > now) { opened = first.Value; return start; }
                first = events.FirstAfter(end, now);
            }
            return null;
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

        // i18n-ignore (weekday names in every language we speak)
        static readonly Regex WeeklyRx = new Regex(@"^\s*(?:週|周|星期|禮拜|礼拜)?\s*(?<d>[日天一二三四五六月火水木金土일월화수목금토]|sun|mon|tue|wed|thu|fri|sat)(?:曜日|曜|요일)?[a-z]*\.?\s+(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?\s*$", RegexOptions.IgnoreCase); // i18n-ignore

        /// <summary>Parses "Thu 23:00", "週四 23:00", "木曜日 23:00", "목요일 23:00", "fri 4pm" into the next such moment (UTC).</summary>
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
                case "日": case "天": case "일": case "sun": dow = 0; break; // i18n-ignore
                case "一": case "月": case "월": case "mon": dow = 1; break; // i18n-ignore
                case "二": case "火": case "화": case "tue": dow = 2; break; // i18n-ignore
                case "三": case "水": case "수": case "wed": dow = 3; break; // i18n-ignore
                case "四": case "木": case "목": case "thu": dow = 4; break; // i18n-ignore
                case "五": case "金": case "금": case "fri": dow = 5; break; // i18n-ignore
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
