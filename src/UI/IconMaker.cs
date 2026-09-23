using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SentriPet
{
    /// <summary>Draws the app icon (a mint jelly pet) and writes a multi-size .ico: --make-icon path.ico</summary>
    static class IconMaker
    {
        public static int Run(string[] args)
        {
            string path = args.Length > 1 ? args[1] : "app.ico";
            var sizes = new[] { 256, 64, 48, 40, 32, 24, 20, 16 };
            var pngs = new List<byte[]>();
            foreach (var s in sizes)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(Render(s)));
                using (var ms = new MemoryStream())
                {
                    enc.Save(ms);
                    pngs.Add(ms.ToArray());
                }
            }
            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0);
                bw.Write((short)1);
                bw.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    bw.Write(dim);
                    bw.Write(dim);
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((short)1);
                    bw.Write((short)32);
                    bw.Write(pngs[i].Length);
                    bw.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) bw.Write(p);
            }
            File.WriteAllBytes(Path.ChangeExtension(path, ".png"), pngs[0]);
            return 0;
        }

        public static BitmapSource Render(int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) Draw(dc, size);
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        static void Draw(DrawingContext dc, double size)
        {
            bool tiny = size <= 24;
            dc.PushTransform(new ScaleTransform(size / 100.0, size / 100.0));
            var ink = Palette.Brush("#1E3A34");
            var body = Geometry.Parse("M 14,66 C 4,66 0,58 0,48 C 0,20 18,0 40,0 C 62,0 80,20 80,48 C 80,58 76,66 66,66 Z");
            var tf = new TransformGroup();
            tf.Children.Add(new ScaleTransform(1.1, 1.1));
            tf.Children.Add(new TranslateTransform(6, tiny ? 22 : 20));
            var shape = body.Clone();
            shape.Transform = tf;

            if (!tiny) dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), null, new Point(50, 94), 36, 4.5);
            dc.DrawGeometry(G.Vertical(Palette.Hex("#F2FFFA"), Palette.Hex("#D3F5E8")), null, shape);
            dc.PushClip(shape);
            var wave = Geometry.Parse("M -5,52 Q 12,46 28,52 T 62,52 T 96,52 T 130,52 L 130,110 L -5,110 Z");
            dc.DrawGeometry(G.Vertical(Palette.Hex("#86EFC5"), Palette.Hex("#22C38E"), Palette.Hex("#0E9F74")), null, wave);
            dc.Pop();
            dc.DrawGeometry(null, new Pen(ink, tiny ? 6 : 4) { LineJoin = PenLineJoin.Round }, shape);
            if (!tiny)
            {
                var gloss = new EllipseGeometry(new Point(26, 34), 11, 5) { Transform = new RotateTransform(-28, 26, 34) };
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), null, gloss);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x6F, 0x91)), null, new Point(27, 64), 6.5, 3.5);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x6F, 0x91)), null, new Point(73, 64), 6.5, 3.5);
            }
            double ey = tiny ? 55 : 54;
            dc.DrawEllipse(ink, null, new Point(36, ey), tiny ? 6 : 5, tiny ? 7 : 6.2);
            dc.DrawEllipse(ink, null, new Point(64, ey), tiny ? 6 : 5, tiny ? 7 : 6.2);
            if (!tiny)
            {
                dc.DrawEllipse(Brushes.White, null, new Point(34.3, 51.6), 1.9, 2);
                dc.DrawEllipse(Brushes.White, null, new Point(62.3, 51.6), 1.9, 2);
                var smile = Geometry.Parse("M 44,66 Q 50,72 56,66");
                dc.DrawGeometry(null, new Pen(ink, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, smile);
                var star = G.Star(new Point(84, 16), 11, 4, 4, 0);
                dc.DrawGeometry(Palette.Brush("#FFD166"), new Pen(Palette.Brush("#D49A00"), 1.6), star);
            }
            dc.Pop();
        }
    }
}
