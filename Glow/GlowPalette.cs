using System.Collections.Generic;
using System.Windows.Media;

namespace SynthWaveVS.Glow
{
    /// <summary>One layer of a CSS text-shadow: "0 0 {Blur}px {Color}".</summary>
    internal readonly struct GlowShadow
    {
        public GlowShadow(double blur, Color color, bool neon)
        {
            Blur = blur;
            Color = color;
            Neon = neon;
        }

        public double Blur { get; }

        public Color Color { get; }

        /// <summary>Neon layers take their opacity from the brightness option, like [NEON_BRIGHTNESS] in VS Code.</summary>
        public bool Neon { get; }

        public Color WithBrightness(double brightness) =>
            Neon ? Color.FromArgb((byte)(brightness * 255), Color.R, Color.G, Color.B) : Color;
    }

    /// <summary>
    /// The text-shadow rules of synthwave84.css (VS Code "Neon Dreams"), keyed by the Neon Dream
    /// text color they apply to.
    /// </summary>
    internal static class GlowPalette
    {
        private static readonly Dictionary<Color, GlowShadow[]> Shadows = new Dictionary<Color, GlowShadow[]>
        {
            // Keywords
            [Rgb(0xF4EEE4)] = new[] { Dark(2, 0x393A33), Neon(8, 0xF39F05), Neon(2, 0xF39F05) },
            // Types, constants, this
            [Rgb(0xFFF5F6)] = new[] { Dark(2, 0x000000), Neon(10, 0xFC1F2C), Neon(5, 0xFC1F2C), Neon(25, 0xFC1F2C) },
            // Functions
            [Rgb(0xFDFDFD)] = new[] { Dark(2, 0x001716), Neon(3, 0x03EDF9), Neon(5, 0x03EDF9), Neon(8, 0x03EDF9) },
            // Tags
            [Rgb(0x72F1B8)] = new[] { Dark(2, 0x100C0F), Neon(10, 0x257C55), Neon(35, 0x212724) },
            // Variables
            [Rgb(0xF92AAD)] = new[] { Dark(2, 0x100C0F), Fixed(5, 0x33, 0xDC078E), Fixed(10, 0x33, 0xFFFFFF) },
        };

        public static bool TryGetShadows(Color text, out GlowShadow[] shadows) =>
            Shadows.TryGetValue(Color.FromRgb(text.R, text.G, text.B), out shadows);

        private static GlowShadow Dark(double blur, int rgb) => new GlowShadow(blur, Rgb(rgb), false);

        private static GlowShadow Neon(double blur, int rgb) => new GlowShadow(blur, Rgb(rgb), true);

        private static GlowShadow Fixed(double blur, byte alpha, int rgb)
        {
            Color color = Rgb(rgb);
            return new GlowShadow(blur, Color.FromArgb(alpha, color.R, color.G, color.B), false);
        }

        private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
