using System;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;

namespace SynthWaveVS.Glow
{
    /// <summary>
    /// Glow options, kept in the VS settings store so the editor can read them without
    /// loading the package. Edited in Tools > Options > SynthWave '84 > Neon Glow.
    /// </summary>
    internal static class GlowSettings
    {
        public const bool DefaultEnabled = true;
        public const int DefaultBrightness = 60; // A bit above the VS Code default (0.45): WPF text renders lighter

        private const string Collection = @"SynthWaveVS\Glow";

        private static bool loaded;
        private static bool enabled = DefaultEnabled;
        private static int brightness = DefaultBrightness;

        public static event EventHandler Changed;

        public static bool Enabled
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Load();
                return enabled;
            }
        }

        /// <summary>Opacity of the neon layers, from 0 to 100.</summary>
        public static int Brightness
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Load();
                return brightness;
            }
        }

        public static void Save(bool newEnabled, int newBrightness)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            enabled = newEnabled;
            brightness = Math.Max(0, Math.Min(100, newBrightness));
            loaded = true;

            WritableSettingsStore store = Store();
            if (!store.CollectionExists(Collection))
            {
                store.CreateCollection(Collection);
            }

            store.SetBoolean(Collection, nameof(Enabled), enabled);
            store.SetInt32(Collection, nameof(Brightness), brightness);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static void Load()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (loaded)
            {
                return;
            }

            loaded = true;
            WritableSettingsStore store = Store();
            enabled = store.GetBoolean(Collection, nameof(Enabled), DefaultEnabled);
            brightness = Math.Max(0, Math.Min(100, store.GetInt32(Collection, nameof(Brightness), DefaultBrightness)));
        }

        private static WritableSettingsStore Store()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return new ShellSettingsManager(ServiceProvider.GlobalProvider).GetWritableSettingsStore(SettingsScope.UserSettings);
        }
    }
}
