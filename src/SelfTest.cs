using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

namespace SentriPet
{
    /// <summary>--selftest [file]: checks where the hover card lands in a few layouts (three 1920×1080 monitors).</summary>
    static class SelfTest
    {
        public static int Run(string outFile)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var sb = new StringBuilder();
            // a real card, so the size is what users will see
            var card = DetailCardView.Build(Snapshots.MockA()[0]);
            card.Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Ceiling(card.Root.DesiredSize.Width), h = Math.Ceiling(card.Root.DesiredSize.Height);
            double margin = DetailCardView.Margin, gap = 6 - margin;
            sb.AppendLine("card window " + w + "x" + h + " px, transparent margin " + margin + ", gap " + gap);

            var left = new Rect(-1920, 0, 1920, 1032);
            var primary = new Rect(0, 0, 1920, 1032);
            var right = new Rect(1920, 0, 1920, 1032);
            int failures = 0;
            failures += Case(sb, "主螢幕右下角（預設）", primary, new Rect(1538, 852, 360, 164), new Rect(1538, 852, 120, 164), w, h, gap, margin);
            failures += Case(sb, "主螢幕左上角", primary, new Rect(22, 64, 360, 164), new Rect(22, 64, 120, 164), w, h, gap, margin);
            failures += Case(sb, "主螢幕正中央", primary, new Rect(780, 450, 360, 164), new Rect(900, 450, 120, 164), w, h, gap, margin);
            failures += Case(sb, "左螢幕貼左邊", left, new Rect(-1920, 852, 360, 164), new Rect(-1920, 852, 120, 164), w, h, gap, margin);
            failures += Case(sb, "右螢幕貼右邊", right, new Rect(3480, 852, 360, 164), new Rect(3720, 852, 120, 164), w, h, gap, margin);
            failures += Case(sb, "高清單（上下都放不下）", primary, new Rect(800, 40, 360, 950), new Rect(800, 500, 360, 120), w, h, gap, margin);
            failures += Case(sb, "高清單貼右邊", primary, new Rect(1560, 40, 360, 950), new Rect(1560, 500, 360, 120), w, h, gap, margin);

            // "use it before it resets" urgency
            sb.AppendLine();
            var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
            failures += Level(sb, "週剩 61%，還有 3 天", 10080, 39, now.AddHours(72), now, 0);
            failures += Level(sb, "週剩 61%，還有 40 小時", 10080, 39, now.AddHours(40), now, 1);
            failures += Level(sb, "週剩 25%，還有 40 小時", 10080, 75, now.AddHours(40), now, 0);
            failures += Level(sb, "週剩 61%，最後一天", 10080, 39, now.AddHours(11), now, 2);
            failures += Level(sb, "週剩 8%，最後一天", 10080, 92, now.AddHours(11), now, 0);
            failures += Level(sb, "週剩 20%，最後 3 小時", 10080, 80, now.AddHours(3), now, 3);
            failures += Level(sb, "月剩 50%，還有 30 小時", 43200, 50, now.AddHours(30), now, 1);
            failures += Level(sb, "5 小時額度不催", 300, 10, now.AddHours(1), now, 0);
            failures += Level(sb, "每日額度不催", 1440, 10, now.AddHours(3), now, 0);
            failures += Level(sb, "已經過了重置時間", 10080, 10, now.AddHours(-1), now, 0);

            // the big number stays on the 5-hour window; no nagging while another window is used up
            var snap = new Snapshot();
            snap.Meters.Add(new Meter { Key = "fh", Label = "5 小時", ShortLabel = "5h", Used = 0, WindowMinutes = 300 });
            snap.Meters.Add(new Meter { Key = "sd", Label = "每週", ShortLabel = "週", Used = 39, WindowMinutes = 10080, ResetsAt = DateTime.UtcNow.AddHours(11) });
            var v = UsageService.MakeView(new ClaudeProvider(), snap);
            failures += Check(sb, "大數字 = 5 小時（週用得比較多時也一樣）", v.Headline == snap.Meters[0] && Math.Abs(v.HeadlineRemaining - 100) < 0.01 && v.Primary == snap.Meters[1],
                "headline=" + v.Headline.Key + " " + v.HeadlineRemaining + "% primary=" + v.Primary.Key);
            failures += Check(sb, "5 小時閒置時重置列顯示週", v.ResetMeter == snap.Meters[1] && v.LabelOf(v.ResetMeter) == "週 ", "reset line=" + v.ResetMeter.Key);
            failures += Check(sb, "最後一天會催", v.UseItLevel == 2 && v.UseIt == snap.Meters[1], "level=" + v.UseItLevel + " → " + Lines.UseItAlert(v));
            snap.Meters[0].Used = 100;
            snap.Meters[0].ResetsAt = DateTime.UtcNow.AddHours(2);
            v = UsageService.MakeView(new ClaudeProvider(), snap);
            failures += Check(sb, "5 小時用完時先不催", v.UseItLevel == 0, "level=" + v.UseItLevel);
            failures += Check(sb, "5 小時用完時重置列顯示 5 小時", v.ResetMeter == snap.Meters[0], "reset line=" + v.ResetMeter.Key);
            sb.AppendLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
            if (outFile != null) File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
            return failures;
        }

        static int Level(StringBuilder sb, string name, int window, double used, DateTime reset, DateTime now, int expected)
        {
            var m = new Meter { Key = "t", Label = "t", Used = used, WindowMinutes = window, ResetsAt = reset };
            int got = ProviderView.UseItLevelFor(m, now);
            return Check(sb, name, got == expected, "level=" + got + " expected=" + expected);
        }

        static int Check(StringBuilder sb, string name, bool ok, string detail)
        {
            sb.AppendLine((ok ? "PASS " : "FAIL ") + name + "  " + detail);
            return ok ? 0 : 1;
        }

        static int Case(StringBuilder sb, string name, Rect work, Rect content, Rect provider, double w, double h, double gap, double margin)
        {
            var r = DetailPlacement.Compute(content, provider, work, w, h, gap);
            var window = new Rect(r.X, r.Y, w, h);
            var visible = window;
            visible.Inflate(-margin, -margin);
            bool inside = window.Left >= work.Left - 0.5 && window.Right <= work.Right + 0.5 && window.Top >= work.Top - 0.5 && window.Bottom <= work.Bottom + 0.5;
            bool overlaps = visible.IntersectsWith(content);
            double arrowAt = r.X + r.PointerX, target = provider.Left + provider.Width / 2;
            bool pointsAt = r.Side == DetailPlacement.Side.Left || r.Side == DetailPlacement.Side.Right || Math.Abs(arrowAt - target) < 1 || r.PointerX <= margin + 18 || r.PointerX >= w - margin - 18;
            bool ok = inside && !overlaps && pointsAt;
            sb.AppendLine(string.Format("{0} {1,-14} side={2,-5} window=({3},{4}) {5}x{6}  inWorkArea={7} overlapsPet={8} arrowX={9:0}",
                ok ? "PASS" : "FAIL", name, r.Side, r.X, r.Y, w, h, inside, overlaps, r.PointerX));
            return ok ? 0 : 1;
        }
    }
}
