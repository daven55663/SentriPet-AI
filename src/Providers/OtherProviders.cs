using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentriPet
{
    /// <summary>
    /// GitHub Copilot monthly quotas, read from the entitlement cache the Copilot CLI keeps
    /// (%LOCALAPPDATA%\copilot\copilot-user-cache.json → quota_snapshots).
    /// </summary>
    class CopilotProvider : Provider
    {
        public CopilotProvider()
        {
            Id = "copilot";
            Name = "Copilot";
            Mascot = "goggles";
            Color = Palette.Hex("#A371F7");
        }

        public override int IntervalSeconds { get { return 60; } }

        /// <summary>Set by the self-test to read a sample cache file.</summary>
        internal static string CacheOverride;

        static string FindCache()
        {
            if (CacheOverride != null) return File.Exists(CacheOverride) ? CacheOverride : null;
            var cands = new List<string>();
            cands.AddRange(AppPaths.Glob(@"%LOCALAPPDATA%\copilot\*user*cache*.json"));
            cands.AddRange(AppPaths.Glob(@"~\.copilot\*user*cache*.json"));
            return cands.Where(File.Exists).OrderByDescending(f => File.GetLastWriteTimeUtc(f)).FirstOrDefault();
        }

        public override Detection Detect()
        {
            var d = new Detection();
            if (Directory.Exists(Path.Combine(AppPaths.Home, ".copilot")) || Directory.Exists(Path.Combine(AppPaths.LocalAppData, "copilot")))
                d.Evidence.Add("Copilot CLI");
            if (AppPaths.EditorExtensions("github.copilot").Count > 0) d.Evidence.Add("VS Code 擴充");
            if (AppPaths.Which("copilot") != null) d.Evidence.Add("copilot 指令");
            d.Installed = d.Evidence.Count > 0;
            if (FindCache() == null) d.Hint = "執行一次 Copilot CLI 就會產生額度快取";
            return d;
        }

        static string LabelFor(string key)
        {
            switch (key)
            {
                case "premium_interactions": return "進階請求";
                case "chat": return "聊天";
                case "completions": return "程式補全";
                default: return key.Replace('_', ' ');
            }
        }

        static string ShortFor(string key)
        {
            switch (key)
            {
                case "premium_interactions": return "PR";
                case "chat": return "聊";
                case "completions": return "補";
                default: return key.Length > 2 ? key.Substring(0, 2) : key;
            }
        }

        static string PlanName(string sku, string plan)
        {
            string s = (sku ?? "").ToLowerInvariant();
            if (s.Contains("free")) return "Free";
            if (s.Contains("pro_plus") || s.Contains("pro+")) return "Pro+";
            if (s.Contains("pro")) return "Pro";
            if (!string.IsNullOrEmpty(plan)) return Fmt.Title(plan);
            return null;
        }

        public override Snapshot Fetch(bool force, AppSettings settings)
        {
            string f = FindCache();
            if (f == null) return Snapshot.Fail("找不到 Copilot 額度快取：執行一次 Copilot CLI 即可");
            string perr;
            var root = Json.TryParse(AppPaths.ReadShared(f), out perr);
            if (root == null) return Snapshot.Fail("Copilot 快取格式無法解析：" + perr);

            var snap = new Snapshot { Source = "Copilot CLI 快取", ObservedAt = File.GetLastWriteTimeUtc(f) };
            var reset = Json.Date(Json.FindKey(root, "quota_reset_date_utc")) ?? Json.Date(Json.FindKey(root, "quota_reset_date"))
                        ?? Json.Date(Json.FindKey(root, "limited_user_reset_date"));
            snap.Plan = PlanName(Json.Str(Json.FindKey(root, "access_type_sku")), Json.Str(Json.FindKey(root, "copilot_plan")));

            var qs = Json.Obj(Json.FindKey(root, "quota_snapshots"));
            bool anyUnlimited = false;
            if (qs != null)
            {
                foreach (var kv in qs.OrderBy(k => k.Key == "premium_interactions" ? 0 : 1))
                {
                    var q = kv.Value;
                    if (Json.Bool(Json.Get(q, "unlimited")) == true) { anyUnlimited = true; continue; }
                    double? ent = Json.Num(Json.Get(q, "entitlement"));
                    double? rem = Json.Num(Json.Get(q, "remaining")) ?? Json.Num(Json.Get(q, "quota_remaining"));
                    double? pct = Json.Num(Json.Get(q, "percent_remaining"));
                    if (!pct.HasValue && ent.HasValue && ent.Value > 0 && rem.HasValue) pct = rem.Value / ent.Value * 100;
                    if (!pct.HasValue) continue;
                    if (ent.HasValue && ent.Value <= 0 && kv.Key != "premium_interactions") continue;
                    var m = new Meter
                    {
                        Key = kv.Key,
                        Label = LabelFor(kv.Key),
                        ShortLabel = ShortFor(kv.Key),
                        Used = Math.Max(0, Math.Min(100, 100 - pct.Value)),
                        ResetsAt = reset,
                        WindowMinutes = 43200,
                    };
                    if (ent.HasValue && rem.HasValue) m.ValueText = Math.Floor(rem.Value) + " / " + ent.Value;
                    var ts = Json.Date(Json.Get(q, "timestamp_utc"));
                    if (ts.HasValue && (!snap.ObservedAt.HasValue || ts.Value > snap.ObservedAt.Value)) snap.ObservedAt = ts;
                    snap.Meters.Add(m);
                }
            }

            // Copilot Free: remaining counts + monthly totals
            var limited = Json.Obj(Json.FindKey(root, "limited_user_quotas"));
            var monthly = Json.Obj(Json.FindKey(root, "monthly_quotas"));
            if (snap.Meters.Count == 0 && limited != null && monthly != null)
            {
                foreach (var kv in monthly)
                {
                    double? total = Json.Num(kv.Value);
                    double? rem = Json.Num(Json.Get(limited, kv.Key));
                    if (!total.HasValue || total.Value <= 0 || !rem.HasValue) continue;
                    snap.Meters.Add(new Meter
                    {
                        Key = kv.Key,
                        Label = LabelFor(kv.Key),
                        ShortLabel = ShortFor(kv.Key),
                        Used = Math.Max(0, Math.Min(100, (total.Value - rem.Value) / total.Value * 100)),
                        ResetsAt = reset,
                        WindowMinutes = 43200,
                        ValueText = rem.Value + " / " + total.Value,
                    });
                }
            }

            if (snap.Meters.Count == 0)
            {
                if (anyUnlimited)
                    snap.Meters.Add(new Meter { Key = "unlimited", Label = "無限制", ShortLabel = "∞", Unlimited = true, ValueText = "∞" });
                else
                    return Snapshot.Fail("Copilot 快取裡沒有額度資訊");
            }

            // Past the monthly reset → the quota is fresh again.
            var now = DateTime.UtcNow;
            foreach (var m in snap.Meters)
            {
                if (m.ResetsAt.HasValue && m.ResetsAt.Value <= now)
                {
                    m.Used = 0;
                    m.WasReset = true;
                    m.ResetsAt = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                    m.ResetApprox = true;
                }
            }
            if (snap.ObservedAt.HasValue && (now - snap.ObservedAt.Value).TotalDays > 2)
            {
                snap.Stale = true;
                snap.Note = "快取來自 " + Fmt.Ago(snap.ObservedAt) + "（Copilot CLI 使用時才會更新）";
            }
            return snap;
        }
    }

    /// <summary>Local models via Ollama: no quota, but it is nice to see what is loaded.</summary>
    class OllamaProvider : Provider
    {
        public OllamaProvider()
        {
            Id = "ollama";
            Name = "Ollama";
            Mascot = "llama";
            Color = Palette.Hex("#C9B79C");
        }

        public override int IntervalSeconds { get { return 30; } }

        static string FindExe()
        {
            foreach (var p in new[] { @"%LOCALAPPDATA%\Programs\Ollama\ollama.exe", @"%ProgramFiles%\Ollama\ollama.exe" })
                if (File.Exists(AppPaths.Expand(p))) return AppPaths.Expand(p);
            return AppPaths.Which("ollama");
        }

        public override Detection Detect()
        {
            var d = new Detection();
            if (FindExe() != null) d.Evidence.Add("Ollama 程式");
            bool models = Directory.Exists(Path.Combine(AppPaths.Home, ".ollama", "models"));
            d.Installed = d.Evidence.Count > 0;
            if (models) d.Evidence.Add("模型資料夾");
            if (!d.Installed) d.Hint = models ? "只找到模型資料夾，Ollama 程式可能已移除" : null;
            return d;
        }

        public override Snapshot Fetch(bool force, AppSettings settings)
        {
            int status;
            string ps;
            try { ps = Net.Request("GET", "http://127.0.0.1:11434/api/ps", null, null, 1500, out status); }
            catch { return new Snapshot { Offline = true, Error = "Ollama 沒有在執行" }; }
            var loaded = Json.Arr(Json.Get(Json.TryParse(ps), "models"));
            int installed = 0;
            try
            {
                var tags = Net.Request("GET", "http://127.0.0.1:11434/api/tags", null, null, 1500, out status);
                var arr = Json.Arr(Json.Get(Json.TryParse(tags), "models"));
                if (arr != null) installed = arr.Count;
            }
            catch { }
            var snap = new Snapshot { Source = "本機 Ollama 服務", ObservedAt = DateTime.UtcNow, Plan = "本機" };
            snap.Meters.Add(new Meter { Key = "local", Label = "本機模型", ShortLabel = "∞", Unlimited = true, ValueText = "∞" });
            var names = loaded == null ? new List<string>() : loaded.Cast<object>().Select(m => Json.Str(Json.Get(m, "name"))).Where(n => n != null).ToList();
            snap.Note = installed + " 個模型" + (names.Count > 0 ? "，載入中：" + string.Join("、", names) : "，目前閒置");
            snap.Active = names.Count > 0;
            return snap;
        }
    }
}
