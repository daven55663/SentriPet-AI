using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>An achievement (#23). Name and description are translation keys.</summary>
    class Achievement
    {
        public string Id, Name, Description;
        public int Xp;          // bonus when unlocked
        public int Goal;        // for "2 / 4" on the settings page (0 = none)
    }

    /// <summary>Something to tell the user: a level reached or an achievement unlocked.</summary>
    class ProgressEvent
    {
        public int Level;                 // > 0: this level was reached
        public Achievement Achievement;   // or this was unlocked
    }

    /// <summary>
    /// Growing pets (#23): experience for using quotas well — planned, not wasted, and not simply "more" — and for
    /// coming back every day; levels (the jelly pets put on accessories) and achievements. Kept in progress.json.
    ///
    /// Experience for each weekly/monthly window that ends (see <see cref="WindowXp"/>): most for using 90% or more
    /// without running out long before the reset; running out early (blocked for a while) earns less than using 70–90%.
    /// A day with the widget open adds a little. Levels need more experience each time (<see cref="XpFor"/>).
    /// </summary>
    class Progress
    {
        public const int DailyXp = 3;
        const double EarlyHours = 12;   // used up more than this long before the reset: ran out too early

        public static readonly List<Achievement> All = new List<Achievement>
        {
            new Achievement { Id = "first-window", Xp = 10, Name = L.N("第一份週報"), Description = L.N("第一次記下一整期週／月額度用了多少") },
            new Achievement { Id = "well-used", Xp = 20, Name = L.N("物盡其用"), Description = L.N("一期額度用到 90% 以上") },
            new Achievement { Id = "planner", Xp = 30, Name = L.N("精準規劃"), Description = L.N("一期額度用到 95% 以上，而且沒有提早用完（最多只卡 6 小時）") },
            new Achievement { Id = "listened", Xp = 20, Name = L.N("聽勸"), Description = L.N("被桌寵催著用額度之後，那一期用到 80% 以上") },
            new Achievement { Id = "recovered", Xp = 30, Name = L.N("重新振作"), Description = L.N("上一期用不到一半，這一期用到 80% 以上") },
            new Achievement { Id = "no-waste-4", Xp = 40, Goal = 4, Name = L.N("不浪費"), Description = L.N("同一個額度連續 4 期都用到 80% 以上") },
            new Achievement { Id = "steady-8", Xp = 80, Goal = 8, Name = L.N("細水長流"), Description = L.N("同一個額度連續 8 期都用到 70% 以上，而且都沒有提早用完") },
            new Achievement { Id = "two-pets", Xp = 30, Name = L.N("雙管齊下"), Description = L.N("同一週裡兩個不同的 AI 都把額度用到 70% 以上") },
            new Achievement { Id = "streak-7", Xp = 20, Goal = 7, Name = L.N("天天見"), Description = L.N("連續 7 天打開桌寵") },
            new Achievement { Id = "streak-30", Xp = 60, Goal = 30, Name = L.N("老朋友"), Description = L.N("連續 30 天打開桌寵") },
        };

        /// <summary>What the jelly pets put on, by level (translation keys).</summary>
        public static readonly SortedDictionary<int, string> Accessories = new SortedDictionary<int, string>
        {
            { 2, L.N("蝴蝶結") },
            { 4, L.N("星星徽章") },
            { 6, L.N("小花") },
            { 8, L.N("皇冠") },
            { 10, L.N("金色光芒") },
        };

        readonly string file;
        readonly HashSet<string> counted = new HashSet<string>();   // windows already scored ("provider|meter|yyyy-MM-dd")
        public int Xp { get; private set; }
        public Dictionary<string, DateTime> Unlocked { get; private set; }
        public int Streak { get; private set; }
        public int BestStreak { get; private set; }
        public int Days { get; private set; }
        string lastDay;

        /// <summary>False when there was no progress.json yet (the first start of 2.4: see <see cref="Backfill"/>).</summary>
        public bool Existed { get; private set; }

        /// <summary>Changed since the last Save.</summary>
        public bool Dirty { get; private set; }

        public Progress(string file)
        {
            this.file = file;
            Unlocked = new Dictionary<string, DateTime>();
            Load();
        }

        public static string DefaultFile { get { return Path.Combine(AppPaths.DataDir, "progress.json"); } }

        public int Level { get { return LevelFor(Xp); } }

        /// <summary>The experience a level needs: 0, 40, 120, 240, 400… (40 × 1, 1+2, 1+2+3…).</summary>
        public static int XpFor(int level)
        {
            int n = Math.Max(1, level) - 1;
            return 20 * n * (n + 1);
        }

        public static int LevelFor(int xp)
        {
            int level = 1;
            while (XpFor(level + 1) <= xp) level++;
            return level;
        }

        /// <summary>The next accessory to unlock (level, name key), or null when all are on.</summary>
        public KeyValuePair<int, string>? NextAccessory
        {
            get
            {
                foreach (var kv in Accessories) if (kv.Key > Level) return kv;
                return null;
            }
        }

        /// <summary>Experience for one weekly/monthly window: planned and not wasted counts, not simply "more".</summary>
        public static int WindowXp(WindowResult r)
        {
            if (!r.SeenToEnd && r.Used < 40) return 2;   // may look low only because the widget wasn't running
            if (r.Used >= 90) return r.EmptyHours > EarlyHours ? 15 : 30;
            if (r.Used >= 70) return 20;
            if (r.Used >= 40) return 10;
            return 3;
        }

        static string WindowId(WindowResult r)
        {
            return r.Key + "|" + r.EndedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A weekly/monthly window ended: its experience and the achievements it completes. <paramref name="history"/>
        /// is every finished window (including this one).
        /// </summary>
        public List<ProgressEvent> Window(WindowResult r, IList<WindowResult> history)
        {
            var events = new List<ProgressEvent>();
            if (!counted.Add(WindowId(r))) return events;
            Dirty = true;
            int before = Level;
            Xp += WindowXp(r);
            var same = history.Where(x => x.Key == r.Key && x.EndedAt <= r.EndedAt.AddHours(1)).OrderBy(x => x.EndedAt).ToList();
            if (!same.Any(x => Math.Abs((x.EndedAt - r.EndedAt).TotalHours) < 12)) same.Add(r);
            Unlock("first-window", r.EndedAt, events);
            if (r.Used >= 90) Unlock("well-used", r.EndedAt, events);
            if (r.Used >= 95 && r.EmptyHours <= 6) Unlock("planner", r.EndedAt, events);
            if (r.NudgeLevel >= 1 && r.Used >= 80) Unlock("listened", r.EndedAt, events);
            if (same.Count >= 2 && same[same.Count - 2].Used < 50 && same[same.Count - 2].SeenToEnd && r.Used >= 80) Unlock("recovered", r.EndedAt, events);
            if (Run(same, x => x.Used >= 80) >= 4) Unlock("no-waste-4", r.EndedAt, events);
            if (Run(same, x => x.Used >= 70 && x.EmptyHours <= EarlyHours) >= 8) Unlock("steady-8", r.EndedAt, events);
            if (r.Used >= 70 && history.Any(x => x.Provider != r.Provider && x.Used >= 70 && Math.Abs((x.EndedAt - r.EndedAt).TotalDays) <= 3.5))
                Unlock("two-pets", r.EndedAt, events);
            LevelEvents(before, events);
            return events;
        }

        /// <summary>How many of the latest windows in a row pass a test.</summary>
        static int Run(List<WindowResult> windows, Func<WindowResult, bool> ok)
        {
            int n = 0;
            for (int i = windows.Count - 1; i >= 0 && ok(windows[i]); i--) n++;
            return n;
        }

        /// <summary>A day with the widget open (and an AI with numbers): the streak and a little experience, once a day.</summary>
        public List<ProgressEvent> Day(DateTime localDate)
        {
            var events = new List<ProgressEvent>();
            string day = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (day == lastDay) return events;
            Dirty = true;
            int before = Level;
            string yesterday = localDate.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Streak = lastDay == yesterday ? Streak + 1 : 1;
            BestStreak = Math.Max(BestStreak, Streak);
            lastDay = day;
            Days++;
            Xp += DailyXp;
            var when = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Local).ToUniversalTime();
            if (Streak >= 7) Unlock("streak-7", when, events);
            if (Streak >= 30) Unlock("streak-30", when, events);
            LevelEvents(before, events);
            return events;
        }

        /// <summary>
        /// The first start with growing pets: score the windows already in the report (nothing is announced for them).
        /// Returns the level reached.
        /// </summary>
        public int Backfill(IList<WindowResult> history)
        {
            foreach (var r in history.OrderBy(x => x.EndedAt).ToList())
                Window(r, history.Where(x => x.EndedAt <= r.EndedAt.AddHours(1)).ToList());
            Existed = true;
            return Level;
        }

        /// <summary>For the settings page: how far a counting achievement has got ("2 / 4"), or null.</summary>
        public string ProgressOf(Achievement a, IList<WindowResult> history)
        {
            if (a.Goal <= 0 || Unlocked.ContainsKey(a.Id)) return null;
            int best = 0;
            if (a.Id == "streak-7" || a.Id == "streak-30") best = Streak;
            else
                foreach (var g in history.GroupBy(x => x.Key))
                {
                    var w = g.OrderBy(x => x.EndedAt).ToList();
                    best = Math.Max(best, a.Id == "no-waste-4" ? Run(w, x => x.Used >= 80) : Run(w, x => x.Used >= 70 && x.EmptyHours <= EarlyHours));
                }
            return Math.Min(best, a.Goal) + " / " + a.Goal;
        }

        void Unlock(string id, DateTime at, List<ProgressEvent> events)
        {
            if (Unlocked.ContainsKey(id)) return;
            var a = All.First(x => x.Id == id);
            Unlocked[id] = at;
            Xp += a.Xp;
            events.Add(new ProgressEvent { Achievement = a });
        }

        void LevelEvents(int before, List<ProgressEvent> events)
        {
            for (int l = before + 1; l <= Level; l++) events.Add(new ProgressEvent { Level = l });
        }

        // ------------------------------------------------------------ the file

        void Load()
        {
            try
            {
                if (file == null || !File.Exists(file)) return;
                var o = Json.Obj(Json.Parse(File.ReadAllText(file, Encoding.UTF8)));
                if (o == null) return;
                Existed = true;
                Xp = (int)(Json.Num(Json.Get(o, "xp")) ?? 0);
                Streak = (int)(Json.Num(Json.Get(o, "streak")) ?? 0);
                BestStreak = (int)(Json.Num(Json.Get(o, "bestStreak")) ?? 0);
                Days = (int)(Json.Num(Json.Get(o, "days")) ?? 0);
                lastDay = Json.Str(Json.Get(o, "lastDay"));
                var c = Json.Arr(Json.Get(o, "counted"));
                if (c != null) foreach (var x in c) { var s = Json.Str(x); if (s != null) counted.Add(s); }
                var u = Json.Obj(Json.Get(o, "achievements"));
                if (u != null)
                    foreach (var kv in u)
                    {
                        DateTime d;
                        if (All.Any(a => a.Id == kv.Key) &&
                            DateTime.TryParse(Json.Str(kv.Value) ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d))
                            Unlocked[kv.Key] = DateTime.SpecifyKind(d, DateTimeKind.Utc);
                    }
            }
            catch (Exception ex) { Log.Error("progress load", ex); }
        }

        public void Save()
        {
            Dirty = false;
            if (file == null) return;
            try
            {
                var o = new Dictionary<string, object>
                {
                    { "version", 1 },
                    { "xp", Xp },
                    { "level", Level },
                    { "streak", Streak },
                    { "bestStreak", BestStreak },
                    { "days", Days },
                    { "lastDay", lastDay },
                    { "achievements", Unlocked.ToDictionary(kv => kv.Key, kv => (object)kv.Value.ToString("o", CultureInfo.InvariantCulture)) },
                    { "counted", counted.OrderBy(x => x.Substring(x.LastIndexOf('|') + 1)).Skip(Math.Max(0, counted.Count - 500)).ToList() },
                };
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(o, true), new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(tmp, file, null);
                else File.Move(tmp, file);
                Existed = true;
            }
            catch (Exception ex) { Log.Error("progress save", ex); }
        }
    }
}
