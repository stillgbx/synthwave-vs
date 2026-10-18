using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SynthWaveVS.Glow
{
    [Guid("19e32712-e307-4e28-9248-4bd59fc1cbc2")]
    public class GlowOptionsPage : DialogPage
    {
        [Category("Neon glow")]
        [DisplayName("Enable glow")]
        [Description("Draw the neon glow around the text when the SynthWave '84 Neon Dream theme is active.")]
        [DefaultValue(GlowSettings.DefaultEnabled)]
        public bool Enabled { get; set; } = GlowSettings.DefaultEnabled;

        [Category("Neon glow")]
        [DisplayName("Brightness")]
        [Description("Glow intensity, from 0 to 100.")]
        [DefaultValue(GlowSettings.DefaultBrightness)]
        public int Brightness { get; set; } = GlowSettings.DefaultBrightness;

        public override void LoadSettingsFromStorage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Enabled = GlowSettings.Enabled;
            Brightness = GlowSettings.Brightness;
        }

        public override void SaveSettingsToStorage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            GlowSettings.Save(Enabled, Brightness);
        }
    }
}
