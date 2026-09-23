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
            sb.AppendLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
            if (outFile != null) File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
            return failures;
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
