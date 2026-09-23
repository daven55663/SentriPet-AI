using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SentriPet
{
    /// <summary>Notices when a window crosses the warning thresholds or resets.</summary>
    class AlertTracker
    {
        readonly Dictionary<string, double> used = new Dictionary<string, double>();
        readonly Dictionary<string, int> level = new Dictionary<string, int>();

        /// <param name="alert">(view, window, 1 = warning / 2 = critical) when a window crosses a threshold</param>
        /// <param name="reset">(view, window, used % before) when a window that was in use drops back to ~0</param>
        public void Check(AppSettings s, List<ProviderView> views, Action<ProviderView, Meter, int> alert, Action<ProviderView, Meter, double> reset)
        {
            foreach (var v in views)
            {
                if (!v.HasData) continue;
                foreach (var m in v.Meters)
                {
                    if (m.Unlimited) continue;
                    string key = v.Id + "|" + m.Key;
                    int lv = m.Used >= s.CriticalAt ? 2 : m.Used >= s.WarnAt ? 1 : 0;
                    double prevUsed;
                    int prevLv;
                    if (used.TryGetValue(key, out prevUsed) && level.TryGetValue(key, out prevLv))
                    {
                        if (lv > prevLv) alert(v, m, lv);
                        else if (prevUsed >= 30 && m.Used <= 3) reset(v, m, prevUsed);
                    }
                    used[key] = m.Used;
                    level[key] = lv;
                }
            }
        }
    }

    /// <summary>Something the pet should say about quota that expires unused.</summary>
    class UseItEvent
    {
        public ProviderView View;
        public bool Announce;     // the urgency went up: say it, send a notification, remember it
        public string Text;
    }

    /// <summary>
    /// "Use it before it resets" (see ProviderView.UseItLevelFor): each urgency level is announced once per window —
    /// remembered in the settings, so a restart does not repeat it — and in between the pet keeps reminding you,
    /// more often as the reset gets closer.
    /// </summary>
    class UseItTracker
    {
        readonly Dictionary<string, DateTime> nextNudge = new Dictionary<string, DateTime>();

        /// <summary>Minutes between reminders at an urgency level.</summary>
        public static double NudgeMinutes(int level) { return level >= 3 ? 12 : level == 2 ? 25 : 60; }

        /// <param name="canTalk">false while a reminder would not be seen (hidden widget, locked session, open menu)</param>
        public List<UseItEvent> Check(AppSettings s, List<ProviderView> views, DateTime now, bool canTalk, Random rng)
        {
            var events = new List<UseItEvent>();
            if (!s.UseItReminder) return events;
            foreach (var v in views)
            {
                var m = v.UseIt;
                if (!v.HasData || v.UseItLevel == 0 || m == null || !m.ResetsAt.HasValue) continue;
                string key = v.Id + "|" + m.Key;
                if (v.UseItLevel > AnnouncedLevel(s, key, m.ResetsAt.Value))
                {
                    Prune(s, now);
                    s.UseItNotified[key] = m.ResetsAt.Value.ToString("o", CultureInfo.InvariantCulture) + "#" + v.UseItLevel;
                    events.Add(new UseItEvent { View = v, Announce = true, Text = Lines.UseItAlert(v) });
                    nextNudge[key] = now.AddMinutes(NudgeMinutes(v.UseItLevel));
                    continue;
                }
                DateTime due;
                if (!nextNudge.TryGetValue(key, out due))
                {
                    // first reminder a few minutes after start-up (not on top of the greeting)
                    nextNudge[key] = now.AddMinutes(Math.Min(6, NudgeMinutes(v.UseItLevel)));
                    continue;
                }
                if (now < due) continue;
                if (!canTalk) { nextNudge[key] = now.AddMinutes(2); continue; }
                nextNudge[key] = now.AddMinutes(NudgeMinutes(v.UseItLevel) * (0.85 + 0.3 * rng.NextDouble()));
                if (v.Active) continue;           // already on it
                events.Add(new UseItEvent { View = v, Text = Lines.UseIt(v, rng) });
            }
            return events;
        }

        /// <summary>The level already announced for the window that resets at <paramref name="reset"/> (0 = none).</summary>
        public static int AnnouncedLevel(AppSettings s, string key, DateTime reset)
        {
            string text;
            DateTime at;
            int level;
            if (!s.UseItNotified.TryGetValue(key, out text) || !TryParse(text, out at, out level)) return 0;
            // same window? (an estimated reset time can move a little between readings)
            return Math.Abs((at - reset).TotalHours) < 6 ? level : 0;
        }

        /// <summary>Forgets announcements for windows that reset more than two days ago (or cannot be read).</summary>
        public static void Prune(AppSettings s, DateTime now)
        {
            foreach (var k in s.UseItNotified.Keys.ToList())
            {
                DateTime at;
                int level;
                if (!TryParse(s.UseItNotified[k], out at, out level) || at < now.AddDays(-2)) s.UseItNotified.Remove(k);
            }
        }

        /// <summary>"2026-09-24T15:00:00.0000000Z#2" → reset time (UTC) and level.</summary>
        static bool TryParse(string text, out DateTime at, out int level)
        {
            at = DateTime.MinValue;
            level = 0;
            if (text == null) return false;
            int hash = text.LastIndexOf('#');
            if (hash < 0 || !int.TryParse(text.Substring(hash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out level)) return false;
            if (!DateTime.TryParse(text.Substring(0, hash), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at)) return false;
            at = at.ToUniversalTime();
            return true;
        }
    }
}
