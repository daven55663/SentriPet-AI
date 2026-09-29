using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Threading;

namespace SentriPet
{
    /// <summary>
    /// --perf-test FILE [--perf-seconds N] [--perf-scale 1.3] [--perf-themes glass,pet]: runs the real widget and measures its CPU use for every theme at 30 and
    /// 15 frames a second and with the automatic frame rate, and while hidden (#8). Writes a table and quits.
    /// Use with --dev (a separate profile); the numbers depend on the machine, so compare runs on the same one.
    /// </summary>
    class PerfTest
    {
        class Step { public string Theme, Label; public int? IntervalMs; public bool Hidden; }

        readonly DesktopController ctl;
        readonly PetWindow window;
        readonly string file;
        readonly double seconds;
        readonly List<Step> steps = new List<Step>();
        readonly List<string> lines = new List<string>();
        int index = -1;
        TimeSpan cpu0, tick0;
        int frames0;
        Stopwatch wall;

        public PerfTest(DesktopController ctl, PetWindow window, string file, double seconds, string themes)
        {
            this.ctl = ctl;
            this.window = window;
            this.file = file;
            this.seconds = seconds;
            var only = string.IsNullOrEmpty(themes) ? null : new HashSet<string>(themes.Split(','));
            foreach (var t in ThemeCatalog.All)
            {
                if (only != null && !only.Contains(t.Id)) continue;
                steps.Add(new Step { Theme = t.Id, Label = "30 fps", IntervalMs = 33 });
                steps.Add(new Step { Theme = t.Id, Label = "15 fps", IntervalMs = 66 });
                steps.Add(new Step { Theme = t.Id, Label = "1 fps", IntervalMs = 1000 });
                steps.Add(new Step { Theme = t.Id, Label = "auto" });
            }
            if (only == null) steps.Add(new Step { Theme = "pet", Label = "hidden", Hidden = true });
        }

        public void Start()
        {
            lines.Add(string.Format(CultureInfo.InvariantCulture, "{0,-10} {1,-7} {2,9} {3,9} {4,8}", "theme", "frames", "CPU %core", "tick %", "fps"));
            After(3, Next);   // let the data arrive first
        }

        static void After(double s, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(s) };
            t.Tick += (x, e) => { t.Stop(); a(); };
            t.Start();
        }

        static TimeSpan Cpu() { return Process.GetCurrentProcess().TotalProcessorTime; }

        void Next()
        {
            index++;
            if (index >= steps.Count) { Finish(); return; }
            var s = steps[index];
            if (ctl.Settings.Theme != s.Theme) ctl.ChangeTheme(s.Theme);
            window.FixedIntervalMs = s.IntervalMs;
            window.UserHidden = s.Hidden;
            window.UpdateVisibility();
            After(1.5, () =>
            {
                cpu0 = Cpu();
                tick0 = window.TickTime;
                frames0 = window.Frames;
                wall = Stopwatch.StartNew();
                After(seconds, Measure);
            });
        }

        void Measure()
        {
            var s = steps[index];
            double ms = wall.Elapsed.TotalMilliseconds;
            double cpu = (Cpu() - cpu0).TotalMilliseconds / ms * 100;
            double tick = (window.TickTime - tick0).TotalMilliseconds / ms * 100;
            double fps = (window.Frames - frames0) / (ms / 1000);
            lines.Add(string.Format(CultureInfo.InvariantCulture, "{0,-10} {1,-7} {2,9:0.0} {3,9:0.0} {4,8:0.0}", s.Theme, s.Label, cpu, tick, fps));
            Next();
        }

        void Finish()
        {
            lines.Insert(0, AppInfo.Name + " " + AppInfo.Version + " perf test on " + Os.Name + ", " + Environment.ProcessorCount + " cores, scale " +
                         ctl.Settings.Scale.ToString("0.##", CultureInfo.InvariantCulture) + ", " + ctl.Views.Count + " AI(s): " +
                         string.Join(", ", ctl.Views.Select(v => v.Id)));
            window.FixedIntervalMs = null;
            window.UserHidden = false;
            window.UpdateVisibility();
            try { File.WriteAllLines(file, lines); } catch (Exception ex) { Log.Error("perf report", ex); }
            ctl.Quit(0);
        }
    }
}
