using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SentriPet
{
    /// <summary>
    /// Owns the providers, runs detection and polling on worker threads and reports results.
    /// Events are raised on worker threads — the UI marshals them.
    /// </summary>
    class UsageService : IDisposable
    {
        readonly object gate = new object();
        readonly AppSettings settings;
        List<Provider> providers = new List<Provider>();
        readonly Dictionary<string, Detection> detections = new Dictionary<string, Detection>();
        readonly Dictionary<string, Snapshot> snaps = new Dictionary<string, Snapshot>();
        readonly Dictionary<string, DateTime> nextDue = new Dictionary<string, DateTime>();
        readonly HashSet<string> running = new HashSet<string>();
        readonly HashSet<string> forced = new HashSet<string>();
        List<KeyValuePair<CatalogEntry, Detection>> catalogHits = new List<KeyValuePair<CatalogEntry, Detection>>();
        Timer timer;
        DateTime lastDetect = DateTime.MinValue;
        volatile bool disposed;

        public event Action Changed;

        public UsageService(AppSettings settings)
        {
            this.settings = settings;
        }

        public void Start()
        {
            ReloadProviders();
            timer = new Timer(_ => Tick(), null, 200, 1000);
        }

        public void ReloadProviders()
        {
            List<Provider> old;
            lock (gate)
            {
                old = providers;
                providers = ProviderRegistry.CreateAll();
                snaps.Clear();
                nextDue.Clear();
                lastDetect = DateTime.MinValue;
            }
            foreach (var p in old) { try { p.Dispose(); } catch { } }
            ThreadPool.QueueUserWorkItem(_ => DetectAll());
        }

        public List<Provider> Providers { get { lock (gate) return providers.ToList(); } }

        public Detection DetectionFor(string id)
        {
            lock (gate)
            {
                Detection d;
                return detections.TryGetValue(id, out d) ? d : null;
            }
        }

        public Snapshot SnapshotFor(string id)
        {
            lock (gate)
            {
                Snapshot s;
                return snaps.TryGetValue(id, out s) ? s : null;
            }
        }

        public List<KeyValuePair<CatalogEntry, Detection>> CatalogHits { get { lock (gate) return catalogHits.ToList(); } }

        public void RefreshNow(string id)
        {
            lock (gate)
            {
                foreach (var p in providers)
                {
                    if (id != null && p.Id != id) continue;
                    nextDue[p.Id] = DateTime.MinValue;
                    forced.Add(p.Id);
                }
            }
        }

        public void Redetect()
        {
            ThreadPool.QueueUserWorkItem(_ => DetectAll());
        }

        void DetectAll()
        {
            List<Provider> list;
            lock (gate) { list = providers.ToList(); lastDetect = DateTime.UtcNow; }
            var found = new Dictionary<string, Detection>();
            foreach (var p in list)
            {
                try { found[p.Id] = p.Detect(); }
                catch (Exception ex)
                {
                    Log.Error("detect " + p.Id, ex);
                    found[p.Id] = new Detection();
                }
            }
            var hits = new List<KeyValuePair<CatalogEntry, Detection>>();
            foreach (var c in CatalogEntry.All)
            {
                if (list.Any(p => p.Id == c.Id)) continue;
                try
                {
                    var d = c.Detect();
                    if (d.Installed) hits.Add(new KeyValuePair<CatalogEntry, Detection>(c, d));
                }
                catch { }
            }
            lock (gate)
            {
                foreach (var kv in found) detections[kv.Key] = kv.Value;
                catalogHits = hits;
            }
            Log.Info("detected: " + string.Join(", ", found.Where(kv => kv.Value.Installed).Select(kv => kv.Key)) +
                     (hits.Count > 0 ? " | other: " + string.Join(", ", hits.Select(h => h.Key.Id)) : ""));
            Raise();
        }

        void Tick()
        {
            if (disposed) return;
            var now = DateTime.UtcNow;
            if ((now - lastDetect).TotalMinutes >= 10) { lastDetect = now; ThreadPool.QueueUserWorkItem(_ => DetectAll()); }
            var due = new List<KeyValuePair<Provider, bool>>();
            lock (gate)
            {
                foreach (var p in providers)
                {
                    Detection d;
                    if (!detections.TryGetValue(p.Id, out d) || !d.Installed) continue;
                    if (!settings.IsEnabled(p.Id)) continue;
                    if (running.Contains(p.Id)) continue;
                    DateTime t;
                    if (nextDue.TryGetValue(p.Id, out t) && t > now) continue;
                    running.Add(p.Id);
                    bool force = forced.Remove(p.Id);
                    due.Add(new KeyValuePair<Provider, bool>(p, force));
                }
            }
            foreach (var kv in due)
            {
                var p = kv.Key;
                bool force = kv.Value;
                ThreadPool.QueueUserWorkItem(_ => Run(p, force));
            }
        }

        void Run(Provider p, bool force)
        {
            Snapshot s;
            try { s = p.Fetch(force, settings); }
            catch (Exception ex)
            {
                Log.Error("fetch " + p.Id, ex);
                s = Snapshot.Fail(ex.Message);
            }
            if (s == null) s = Snapshot.Fail("沒有資料");
            lock (gate)
            {
                running.Remove(p.Id);
                if (!providers.Contains(p)) return;   // reloaded meanwhile
                snaps[p.Id] = s;
                nextDue[p.Id] = DateTime.UtcNow.AddSeconds(p.IntervalSeconds);
            }
            Raise();
        }

        void Raise()
        {
            var h = Changed;
            if (h != null && !disposed) { try { h(); } catch (Exception ex) { Log.Error("changed handler", ex); } }
        }

        /// <summary>Builds the per-provider view models for the themes (call on any thread).</summary>
        public List<ProviderView> BuildViews(AppSettings s, bool includeHidden)
        {
            var views = new List<ProviderView>();
            List<Provider> list;
            lock (gate) list = providers.ToList();
            var order = s.Order ?? new List<string>();
            foreach (var p in list.OrderBy(x => { int i = order.IndexOf(x.Id); return i < 0 ? 100 + list.IndexOf(x) : i; }))
            {
                var d = DetectionFor(p.Id);
                if (d == null || !d.Installed) continue;
                if (!includeHidden && !s.IsEnabled(p.Id)) continue;
                var snap = SnapshotFor(p.Id);
                if (snap == null) continue;           // not fetched yet
                if (snap.Offline && !includeHidden) continue;
                views.Add(MakeView(p, snap));
            }
            return views;
        }

        public static ProviderView MakeView(Provider p, Snapshot snap)
        {
            var v = new ProviderView
            {
                Id = p.Id,
                Name = p.Name,
                Mascot = p.Mascot,
                Color = p.Color,
                Snap = snap,
                Meters = snap.Meters,
                Plan = snap.Plan,
                Error = snap.Error,
                Stale = snap.Stale,
                Active = snap.Active,
            };
            var limited = snap.Meters.Where(m => !m.Unlimited).ToList();
            v.HasData = snap.Meters.Count > 0;
            v.Unlimited = v.HasData && limited.Count == 0;
            v.Primary = limited.OrderByDescending(m => m.Used).ThenBy(m => m.WindowMinutes).FirstOrDefault() ?? snap.Meters.FirstOrDefault();
            v.Secondary = snap.Meters.FirstOrDefault(m => m != v.Primary);
            v.Remaining = v.Primary == null ? 100 : v.Primary.Remaining;
            v.Mood = !v.HasData ? Mood.Unknown : ProviderView.MoodFor(v.Remaining);
            if (!v.HasData) v.StatusText = snap.Error ?? "沒有資料";
            else
            {
                string t = snap.ObservedAt.HasValue ? snap.ObservedAt.Value.ToLocalTime().ToString("HH:mm") + " 更新" : "已更新";
                if (snap.ObservedAt.HasValue && (DateTime.UtcNow - snap.ObservedAt.Value).TotalHours >= 20) t = Fmt.Ago(snap.ObservedAt) + "更新";
                v.StatusText = t + (snap.Source != null ? " · " + snap.Source : "");
            }
            return v;
        }

        public void Dispose()
        {
            disposed = true;
            if (timer != null) timer.Dispose();
            foreach (var p in Providers) { try { p.Dispose(); } catch { } }
        }
    }
}
