using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SentriPet
{
    /// <summary>The tray / menu-bar icon: a tiny jelly whose fill shows the lowest remaining quota (same drawing as the WPF version).</summary>
    static class TrayArt
    {
        /// <param name="remaining">lowest remaining %, or -1 when unknown</param>
        /// <param name="active">an AI is working (a blue dot)</param>
        public static Bitmap Draw(double remaining, bool active, int size = 32)
        {
            var rtb = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            double k = size / 32.0;
            var body = new StreamGeometry();
            using (var g = body.Open())
            {
                // gumdrop: flat bottom, dome top (same silhouette as the pet)
                g.BeginFigure(new Point(5 * k, 29 * k), true);
                g.CubicBezierTo(new Point(1.5 * k, 29 * k), new Point(1.5 * k, 25 * k), new Point(1.5 * k, 21 * k));
                g.CubicBezierTo(new Point(1.5 * k, 9 * k), new Point(8 * k, 2.5 * k), new Point(16 * k, 2.5 * k));
                g.CubicBezierTo(new Point(24 * k, 2.5 * k), new Point(30.5 * k, 9 * k), new Point(30.5 * k, 21 * k));
                g.CubicBezierTo(new Point(30.5 * k, 25 * k), new Point(30.5 * k, 29 * k), new Point(27 * k, 29 * k));
                g.EndFigure(true);
            }
            Color level = remaining < 0 ? Color.FromRgb(150, 160, 175) :
                          remaining >= 50 ? Color.FromRgb(52, 211, 153) :
                          remaining >= 20 ? Color.FromRgb(251, 191, 36) : Color.FromRgb(248, 113, 113);
            var inkColor = Color.FromRgb(43, 33, 30);
            using (var dc = rtb.CreateDrawingContext())
            {
                dc.DrawGeometry(G.B(Color.FromRgb(245, 246, 250)), null, body);
                double frac = remaining < 0 ? 0.55 : Math.Max(0.06, remaining / 100);
                double top = (29 - frac * 26.5) * k;
                using (dc.PushGeometryClip(body))
                    dc.FillRectangle(G.B(level), new Rect(0, top, size, size));
                dc.DrawGeometry(null, new Pen(G.B(inkColor), 2.2 * k), body);
                var ink = G.B(inkColor);
                dc.DrawEllipse(ink, null, new Rect(9.5 * k, 13 * k, 4 * k, 5 * k));
                dc.DrawEllipse(ink, null, new Rect(18.5 * k, 13 * k, 4 * k, 5 * k));
                if (active)
                {
                    var dot = new Rect(22 * k, 0.5 * k, 9 * k, 9 * k);
                    dc.DrawEllipse(G.B(Color.FromRgb(59, 130, 246)), new Pen(Brushes.White, 1.4 * k), dot);
                }
            }
            return rtb;
        }
    }
}
