using System;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace SynthWaveVS.Glow
{
    /// <summary>
    /// Tells whether the active theme wants the glow: only NeonDream.vstheme defines the
    /// SynthWave/NeonGlow color (see tools/generate-themes.py).
    /// </summary>
    internal static class GlowTheme
    {
        private static readonly ThemeResourceKey Marker =
            new ThemeResourceKey(new Guid("cab52692-d5e4-4f3a-a97a-10fbb0b9859f"), "NeonGlow", ThemeResourceKeyType.BackgroundColor);

        public static bool IsActive
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    return VSColorTheme.GetThemedColor(Marker).A != 0;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }
}
