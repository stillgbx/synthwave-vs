using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using SynthWaveVS.Glow;

namespace SynthWaveVS
{
    /// <summary>
    /// Only registers the options page: VS loads the package when the page is opened.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid("e59db2c7-4082-481f-adcf-66e66b19d569")]
    [ProvideOptionPage(typeof(GlowOptionsPage), "SynthWave '84", "Neon Glow", 0, 0, true)]
    public sealed class SynthWavePackage : AsyncPackage
    {
    }
}
