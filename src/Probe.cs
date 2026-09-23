using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SentriPet
{
    /// <summary>--probe [file]: detection + one fetch for every provider, written as a text report.</summary>
    static class Probe
    {
        public static int Run(string outFile)
        {
            var sb = new StringBuilder();
            var settings = AppSettings.Load();
            sb.AppendLine(App.DisplayName + " " + App.Version + " probe @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (var p in ProviderRegistry.CreateAll())
            {
                sb.AppendLine();
                Detection d;
                try { d = p.Detect(); }
                catch (Exception ex) { sb.AppendLine("[" + p.Id + "] detect error: " + ex.Message); continue; }
                sb.AppendLine("[" + p.Id + "] " + p.Name + " installed=" + d.Installed + " evidence=" + d.EvidenceText + (d.Hint != null ? " hint=" + d.Hint : ""));
                if (!d.Installed) continue;
                var t0 = DateTime.UtcNow;
                Snapshot s;
                try { s = p.Fetch(true, settings); }
                catch (Exception ex) { sb.AppendLine("  fetch exception: " + ex); continue; }
                sb.AppendLine("  took " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms; source=" + s.Source + " plan=" + s.Plan +
                              " observed=" + (s.ObservedAt.HasValue ? s.ObservedAt.Value.ToLocalTime().ToString("MM-dd HH:mm:ss") : "-") +
                              " stale=" + s.Stale + " active=" + s.Active + " offline=" + s.Offline);
                if (s.Error != null) sb.AppendLine("  error: " + s.Error);
                if (s.Note != null) sb.AppendLine("  note: " + s.Note);
                foreach (var m in s.Meters)
                {
                    sb.AppendLine(string.Format("  - {0,-12} used {1,5:0.#}%{8}  remaining {2,5:0.#}%  window {3}m  resets {4}{5}{6}{7}",
                        m.Label, m.Used, m.Remaining, m.WindowMinutes,
                        m.ResetsAt.HasValue ? m.ResetsAt.Value.ToLocalTime().ToString("MM-dd HH:mm") + " (" + Fmt.Countdown(m.ResetsAt) + ")" : "-",
                        m.ResetApprox ? " ≈" : "", m.WasReset ? " [rolled-over]" : "", m.ValueText != null ? "  " + m.ValueText : "",
                        m.UsedApprox ? " (estimated)" : ""));
                }
                var cp = p as ClaudeProvider;
                if (cp != null)
                    sb.AppendLine(string.Format("  calibration: 5h {0}   weekly {1}",
                        cp.LastK5.HasValue ? (1 / cp.LastK5.Value / 1e6).ToString("0.000") + "M weighted tokens per 1%" : "n/a",
                        cp.LastK7.HasValue ? (1 / cp.LastK7.Value / 1e6).ToString("0.000") + "M per 1%" : "n/a"));
                p.Dispose();
            }
            sb.AppendLine();
            sb.AppendLine("[catalog]");
            foreach (var c in CatalogEntry.All)
            {
                var d = c.Detect();
                if (d.Installed) sb.AppendLine("  " + c.Name + ": " + d.EvidenceText);
            }
            string text = sb.ToString();
            if (outFile != null) File.WriteAllText(outFile, text, new UTF8Encoding(false));
            else Console.Write(text);
            return 0;
        }
    }
}
