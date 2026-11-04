using System;
using Avalonia;
using Avalonia.Media.Imaging;

namespace SentriPet
{
    /// <summary>
    /// The frames of an animated GIF, composed the way a browser shows them and each with how long it shows.
    /// Decoded once with SkiaSharp, which Avalonia draws with anyway; frames bigger than <c>most</c> pixels on a side are
    /// scaled down to that (a 480×480 GIF of 38 frames would otherwise take 35 MB).
    /// </summary>
    sealed class GifFrames : IDisposable
    {
        public Bitmap[] Images;
        public int[] Ms;

        /// <summary>The frames of a GIF, or null for a still picture, one frame or a file that can't be read.</summary>
        public static GifFrames Decode(string path, double most)
        {
            if (path == null || !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return null;
            using (var codec = SkiaSharp.SKCodec.Create(path))
            {
                int n = codec != null ? codec.FrameCount : 0;
                if (n <= 1) return null;
                var info = new SkiaSharp.SKImageInfo(codec.Info.Width, codec.Info.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
                double k = Math.Min(1, most / Math.Max(info.Width, info.Height));
                var shown = new SkiaSharp.SKImageInfo(Math.Max(1, (int)Math.Round(info.Width * k)), Math.Max(1, (int)Math.Round(info.Height * k)),
                                                      SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
                var sampling = new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Linear);
                var frameInfo = codec.FrameInfo;
                // a full-size frame is kept only as long as a later frame is drawn on top of it
                var lastUse = new int[n];
                for (int i = 0; i < n; i++)
                {
                    lastUse[i] = -1;
                    int r = frameInfo[i].RequiredFrame;
                    if (r >= 0 && r < i) lastUse[r] = i;
                }
                var composed = new SkiaSharp.SKBitmap[n];
                var f = new GifFrames { Images = new Bitmap[n], Ms = new int[n] };
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        int prior = frameInfo[i].RequiredFrame;
                        SkiaSharp.SKBitmap bmp;
                        if (prior >= 0 && prior < i && composed[prior] != null) bmp = composed[prior].Copy();
                        else { bmp = new SkiaSharp.SKBitmap(info); bmp.Erase(SkiaSharp.SKColors.Transparent); prior = -1; }
                        codec.GetPixels(info, bmp.GetPixels(), new SkiaSharp.SKCodecOptions(i, prior));
                        composed[i] = bmp;
                        using (var small = k < 1 ? bmp.Resize(shown, sampling) : null)
                        {
                            var src = small ?? bmp;
                            f.Images[i] = new Bitmap(Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul, src.GetPixels(),
                                                     new PixelSize(src.Width, src.Height), new Vector(96, 96), src.RowBytes);
                        }
                        // browsers show very short frames at least 20 ms; a frame held for minutes (a GIF meant to play once)
                        // is shown at most 4 s, so the pet keeps moving
                        f.Ms[i] = Math.Min(4000, Math.Max(20, frameInfo[i].Duration));
                        for (int j = 0; j <= i; j++)
                            if (composed[j] != null && lastUse[j] <= i) { composed[j].Dispose(); composed[j] = null; }
                    }
                }
                finally { foreach (var b in composed) if (b != null) b.Dispose(); }
                return f;
            }
        }

        /// <summary>The frame to show <paramref name="ms"/> milliseconds into the animation (it loops).</summary>
        public int FrameAt(double ms)
        {
            int total = 0;
            foreach (var m in Ms) total += m;
            if (total <= 0) return 0;
            double t = ms % total;
            for (int i = 0; i < Ms.Length; i++)
            {
                if (t < Ms[i]) return i;
                t -= Ms[i];
            }
            return Ms.Length - 1;
        }

        public void Dispose()
        {
            if (Images != null) foreach (var b in Images) if (b != null) b.Dispose();
            Images = null;
        }
    }
}
