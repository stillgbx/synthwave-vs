using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SynthWaveVS.Glow
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class GlowTextViewCreationListener : IWpfTextViewCreationListener
    {
#pragma warning disable 649, 169
        // Above the text: the editor paints text lines opaque, which would hide a glow drawn below.
        [Export(typeof(AdornmentLayerDefinition))]
        [Name(GlowAdornment.LayerName)]
        [Order(After = PredefinedAdornmentLayers.Text, Before = PredefinedAdornmentLayers.Caret)]
        private AdornmentLayerDefinition glowLayer;
#pragma warning restore 649, 169

        [Import]
        internal IViewClassifierAggregatorService ClassifierAggregatorService { get; set; }

        [Import]
        internal IClassificationFormatMapService ClassificationFormatMapService { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            new GlowAdornment(
                textView,
                ClassifierAggregatorService.GetClassifier(textView),
                ClassificationFormatMapService.GetClassificationFormatMap(textView));
        }
    }
}
