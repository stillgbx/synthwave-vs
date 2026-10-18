using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SynthWaveVS.Glow
{
    /// <summary>One word of a text line, as the editor draws it.</summary>
    internal sealed class GlowWord
    {
        public GlowWord(FormattedText text, Point origin, GlowShadow[] shadows)
        {
            Text = text;
            Origin = origin;
            Shadows = shadows;
        }

        public FormattedText Text { get; }

        /// <summary>Top-left of the text, in the coordinates of the line.</summary>
        public Point Origin { get; }

        /// <summary>The text-shadow layers of the word, or null when it does not glow.</summary>
        public GlowShadow[] Shadows { get; }
    }

    /// <summary>
    /// Renders the glow of one text line into a bitmap, the way a browser renders CSS text-shadow:
    /// each shadow is the word blurred with a gaussian of sigma = blur / 2. The letters of the line
    /// are then cut out, so the text the editor draws below stays untouched.
    /// Everything runs on the CPU once per line: displaying and scrolling a bitmap costs nothing,
    /// unlike WPF effects that are recomputed on every frame.
    /// </summary>
    internal static class GlowRenderer
    {
        // Shadows are grouped by blur, one blurred layer per group (CSS blur radius, in DIPs).
        private static readonly double[] BlurLevels = { 2, 5, 10, 25 };

        // The thin 2px shadows (the dark outline and the neon edge) are rendered at full
        // resolution; the wider halos are smooth, half resolution is enough and four times cheaper.
        private const double TightLevel = 2;
        private const double HaloResolution = 0.5;

        /// <param name="scale">Device pixels per DIP (monitor DPI and editor zoom).</param>
        /// <param name="brightness">Opacity of the neon layers, from 0 to 1.</param>
        /// <returns>The glow and its bounds in line coordinates, or null when no word glows.</returns>
        public static (BitmapSource Bitmap, Rect Bounds)? Render(IReadOnlyList<GlowWord> words, double scale, double brightness)
        {
            List<GlowWord> glowing = words.Where(w => w.Shadows != null).ToList();
            if (glowing.Count == 0)
            {
                return null;
            }

            double[] levels = BlurLevels
                .Where(level => glowing.Any(w => w.Shadows.Any(s => Level(s.Blur) == level)))
                .ToArray();

            // A gaussian is almost gone after 2.5 sigma (sigma = blur / 2, like CSS).
            double margin = 2.5 * levels.Max() / 2;
            Rect area = glowing.Select(w => new Rect(w.Origin, new Size(w.Text.WidthIncludingTrailingWhitespace, w.Text.Height))).Aggregate(Rect.Union);
            area.Inflate(margin, margin);

            // Snap to the device pixel grid, so the cut out letters land exactly on the editor ones.
            double left = Math.Floor(area.Left * scale) / scale;
            double top = Math.Floor(area.Top * scale) / scale;
            int width = (int)Math.Ceiling((area.Right - left) * scale);
            int height = (int)Math.Ceiling((area.Bottom - top) * scale);
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var origin = new Vector(-left, -top);
            double haloScale = scale * HaloResolution;
            int haloWidth = (int)Math.Ceiling(width * HaloResolution) + 1;
            int haloHeight = (int)Math.Ceiling(height * HaloResolution) + 1;

            // Widest blur at the bottom, like the last shadow of a CSS text-shadow list.
            int[] halo = null;
            foreach (double level in levels.Where(l => l != TightLevel).OrderByDescending(l => l))
            {
                byte[] pixels = Draw(haloWidth, haloHeight, haloScale, origin, context => DrawShadows(context, glowing, level, brightness));
                int[] layer = Blur(pixels, haloWidth, haloHeight, level / 2 * haloScale);
                halo = halo == null ? layer : Over(layer, halo);
            }

            int[] tight = null;
            if (levels.Contains(TightLevel))
            {
                byte[] pixels = Draw(width, height, scale, origin, context => DrawShadows(context, glowing, TightLevel, brightness));
                tight = Blur(pixels, width, height, TightLevel / 2 * scale);
            }

            // Every letter of the line, glowing or not, is cut out of the glow. Letters only cover
            // the text band of the line: no need to render the margins at full resolution.
            int bandTop = Math.Max(0, (int)Math.Floor((words.Min(w => w.Origin.Y) - top) * scale));
            int bandBottom = Math.Min(height, (int)Math.Ceiling((words.Max(w => w.Origin.Y + w.Text.Height) - top) * scale));
            int bandHeight = Math.Max(1, bandBottom - bandTop);
            byte[] letters = Draw(width, bandHeight, scale, origin - new Vector(0, bandTop / scale), context =>
            {
                foreach (GlowWord word in words)
                {
                    word.Text.SetForegroundBrush(Brushes.White);
                    context.DrawText(word.Text, word.Origin);
                }
            });

            var output = new byte[width * height * 4];
            var pixel = new int[4];
            for (int y = 0; y < height; y++)
            {
                double hy = (y + 0.5) * HaloResolution - 0.5;
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    int keep = y < bandTop || y >= bandTop + bandHeight ? 255 : 255 - letters[((y - bandTop) * width + x) * 4 + 3];
                    if (keep == 0)
                    {
                        continue;
                    }

                    pixel[0] = pixel[1] = pixel[2] = pixel[3] = 0;
                    if (halo != null)
                    {
                        Sample(halo, haloWidth, haloHeight, (x + 0.5) * HaloResolution - 0.5, hy, pixel);
                    }

                    // The thin shadows over the wide ones.
                    if (tight != null)
                    {
                        int over = 255 - tight[i + 3];
                        for (int c = 0; c < 4; c++)
                        {
                            pixel[c] = tight[i + c] + pixel[c] * over / 255;
                        }
                    }

                    for (int c = 0; c < 4; c++)
                    {
                        output[i + c] = (byte)Math.Min(255, pixel[c] * keep / 255);
                    }
                }
            }

            BitmapSource bitmap = BitmapSource.Create(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null, output, width * 4);
            bitmap.Freeze();
            return (bitmap, new Rect(left, top, width / scale, height / scale));
        }

        // In CSS the first shadow of the list is on top: draw them in reverse order.
        private static void DrawShadows(DrawingContext context, List<GlowWord> words, double level, double brightness)
        {
            foreach (GlowWord word in words)
            {
                foreach (GlowShadow shadow in word.Shadows.Reverse().Where(s => Level(s.Blur) == level))
                {
                    word.Text.SetForegroundBrush(new SolidColorBrush(shadow.WithBrightness(brightness)));
                    context.DrawText(word.Text, word.Origin);
                }
            }
        }

        private static double Level(double blur)
        {
            foreach (double level in BlurLevels)
            {
                if (blur <= level)
                {
                    return level;
                }
            }

            return BlurLevels[BlurLevels.Length - 1];
        }

        private static byte[] Draw(int width, int height, double scale, Vector origin, Action<DrawingContext> draw)
        {
            var visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                context.PushTransform(new MatrixTransform(scale, 0, 0, scale, origin.X * scale, origin.Y * scale));
                draw(context);
                context.Pop();
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var pixels = new byte[width * height * 4];
            target.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }

        /// <summary>Gaussian blur of premultiplied BGRA pixels, approximated by three box blurs.</summary>
        private static int[] Blur(byte[] pixels, int width, int height, double sigma)
        {
            var current = new int[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                current[i] = pixels[i];
            }

            // Three box blurs of radius r have a variance of ((2r+1)^2 - 1) / 4.
            int radius = Math.Max(1, (int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2));
            var buffer = new int[current.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                Box(current, buffer, height, width, radius, 4, width * 4);
                Box(buffer, current, width, height, radius, width * 4, 4);
            }

            return current;
        }

        // One box blur along each of 'lines' runs of 'length' pixels, the four channels at once.
        private static void Box(int[] source, int[] target, int lines, int length, int radius, int step, int lineStride)
        {
            // Fixed point 1 / (2r+1), so the inner loop has no division.
            int scale = (1 << 16) / (2 * radius + 1);
            for (int line = 0; line < lines; line++)
            {
                int start = line * lineStride;
                int b = 0, g = 0, r = 0, a = 0;
                for (int k = 0; k <= radius && k < length; k++)
                {
                    int p = start + k * step;
                    b += source[p]; g += source[p + 1]; r += source[p + 2]; a += source[p + 3];
                }

                for (int i = 0; i < length; i++)
                {
                    int p = start + i * step;
                    target[p] = (b * scale) >> 16;
                    target[p + 1] = (g * scale) >> 16;
                    target[p + 2] = (r * scale) >> 16;
                    target[p + 3] = (a * scale) >> 16;

                    int add = i + radius + 1, remove = i - radius;
                    if (add < length)
                    {
                        int q = start + add * step;
                        b += source[q]; g += source[q + 1]; r += source[q + 2]; a += source[q + 3];
                    }

                    if (remove >= 0)
                    {
                        int q = start + remove * step;
                        b -= source[q]; g -= source[q + 1]; r -= source[q + 2]; a -= source[q + 3];
                    }
                }
            }
        }

        // Premultiplied "top over bottom".
        private static int[] Over(int[] top, int[] bottom)
        {
            for (int i = 0; i < top.Length; i += 4)
            {
                int keep = 255 - top[i + 3];
                for (int c = 0; c < 4; c++)
                {
                    top[i + c] += bottom[i + c] * keep / 255;
                }
            }

            return top;
        }

        // Bilinear sample of the half resolution halo (fixed point, 8 bits).
        private static void Sample(int[] halo, int width, int height, double x, double y, int[] pixel)
        {
            int x0 = Math.Max(0, Math.Min(width - 1, (int)Math.Floor(x)));
            int y0 = Math.Max(0, Math.Min(height - 1, (int)Math.Floor(y)));
            int x1 = Math.Min(width - 1, x0 + 1), y1 = Math.Min(height - 1, y0 + 1);
            int fx = (int)(Math.Max(0, Math.Min(1, x - x0)) * 256), fy = (int)(Math.Max(0, Math.Min(1, y - y0)) * 256);
            int w00 = (256 - fx) * (256 - fy), w10 = fx * (256 - fy), w01 = (256 - fx) * fy, w11 = fx * fy;
            int p00 = (y0 * width + x0) * 4, p10 = (y0 * width + x1) * 4, p01 = (y1 * width + x0) * 4, p11 = (y1 * width + x1) * 4;
            if ((halo[p00 + 3] | halo[p10 + 3] | halo[p01 + 3] | halo[p11 + 3]) == 0)
            {
                return;
            }

            for (int c = 0; c < 4; c++)
            {
                long value = (long)halo[p00 + c] * w00 + (long)halo[p10 + c] * w10 + (long)halo[p01 + c] * w01 + (long)halo[p11 + c] * w11;
                pixel[c] = (int)(value >> 16);
            }
        }
    }
}
