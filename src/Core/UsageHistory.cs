using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>How one weekly or monthly window ended (#16).</summary>
    class WindowResult
    {
        public string Provider, Name, Meter, Label;
        public DateTime EndedAt;      // UTC: the reset
        public double Used;           // % used when last seen before the reset
        public int NudgeLevel;        // highest "use it before it resets" level announced in that window (0 = none)
        public bool SeenToEnd;        // the widget was running until shortly before the reset (otherwise Used may be low)
        public double EmptyHours;     // how long before the reset it was used up (0 = never, or only at the very end) — planning (#23)

        public double Wasted { get { return Math.Max(0, 100 - Used); } }
        public string Key { get { return Provider + "|" + Meter; } }
    }

    /// <summary>
    /// What the meters did lately, and how each weekly/monthly window ended:
    /// - the pace (#15): when a quota runs out if it keeps being used like in the last hour;
    /// - the report (#16): the use of every weekly/monthly window, kept in report.json in the settings folder,
    ///   including windows that ended while the computer was off (with the last number seen before).
    /// </summary>
    class UsageHistory
    {
        /// <summary>Windows this long or longer go into the report.</summary>
        public const int ReportWindowMinutes = 10080;
        const int KeepResults = 26;   // per quota (half a year of weeks)

        class Point { public DateTime At; public double Used; }

        class Track
        {
            public string Name, Label;
            public int WindowMinutes;
            public DateTime? ResetsAt;
            public double LastUsed;
            public DateTime LastSeen;
            public DateTime? EmptySince;   // first seen used up in this window (#23)
            public readonly List<Point> Points = new List<Point>();
        }

        readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();
        readonly List<WindowResult> results = new List<WindowResult>();
        readonly string file;
        bool dirty;
        DateTime lastSave = DateTime.MinValue;

        public UsageHistory(string file)
        {
            this.file = file;
            Load();
        }

        public static string DefaultFile { get { return Path.Combine(AppPaths.DataDir, "report.json"); } }

        /// <summary>Adds a finished window (samples for the settings page snapshot and tests).</summary>
        internal void AddResult(WindowResult r) { results.Add(r); }

        /// <summary>Finished windows, oldest first.</summary>
        public List<WindowResult> Results { get { return results.OrderBy(r => r.EndedAt).ToList(); } }

        /// <summary>Records the latest numbers; returns the windows that just ended (new rows of the report).</summary>
        public List<WindowResult> Observe(List<ProviderView> views, AppSettings s, DateTime now)
        {
            var ended = new List<WindowResult>();
            foreach (var v in views)
            {
                if (!v.HasData || v.Stale) continue;
                foreach (var m in v.Meters)
                {
                    if (m.Unlimited || !m.ResetsAt.HasValue || m.WindowMinutes <= 0) continue;
                    string key = v.Id + "|" + m.Key;
                    Track t;
                    if (!tracks.TryGetValue(key, out t))
                    {
                        t = new Track { ResetsAt = m.ResetsAt, LastUsed = m.Used, LastSeen = now };
                        tracks[key] = t;
                        if (m.WindowMinutes >= ReportWindowMinutes) dirty = true;   // remembered across restarts
                    }
                    t.Name = v.Name;
                    t.Label = m.Label;
                    t.WindowMinutes = m.WindowMinutes;
                    if (Rolled(t, m, now))
                    {
                        if (t.WindowMinutes >= ReportWindowMinutes)
                        {
                            var r = new WindowResult
                            {
                                Provider = v.Id, Name = v.Name, Meter = m.Key, Label = t.Label, EndedAt = t.ResetsAt.Value,
                                Used = Math.Round(Math.Max(0, Math.Min(100, t.LastUsed)), 1),
                                NudgeLevel = UseItTracker.AnnouncedLevel(s, key, t.ResetsAt.Value),
                                SeenToEnd = (t.ResetsAt.Value - t.LastSeen).TotalMinutes <= 90,
                                EmptyHours = t.EmptySince.HasValue ? Math.Round(Math.Max(0, (t.ResetsAt.Value - t.EmptySince.Value).TotalHours), 1) : 0,
                            };
                            if (!results.Any(x => x.Key == r.Key && Math.Abs((x.EndedAt - r.EndedAt).TotalHours) < 12))
                            {
                                results.Add(r);
                                ended.Add(r);
                            }
                        }
                        t.Points.Clear();
                        t.EmptySince = null;
                        dirty = true;
                    }
                    t.ResetsAt = m.ResetsAt;   // (estimated reset times move a little)
                    // the pace: a point when the number moves, and every 5 minutes (so a pause shows)
                    var last = t.Points.Count > 0 ? t.Points[t.Points.Count - 1] : null;
                    if (last == null || Math.Abs(last.Used - m.Used) >= 0.05 || (now - last.At).TotalMinutes >= 5)
                        t.Points.Add(new Point { At = now, Used = m.Used });
                    t.Points.RemoveAll(p => (now - p.At).TotalHours > 12);
                    if (t.WindowMinutes >= ReportWindowMinutes && (Math.Abs(t.LastUsed - m.Used) >= 0.5 || (now - t.LastSeen).TotalMinutes >= 10)) dirty = true;
                    if (m.Used >= 99.5 && !t.EmptySince.HasValue)
                    {
                        t.EmptySince = now;
                        if (t.WindowMinutes >= ReportWindowMinutes) dirty = true;
                    }
                    t.LastUsed = m.Used;
                    t.LastSeen = now;
                }
            }
            if (ended.Count > 0 || (dirty && (now - lastSave).TotalMinutes >= 2)) Save(now);
            return ended;
        }

        /// <summary>The window rolled over: its reset time moved on by a good part of a window, or passed and the number dropped.</summary>
        static bool Rolled(Track t, Meter m, DateTime now)
        {
            if (!t.ResetsAt.HasValue) return false;
            double tolerance = Math.Max(120, t.WindowMinutes * 0.25);   // minutes: estimated reset times wander
            if ((m.ResetsAt.Value - t.ResetsAt.Value).TotalMinutes > tolerance) return true;
            return now >= t.ResetsAt.Value && m.Used < t.LastUsed - 10;
        }

        /// <summary>
        /// When this quota runs out at the recent pace (UTC), or null: not before its reset, not used lately, or
        /// too little to go on. The pace is the rise over the last hour (six hours for weekly windows), and only
        /// while the number is still moving (within the last 20 minutes).
        /// </summary>
        public DateTime? RunsOutAt(string provider, Meter m, DateTime now)
        {
            Track t;
            if (m == null || m.Unlimited || !m.ResetsAt.HasValue || m.Used >= 99.5) return null;
            if (!tracks.TryGetValue(provider + "|" + m.Key, out t)) return null;
            double horizon = m.WindowMinutes > 0 && m.WindowMinutes <= 600 ? 60 : 360;
            var pts = t.Points.Where(p => (now - p.At).TotalMinutes <= horizon).ToList();
            if (pts.Count < 2) return null;
            var first = pts[0];
            var last = pts[pts.Count - 1];
            double span = (last.At - first.At).TotalMinutes;
            double rise = last.Used - first.Used;
            if (span < 10 || rise < 2) return null;
            DateTime moved = first.At;
            for (int i = 1; i < pts.Count; i++) if (pts[i].Used - pts[i - 1].Used >= 0.05) moved = pts[i].At;
            if ((now - moved).TotalMinutes > 20) return null;
            var at = now.AddMinutes((100 - m.Used) / (rise / span));
            return at < m.ResetsAt.Value ? at : (DateTime?)null;
        }

        /// <summary>The forecast for every meter of these views (Meter.RunsOutAt).</summary>
        public void Annotate(List<ProviderView> views, DateTime now)
        {
            foreach (var v in views)
                foreach (var m in v.Meters)
                    m.RunsOutAt = v.HasData && !v.Stale ? RunsOutAt(v.Id, m, now) : null;
        }

        // ------------------------------------------------------------ the file

        static string Iso(DateTime t) { return t.ToString("o", CultureInfo.InvariantCulture); }

        static DateTime? Date(object o)
        {
            DateTime d;
            string s = Json.Str(o);
            if (s == null || !DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d)) return null;
            return DateTime.SpecifyKind(d, DateTimeKind.Utc);
        }

        void Load()
        {
            try
            {
                if (file == null || !File.Exists(file)) return;
                var o = Json.Obj(Json.Parse(File.ReadAllText(file, Encoding.UTF8)));
                if (o == null) return;
                var tr = Json.Obj(Json.Get(o, "tracks"));
                if (tr != null)
                    foreach (var kv in tr)
                    {
                        var x = Json.Obj(kv.Value);
                        if (x == null) continue;
                        tracks[kv.Key] = new Track
                        {
                            Name = Json.Str(Json.Get(x, "name")),
                            Label = Json.Str(Json.Get(x, "label")),
                            WindowMinutes = (int)(Json.Num(Json.Get(x, "window")) ?? 0),
                            ResetsAt = Date(Json.Get(x, "resetsAt")),
                            LastUsed = Json.Num(Json.Get(x, "lastUsed")) ?? 0,
                            LastSeen = Date(Json.Get(x, "lastSeen")) ?? DateTime.MinValue,
                            EmptySince = Date(Json.Get(x, "emptySince")),
                        };
                    }
                var ws = Json.Arr(Json.Get(o, "windows"));
                if (ws != null)
                    foreach (var w in ws)
                    {
                        var x = Json.Obj(w);
                        var ended = x != null ? Date(Json.Get(x, "endedAt")) : null;
                        if (ended == null) continue;
                        results.Add(new WindowResult
                        {
                            Provider = Json.Str(Json.Get(x, "provider")),
                            Name = Json.Str(Json.Get(x, "name")),
                            Meter = Json.Str(Json.Get(x, "meter")),
                            Label = Json.Str(Json.Get(x, "label")),
                            EndedAt = ended.Value,
                            Used = Json.Num(Json.Get(x, "used")) ?? 0,
                            NudgeLevel = (int)(Json.Num(Json.Get(x, "nudge")) ?? 0),
                            SeenToEnd = Json.Bool(Json.Get(x, "seenToEnd")) ?? true,
                            EmptyHours = Json.Num(Json.Get(x, "emptyHours")) ?? 0,
                        });
                    }
            }
            catch (Exception ex) { Log.Error("report load", ex); }
        }

        public void Save(DateTime now)
        {
            lastSave = now;
            dirty = false;
            if (file == null) return;
            try
            {
                // keep half a year per quota; forget quotas not seen for two months
                foreach (var g in results.GroupBy(r => r.Key).ToList())
                    foreach (var old in g.OrderByDescending(r => r.EndedAt).Skip(KeepResults).ToList()) results.Remove(old);
                foreach (var k in tracks.Where(kv => (now - kv.Value.LastSeen).TotalDays > 60).Select(kv => kv.Key).ToList()) tracks.Remove(k);

                var tr = new Dictionary<string, object>();
                foreach (var kv in tracks.Where(kv => kv.Value.WindowMinutes >= ReportWindowMinutes && kv.Value.ResetsAt.HasValue))
                {
                    var x = new Dictionary<string, object>
                    {
                        { "name", kv.Value.Name }, { "label", kv.Value.Label }, { "window", kv.Value.WindowMinutes },
                        { "resetsAt", Iso(kv.Value.ResetsAt.Value) }, { "lastUsed", Math.Round(kv.Value.LastUsed, 2) }, { "lastSeen", Iso(kv.Value.LastSeen) },
                    };
                    if (kv.Value.EmptySince.HasValue) x["emptySince"] = Iso(kv.Value.EmptySince.Value);
                    tr[kv.Key] = x;
                }
                var ws = results.OrderBy(r => r.EndedAt).Select(r => (object)new Dictionary<string, object>
                {
                    { "provider", r.Provider }, { "name", r.Name }, { "meter", r.Meter }, { "label", r.Label }, { "endedAt", Iso(r.EndedAt) },
                    { "used", r.Used }, { "nudge", r.NudgeLevel }, { "seenToEnd", r.SeenToEnd }, { "emptyHours", r.EmptyHours },
                }).ToList();
                var o = new Dictionary<string, object> { { "version", 1 }, { "tracks", tr }, { "windows", ws } };
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(o, true), new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(tmp, file, null);
                else File.Move(tmp, file);
            }
            catch (Exception ex) { Log.Error("report save", ex); }
        }
    }
}
