using System;
using System.Globalization;

namespace SentriPet
{
    /// <summary>
    /// Quiet time (#17): during the set hours, and while reminders are paused from the menu, the widget shows no
    /// notifications and says nothing by itself (it still answers when clicked); the numbers keep showing.
    /// </summary>
    static class Quiet
    {
        /// <summary>How long "pause reminders" lasts.</summary>
        public static readonly TimeSpan PauseLength = TimeSpan.FromHours(1);

        /// <summary>"22:00", "8:30", "0800" → time of day.</summary>
        public static bool TryParseTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim().Replace('：', ':').Replace('.', ':');
            int h, m = 0;
            if (s.Contains(":"))
            {
                var parts = s.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out h) ||
                    !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out m) || parts[1].Length != 2) return false;
            }
            else
            {
                int n;
                if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return false;
                if (s.Length <= 2) h = n;
                else if (s.Length == 4) { h = n / 100; m = n % 100; }
                else return false;
            }
            if (h < 0 || h > 24 || m < 0 || m > 59 || (h == 24 && m > 0)) return false;
            time = new TimeSpan(h, m, 0);
            return true;
        }

        /// <summary>Whether quiet hours are on for a weekday ("0123456", Sunday = 0).</summary>
        public static bool DayOn(AppSettings s, DayOfWeek day)
        {
            return string.IsNullOrEmpty(s.QuietDays) || s.QuietDays.IndexOf((char)('0' + (int)day)) >= 0;
        }

        /// <summary>
        /// Inside the quiet hours at this local time? A period over midnight (22:00–08:00) belongs to the day it
        /// starts on: with Monday to Friday, Friday night is quiet until Saturday 08:00, Sunday night is not.
        /// The same start and end means the whole day.
        /// </summary>
        public static bool InHours(AppSettings s, DateTime local)
        {
            if (!s.QuietHours) return false;
            TimeSpan from, to;
            if (!TryParseTime(s.QuietFrom, out from) || !TryParseTime(s.QuietTo, out to)) return false;
            var t = local.TimeOfDay;
            if (from == to) return DayOn(s, local.DayOfWeek);
            if (from < to) return DayOn(s, local.DayOfWeek) && t >= from && t < to;
            if (t >= from) return DayOn(s, local.DayOfWeek);
            if (t < to) return DayOn(s, local.AddDays(-1).DayOfWeek);
            return false;
        }

        public static bool Paused(AppSettings s, DateTime utcNow)
        {
            return s.PausedUntil.HasValue && utcNow < s.PausedUntil.Value;
        }

        /// <summary>No notifications and no speech of its own right now.</summary>
        public static bool IsQuiet(AppSettings s, DateTime utcNow)
        {
            return Paused(s, utcNow) || InHours(s, utcNow.ToLocalTime());
        }

        /// <summary>When the current quiet time ends (local time), or null when it is not quiet.</summary>
        public static DateTime? EndsAt(AppSettings s, DateTime utcNow)
        {
            if (!IsQuiet(s, utcNow)) return null;
            var end = Paused(s, utcNow) ? s.PausedUntil.Value.ToLocalTime() : utcNow.ToLocalTime();
            // step forward a minute at a time through the quiet hours (at most two days: whole days can follow each other)
            for (int i = 0; i < 2 * 24 * 60 && InHours(s, end); i++) end = end.AddMinutes(1);
            if (Paused(s, end.ToUniversalTime())) end = s.PausedUntil.Value.ToLocalTime();
            return new DateTime(end.Year, end.Month, end.Day, end.Hour, end.Minute, 0, DateTimeKind.Local);
        }
    }
}
