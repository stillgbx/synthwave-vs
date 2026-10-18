using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;

namespace SynthWaveVS.Glow
{
    /// <summary>
    /// Shows the glow of each visible line of an editor. The editor paints its text lines opaque,
    /// so the glow sits in a layer above the text, with the letters cut out of it (see GlowRenderer).
    /// Glows are rendered in the background, a few lines at a time, so typing and scrolling never wait.
    /// </summary>
    internal sealed class GlowAdornment
    {
        public const string LayerName = "SynthWaveGlow";

        // Rendering time allowed per dispatcher round, so the editor stays responsive.
        private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(8);

        private readonly IWpfTextView view;
        private readonly IClassifier classifier;
        private readonly IClassificationFormatMap formatMap;
        private readonly IAdornmentLayer layer;
        private readonly List<ITextViewLine> pending = new List<ITextViewLine>();
        private readonly List<SnapshotSpan> changed = new List<SnapshotSpan>();
        private bool active;
        private bool scheduled;
        private Dictionary<IClassificationType, int> priorities;

        public GlowAdornment(IWpfTextView view, IClassifier classifier, IClassificationFormatMap formatMap)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            this.view = view;
            this.classifier = classifier;
            this.formatMap = formatMap;
            layer = view.GetAdornmentLayer(LayerName);
            active = IsActive();

            view.LayoutChanged += OnLayoutChanged;
            view.ZoomLevelChanged += OnZoomLevelChanged;
            classifier.ClassificationChanged += OnClassificationChanged;
            formatMap.ClassificationFormatMappingChanged += OnAppearanceChanged;
            VSColorTheme.ThemeChanged += OnThemeChanged;
            GlowSettings.Changed += OnAppearanceChanged;
            view.Closed += OnClosed;
        }

        private static bool IsActive()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GlowSettings.Enabled && GlowTheme.IsActive;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            view.LayoutChanged -= OnLayoutChanged;
            view.ZoomLevelChanged -= OnZoomLevelChanged;
            classifier.ClassificationChanged -= OnClassificationChanged;
            formatMap.ClassificationFormatMappingChanged -= OnAppearanceChanged;
            VSColorTheme.ThemeChanged -= OnThemeChanged;
            GlowSettings.Changed -= OnAppearanceChanged;
            view.Closed -= OnClosed;
            pending.Clear();
            changed.Clear();
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Reformatted lines lose their text-relative adornments, so only those need drawing.
            Queue(e.NewOrReformattedLines);
        }

        private void OnClassificationChanged(object sender, ClassificationChangedEventArgs e)
        {
            // Semantic colors (method name, local name...) usually arrive after the first layout.
            if (!ThreadHelper.CheckAccess())
            {
                // Fire and forget on purpose: an event handler has nothing to await it.
#pragma warning disable VSSDK007
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    OnClassificationChanged(sender, e);
                }).FileAndForget("SynthWaveVS/Glow/ClassificationChanged");
#pragma warning restore VSSDK007
                return;
            }

            if (view.IsClosed || !active)
            {
                return;
            }

            // The change comes on the visual buffer (the one with collapsed regions): bring it back
            // to the buffer of the text view lines. Lines are only redrawn once the layout is over.
            changed.AddRange(ToViewBuffer(e.ChangeSpan));
            Queue(Enumerable.Empty<ITextViewLine>());
        }

        private IEnumerable<SnapshotSpan> ToViewBuffer(SnapshotSpan span)
        {
            if (span.Snapshot.TextBuffer == view.TextBuffer)
            {
                return new[] { span };
            }

            NormalizedSnapshotSpanCollection down = view.BufferGraph.MapDownToBuffer(span, SpanTrackingMode.EdgeInclusive, view.TextBuffer);
            return down.Count > 0 ? down : view.BufferGraph.MapUpToBuffer(span, SpanTrackingMode.EdgeInclusive, view.TextBuffer);
        }

        private void OnZoomLevelChanged(object sender, ZoomLevelChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            OnAppearanceChanged(this, EventArgs.Empty);
        }

        private void OnThemeChanged(ThemeChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            GlowCache.Clear();
            OnAppearanceChanged(this, EventArgs.Empty);
        }

        private void OnAppearanceChanged(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Theme, Fonts and Colors, zoom or glow options changed: redraw every visible line.
            active = IsActive();
            priorities = null;
            pending.Clear();
            changed.Clear();
            layer.RemoveAllAdornments();
            if (!view.IsClosed && !view.InLayout)
            {
                Queue(view.TextViewLines);
            }
        }

        private void Queue(IEnumerable<ITextViewLine> lines)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!active)
            {
                return;
            }

            pending.AddRange(lines);
            if (!scheduled && (pending.Count > 0 || changed.Count > 0))
            {
                scheduled = true;
#pragma warning disable VSSDK007 // Fire and forget on purpose: nothing waits for the glow.
                ThreadHelper.JoinableTaskFactory.StartOnIdle(ProcessPending).FileAndForget("SynthWaveVS/Glow/ProcessPending");
#pragma warning restore VSSDK007
            }
        }

        private void ProcessPending()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            scheduled = false;
            if (view.IsClosed || !active)
            {
                pending.Clear();
                changed.Clear();
                return;
            }

            if (view.InLayout)
            {
                // The layout ends with a LayoutChanged event, which queues its lines again.
                pending.Clear();
                Queue(Enumerable.Empty<ITextViewLine>());
                return;
            }

            // Recolored text: its lines keep their glow until DrawGlow replaces it.
            foreach (SnapshotSpan span in changed)
            {
                pending.AddRange(view.TextViewLines.GetTextViewLinesIntersectingSpan(span.TranslateTo(view.TextSnapshot, SpanTrackingMode.EdgeInclusive)));
            }

            changed.Clear();

            // Visible lines first, top to bottom; skip lines that were reformatted meanwhile.
            List<ITextViewLine> lines = pending.Where(l => l.IsValid).Distinct().OrderBy(l => l.Top).ToList();
            pending.Clear();

            var watch = Stopwatch.StartNew();
            for (int i = 0; i < lines.Count; i++)
            {
                if (watch.Elapsed > Slice)
                {
                    Queue(lines.Skip(i));
                    return;
                }

                DrawGlow(lines[i]);
            }
        }

        private void DrawGlow(ITextViewLine line)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(line is IWpfTextViewLine wpfLine) || line.Extent.IsEmpty)
            {
                return;
            }

            double scale = VisualTreeHelper.GetDpi(view.VisualElement).PixelsPerDip * view.ZoomLevel / 100;

            // Line coordinates start on a device pixel, so cached glows stay pixel aligned anywhere.
            double lineTop = Math.Floor(line.Top * scale) / scale;
            List<Segment> segments = Segments(line);
            if (!segments.Any(s => s.Shadows != null))
            {
                layer.RemoveAdornmentsByVisualSpan(line.Extent);
                return;
            }

            double brightness = GlowSettings.Brightness / 100.0;
            string key = Key(segments, line, lineTop, scale, brightness);
            if (!GlowCache.TryGet(key, out BitmapSource bitmap, out Rect bounds))
            {
                double pixelsPerDip = VisualTreeHelper.GetDpi(view.VisualElement).PixelsPerDip;
                TextFormattingMode formattingMode = TextOptions.GetTextFormattingMode(view.VisualElement);
                var words = new List<GlowWord>(segments.Count);
                foreach (Segment segment in segments)
                {
                    var formatted = new FormattedText(
                        segment.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, segment.Properties.Typeface,
                        segment.Properties.FontRenderingEmSize, Brushes.White, null, formattingMode, pixelsPerDip);

                    // Same baseline as the text the editor drew.
                    var origin = new Point(segment.Left, line.TextTop + line.Baseline - formatted.Baseline - lineTop);
                    words.Add(new GlowWord(formatted, origin, segment.Shadows));
                }

                var glow = GlowRenderer.Render(words, scale, brightness);
                bitmap = glow?.Bitmap;
                bounds = glow?.Bounds ?? Rect.Empty;
                GlowCache.Add(key, bitmap, bounds);
            }

            // The previous glow of the line stays until now, so recoloring does not flicker.
            layer.RemoveAdornmentsByVisualSpan(line.Extent);
            if (bitmap == null)
            {
                return;
            }

            var image = new Image { Source = bitmap, Width = bounds.Width, Height = bounds.Height, IsHitTestVisible = false };
            Canvas.SetLeft(image, bounds.Left);
            Canvas.SetTop(image, lineTop + bounds.Top);
            layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, line.Extent, null, image, null);
        }

        /// <summary>The words of the line, each in the single color the editor shows it with.</summary>
        private List<Segment> Segments(ITextViewLine line)
        {
            SnapshotSpan extent = line.Extent;
            string text = extent.GetText();
            IClassificationType[] colors = ResolveColors(extent);
            var segments = new List<Segment>();

            // One word at a time, so each one starts exactly where the editor drew it.
            for (int i = 0; i < text.Length;)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                IClassificationType type = colors[i];
                while (i < text.Length && colors[i] == type && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                var properties = type == null ? formatMap.DefaultTextProperties : formatMap.GetTextProperties(type);
                GlowShadow[] shadows = null;
                if (properties.ForegroundBrush is SolidColorBrush brush)
                {
                    GlowPalette.TryGetShadows(brush.Color, out shadows);
                }

                double left = ((IWpfTextViewLine)line).GetCharacterBounds(extent.Start + start).Left;
                segments.Add(new Segment(text.Substring(start, i - start), left, properties, shadows));
            }

            return segments;
        }

        private static string Key(List<Segment> segments, ITextViewLine line, double lineTop, double scale, double brightness)
        {
            var key = new StringBuilder();
            key.Append(scale.ToString("R", CultureInfo.InvariantCulture)).Append('|')
               .Append(brightness.ToString("R", CultureInfo.InvariantCulture)).Append('|')
               .Append((line.TextTop + line.Baseline - lineTop).ToString("F2", CultureInfo.InvariantCulture));
            foreach (Segment segment in segments)
            {
                var properties = segment.Properties;
                key.Append('|').Append(segment.Left.ToString("F2", CultureInfo.InvariantCulture))
                   .Append(':').Append(segment.Text)
                   .Append(':').Append(properties.Typeface.FontFamily.Source)
                   .Append(':').Append(properties.Typeface.Weight).Append(properties.Typeface.Style)
                   .Append(':').Append(properties.FontRenderingEmSize.ToString("R", CultureInfo.InvariantCulture))
                   .Append(':').Append(segment.Shadows == null ? "-" : ((SolidColorBrush)properties.ForegroundBrush).Color.ToString());
            }

            return key.ToString();
        }

        /// <summary>
        /// The classification whose foreground the editor shows for each character of the line.
        /// Roslyn often stacks several classifications on one word (identifier, then field name...):
        /// like the editor, keep the highest priority one that sets a foreground.
        /// </summary>
        private IClassificationType[] ResolveColors(SnapshotSpan extent)
        {
            var colors = new IClassificationType[extent.Length];
            var ranks = new int[extent.Length];
            foreach (ClassificationSpan classified in classifier.GetClassificationSpans(extent))
            {
                IClassificationType type = classified.ClassificationType;
                SnapshotSpan? span = classified.Span.Intersection(extent);
                if (span == null || !SetsForeground(type))
                {
                    continue;
                }

                int rank = Rank(type);
                for (int i = span.Value.Start - extent.Start; i < span.Value.End - extent.Start; i++)
                {
                    if (colors[i] == null || rank >= ranks[i])
                    {
                        colors[i] = type;
                        ranks[i] = rank;
                    }
                }
            }

            return colors;
        }

        // Several classifications on the same text come as one transient type (for example
        // "method name;static symbol"): it has no color of its own but shows the merged one.
        private bool SetsForeground(IClassificationType type)
        {
            if (!formatMap.GetExplicitTextProperties(type).ForegroundBrushEmpty)
            {
                return true;
            }

            return type.BaseTypes.Any()
                && formatMap.GetTextProperties(type).ForegroundBrush is SolidColorBrush merged
                && formatMap.DefaultTextProperties.ForegroundBrush is SolidColorBrush plain
                && merged.Color != plain.Color;
        }

        private int Rank(IClassificationType type)
        {
            if (priorities == null)
            {
                priorities = new Dictionary<IClassificationType, int>();
                for (int i = 0; i < formatMap.CurrentPriorityOrder.Count; i++)
                {
                    if (formatMap.CurrentPriorityOrder[i] != null)
                    {
                        priorities[formatMap.CurrentPriorityOrder[i]] = i;
                    }
                }
            }

            return priorities.TryGetValue(type, out int rank) ? rank : -1;
        }

        private sealed class Segment
        {
            public Segment(string text, double left, Microsoft.VisualStudio.Text.Formatting.TextFormattingRunProperties properties, GlowShadow[] shadows)
            {
                Text = text;
                Left = left;
                Properties = properties;
                Shadows = shadows;
            }

            public string Text { get; }

            public double Left { get; }

            public Microsoft.VisualStudio.Text.Formatting.TextFormattingRunProperties Properties { get; }

            public GlowShadow[] Shadows { get; }
        }
    }
}
