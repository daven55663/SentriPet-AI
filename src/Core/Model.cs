using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SentriPet
{
    /// <summary>One usage window, e.g. "5 hours" or "weekly".</summary>
    class Meter
    {
        public string Key;
        public string Label;          // 5 小時
        public string ShortLabel;     // 5h
        public double Used;           // 0..100
        public bool UsedApprox;       // Used includes an estimate on top of the last real reading
        public DateTime? ResetsAt;    // UTC
        public bool ResetApprox;      // reset time is an estimate
        public int WindowMinutes;
        public bool Unlimited;
        public string ValueText;      // optional, e.g. "250 / 300"
        public bool WasReset;         // window rolled over after the data was observed

        public double Remaining
        {
            get { return Unlimited ? 100 : Math.Max(0, Math.Min(100, 100 - Used)); }
        }

        public Meter Clone() { return (Meter)MemberwiseClone(); }
    }

    /// <summary>The latest known usage for one provider.</summary>
    class Snapshot
    {
        public List<Meter> Meters = new List<Meter>();
        public string Plan;
        public DateTime? ObservedAt;   // UTC time the numbers were measured
        public string Source;          // human readable data source
        public string Error;           // set when nothing usable was found
        public string Note;            // extra info line
        public bool Offline;           // local service not running → hide from widget
        public bool Active;            // the AI is working right now
        public bool Stale;             // numbers are old

        public Snapshot Clone()
        {
            var s = (Snapshot)MemberwiseClone();
            s.Meters = Meters.Select(m => m.Clone()).ToList();
            return s;
        }

        public static Snapshot Fail(string error)
        {
            return new Snapshot { Error = error };
        }
    }

    class Detection
    {
        public bool Installed;
        public bool UsageSupported = true;
        public List<string> Evidence = new List<string>();
        public string Hint;

        public string EvidenceText
        {
            get { return Evidence.Count == 0 ? "未偵測到" : string.Join(" · ", Evidence.Distinct()); }
        }
    }

    enum Mood { Great, Good, Worried, Critical, Empty, Unknown }

    /// <summary>Everything a theme needs to draw one provider.</summary>
    class ProviderView
    {
        public string Id;
        public string Name;
        public string Mascot;
        public Rgba Color;
        public Snapshot Snap;
        public List<Meter> Meters = new List<Meter>();
        public Meter Primary;         // most constrained window (drives the mood)
        public Meter Secondary;       // second window (if any)
        public double Remaining;      // primary remaining (100 when unlimited / unknown)
        public Meter Headline;        // the big number: the short (5-hour) window when there is one
        public double HeadlineRemaining;
        public Meter UseIt;           // a weekly/monthly window that resets soon with quota left over
        public int UseItLevel;        // 0 none · 1 last 2 days · 2 last day · 3 last hours
        public Mood Mood;
        public bool HasData;
        public bool Stale;
        public bool Active;
        public bool Unlimited;
        public string Plan;
        public string Error;
        public string StatusText;

        public Rgba Dark { get { return Color.Darken(0.35); } }
        public Rgba Light { get { return Color.Lighten(0.55); } }

        /// <summary>
        /// The window a one-line "↻ resets in …" status talks about: the one that is nearly used up (it is what
        /// blocks you), otherwise the headline window, otherwise the next window that is counting down.
        /// </summary>
        public Meter ResetMeter
        {
            get
            {
                if (Primary != null && !Primary.Unlimited && Primary.ResetsAt.HasValue && Primary.Remaining < 12) return Primary;
                if (Headline != null && Headline.ResetsAt.HasValue) return Headline;
                return Meters.FirstOrDefault(m => !m.Unlimited && m.ResetsAt.HasValue) ?? Headline;
            }
        }

        /// <summary>"5h " / "週 " when there is more than one window, so a one-line status says which one it means.</summary>
        public string LabelOf(Meter m)
        {
            return m != null && Meters.Count > 1 && !string.IsNullOrEmpty(m.ShortLabel) ? m.ShortLabel + " " : "";
        }

        /// <summary>"Claude 5h 100% 週 61%" — every window, for the tray tooltip and the menu.</summary>
        public string Summary
        {
            get
            {
                if (!HasData) return Name + " ?";
                if (Unlimited) return Name + " ∞";
                return Name + " " + string.Join(" ", Meters.Take(2).Select(m => LabelOf(m) + (m.Unlimited ? "∞" : Fmt.Pct(m.Remaining))));
            }
        }

        public static Mood MoodFor(double remaining)
        {
            if (remaining >= 60) return Mood.Great;
            if (remaining >= 30) return Mood.Good;
            if (remaining >= 12) return Mood.Worried;
            if (remaining > 0.5) return Mood.Critical;
            return Mood.Empty;
        }

        /// <summary>
        /// "Use it or lose it": how urgently the left-over quota of a weekly/monthly window should be spent
        /// before it resets. 3 = last 6 hours (≥5% left), 2 = last day (≥10%), 1 = last 2 days (≥30%).
        /// Short windows (5 hours, daily) come back too soon to be worth nagging about.
        /// </summary>
        public static int UseItLevelFor(Meter m, DateTime nowUtc)
        {
            if (m == null || m.Unlimited || m.WindowMinutes < 3 * 1440 || !m.ResetsAt.HasValue) return 0;
            double hours = (m.ResetsAt.Value - nowUtc).TotalHours;
            double left = m.Remaining;
            if (hours <= 0) return 0;
            if (hours <= 6 && left >= 5) return 3;
            if (hours <= 24 && left >= 10) return 2;
            if (hours <= 48 && left >= 30) return 1;
            return 0;
        }
    }

    static class Fmt
    {
        static readonly string[] WeekDays = { "日", "一", "二", "三", "四", "五", "六" };

        public static string Pct(double v)
        {
            return Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Compact Chinese countdown: 2天3時 / 4時12分 / 12分 / 45秒.</summary>
        public static string Countdown(DateTime? resetUtc)
        {
            if (!resetUtc.HasValue) return "—";
            var d = resetUtc.Value - DateTime.UtcNow;
            if (d.TotalSeconds <= 0) return "即將重置";
            if (d.TotalDays >= 1) return (int)d.TotalDays + "天" + d.Hours + "時";
            if (d.TotalHours >= 1) return (int)d.TotalHours + "時" + d.Minutes.ToString("00") + "分";
            if (d.TotalMinutes >= 1) return d.Minutes + "分" + d.Seconds.ToString("00") + "秒";
            return d.Seconds + "秒";
        }

        /// <summary>Clock style countdown: 02:13:45 or 3d 04:12.</summary>
        public static string Clock(DateTime? resetUtc)
        {
            if (!resetUtc.HasValue) return "--:--:--";
            var d = resetUtc.Value - DateTime.UtcNow;
            if (d.TotalSeconds <= 0) return "00:00:00";
            if (d.TotalDays >= 1) return (int)d.TotalDays + "d " + d.Hours.ToString("00") + ":" + d.Minutes.ToString("00");
            return ((int)d.TotalHours).ToString("00") + ":" + d.Minutes.ToString("00") + ":" + d.Seconds.ToString("00");
        }

        /// <summary>今天 15:00 / 明天 04:00 / 週四 23:00 / 10/1 00:00.</summary>
        public static string When(DateTime? utc)
        {
            if (!utc.HasValue) return "未知";
            var t = utc.Value.ToLocalTime();
            var today = DateTime.Now.Date;
            string hm = t.ToString("HH:mm");
            if (t.Date == today) return "今天 " + hm;
            if (t.Date == today.AddDays(1)) return "明天 " + hm;
            if (t.Date > today && t.Date < today.AddDays(7)) return "週" + WeekDays[(int)t.DayOfWeek] + " " + hm;
            return t.Month + "/" + t.Day + " " + hm;
        }

        public static string Ago(DateTime? utc)
        {
            if (!utc.HasValue) return "未知";
            var d = DateTime.UtcNow - utc.Value;
            if (d.TotalSeconds < 60) return "剛剛";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " 分鐘前";
            if (d.TotalHours < 24) return (int)d.TotalHours + " 小時前";
            return (int)d.TotalDays + " 天前";
        }

        public static string WindowLabel(int minutes)
        {
            if (minutes <= 0) return "額度";
            if (minutes == 10080) return "每週";
            if (minutes == 1440) return "每日";
            if (minutes >= 40000 && minutes <= 46000) return "每月";
            if (minutes % 1440 == 0) return (minutes / 1440) + " 天";
            if (minutes % 60 == 0) return (minutes / 60) + " 小時";
            if (minutes > 60 && minutes % 60 >= 55) return (minutes / 60 + 1) + " 小時";
            return minutes + " 分鐘";
        }

        public static string WindowShort(int minutes)
        {
            if (minutes <= 0) return "Q";
            if (minutes == 10080) return "週";
            if (minutes == 1440) return "日";
            if (minutes >= 40000 && minutes <= 46000) return "月";
            if (minutes % 1440 == 0) return (minutes / 1440) + "d";
            if (minutes % 60 == 0) return (minutes / 60) + "h";
            if (minutes > 60 && minutes % 60 >= 55) return (minutes / 60 + 1) + "h";
            return minutes + "m";
        }

        public static string Title(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
