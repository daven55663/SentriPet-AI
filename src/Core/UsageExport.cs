using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>
    /// The numbers for other programs (#22): usage.json in the settings folder, and the same JSON from the local web page
    /// (see UsageServer). The format is documented in docs/usage-json.md; "version" goes up only when a field changes
    /// meaning or goes away — new fields may be added to version 1.
    /// </summary>
    static class UsageExport
    {
        public const int Version = 1;

        public static string File { get { return Path.Combine(AppPaths.DataDir, "usage.json"); } }

        /// <summary>The JSON object for the providers shown right now.</summary>
        public static Dictionary<string, object> Build(IList<ProviderView> views, DateTime nowUtc)
        {
            var providers = new List<object>();
            double? lowest = null;
            foreach (var v in views)
            {
                var meters = new List<object>();
                foreach (var m in v.Meters)
                {
                    meters.Add(new Dictionary<string, object>
                    {
                        { "key", m.Key },
                        { "label", m.Label },
                        { "shortLabel", m.ShortLabel },
                        { "windowMinutes", m.WindowMinutes > 0 ? (object)m.WindowMinutes : null },
                        { "unlimited", m.Unlimited },
                        { "used", m.Unlimited ? null : (object)Round(m.Used) },
                        { "remaining", m.Unlimited ? null : (object)Round(m.Remaining) },
                        { "estimated", m.UsedApprox },
                        { "resetsAt", Utc(m.ResetsAt) },
                        { "resetEstimated", m.ResetsAt.HasValue && m.ResetApprox },
                        { "runsOutAt", Utc(m.RunsOutAt) },
                        { "value", string.IsNullOrEmpty(m.ValueText) ? null : m.ValueText },
                    });
                }
                bool limited = v.HasData && !v.Unlimited;
                if (limited && (!lowest.HasValue || v.Remaining < lowest.Value)) lowest = v.Remaining;
                providers.Add(new Dictionary<string, object>
                {
                    { "id", v.Id },
                    { "name", v.Name },
                    { "color", v.Color.ToHex() },
                    { "plan", string.IsNullOrEmpty(v.Plan) ? null : v.Plan },
                    { "hasData", v.HasData },
                    { "active", v.Active },
                    { "stale", v.Stale },
                    { "unlimited", v.Unlimited },
                    { "remaining", limited ? (object)Round(v.Remaining) : null },
                    { "mood", v.HasData ? v.Mood.ToString().ToLowerInvariant() : "unknown" },
                    { "status", string.IsNullOrEmpty(v.StatusText) ? null : v.StatusText },
                    { "error", string.IsNullOrEmpty(v.Error) ? null : v.Error },
                    { "observedAt", Utc(v.Snap != null ? v.Snap.ObservedAt : null) },
                    { "meters", meters },
                });
            }
            return new Dictionary<string, object>
            {
                { "version", Version },
                { "app", AppInfo.Name },
                { "appVersion", AppInfo.Version },
                { "language", L.Current },
                { "updatedAt", Utc(nowUtc) },
                { "lowestRemaining", lowest.HasValue ? (object)Round(lowest.Value) : null },
                { "providers", providers },
            };
        }

        static double Round(double v) { return Math.Round(v, 1); }

        static string Utc(DateTime? t)
        {
            if (!t.HasValue) return null;
            var u = t.Value.Kind == DateTimeKind.Local ? t.Value.ToUniversalTime() : t.Value;
            return u.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Keeps the latest usage JSON (for the local web page) and writes usage.json when the numbers change, or once a
    /// minute so scripts can tell from "updatedAt" that the widget still runs. Written through a temporary file, so a
    /// reader never sees half a file.
    /// </summary>
    class UsageExporter
    {
        public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(1);

        readonly string file;
        string lastContent;        // the JSON without "updatedAt"
        DateTime lastWrite = DateTime.MinValue;
        volatile string latest;

        public UsageExporter(string file) { this.file = file; }

        /// <summary>The newest JSON (null before the first Update).</summary>
        public string Latest { get { return latest; } }

        /// <summary>How many times the file was written (tests).</summary>
        public int Writes { get; private set; }

        /// <summary>New numbers; writes the file when <paramref name="toFile"/> and something changed (or a minute passed).</summary>
        public void Update(IList<ProviderView> views, DateTime nowUtc, bool toFile)
        {
            var o = UsageExport.Build(views, nowUtc);
            latest = Json.Serialize(o, true);
            if (!toFile) return;
            o.Remove("updatedAt");
            string content = Json.Serialize(o, false);
            if (content == lastContent && nowUtc - lastWrite < Heartbeat) return;
            try
            {
                string dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = file + ".tmp";
                System.IO.File.WriteAllText(tmp, latest, new UTF8Encoding(false));
                if (System.IO.File.Exists(file)) System.IO.File.Replace(tmp, file, null);
                else System.IO.File.Move(tmp, file);
                lastContent = content;
                lastWrite = nowUtc;
                Writes++;
            }
            catch (Exception ex)
            {
                // someone reading the file right now (Windows keeps it locked): try again next second
                Log.Warn("usage.json: " + ex.Message);
            }
        }

        /// <summary>The option was turned off: remove the file, so no script keeps reading old numbers.</summary>
        public void Remove()
        {
            lastContent = null;
            lastWrite = DateTime.MinValue;
            try { if (System.IO.File.Exists(file)) System.IO.File.Delete(file); }
            catch (Exception ex) { Log.Warn("usage.json: " + ex.Message); }
        }
    }
}
