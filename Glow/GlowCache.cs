using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SynthWaveVS.Glow
{
    /// <summary>
    /// Rendered glows, shared by every editor: scrolling back to a line, or opening the same
    /// code again, reuses its bitmap. Least recently used glows go first past the memory budget.
    /// </summary>
    internal static class GlowCache
    {
        private const long Budget = 64L * 1024 * 1024;

        private static readonly Dictionary<string, LinkedListNode<Entry>> Entries = new Dictionary<string, LinkedListNode<Entry>>();
        private static readonly LinkedList<Entry> Recent = new LinkedList<Entry>();
        private static long size;

        /// <summary>Null bitmap: the line was rendered and has no glow.</summary>
        public static bool TryGet(string key, out BitmapSource bitmap, out Rect bounds)
        {
            if (Entries.TryGetValue(key, out LinkedListNode<Entry> node))
            {
                Recent.Remove(node);
                Recent.AddFirst(node);
                bitmap = node.Value.Bitmap;
                bounds = node.Value.Bounds;
                return true;
            }

            bitmap = null;
            bounds = Rect.Empty;
            return false;
        }

        public static void Add(string key, BitmapSource bitmap, Rect bounds)
        {
            if (Entries.ContainsKey(key))
            {
                return;
            }

            var entry = new Entry(key, bitmap, bounds);
            Entries[key] = Recent.AddFirst(entry);
            size += entry.Size;
            while (size > Budget && Recent.Last != null)
            {
                Entry oldest = Recent.Last.Value;
                Recent.RemoveLast();
                Entries.Remove(oldest.Key);
                size -= oldest.Size;
            }
        }

        public static void Clear()
        {
            Entries.Clear();
            Recent.Clear();
            size = 0;
        }

        private sealed class Entry
        {
            public Entry(string key, BitmapSource bitmap, Rect bounds)
            {
                Key = key;
                Bitmap = bitmap;
                Bounds = bounds;
                Size = key.Length * 2 + (bitmap == null ? 0 : (long)bitmap.PixelWidth * bitmap.PixelHeight * 4);
            }

            public string Key { get; }

            public BitmapSource Bitmap { get; }

            public Rect Bounds { get; }

            public long Size { get; }
        }
    }
}
