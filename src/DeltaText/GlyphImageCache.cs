using System.Diagnostics.CodeAnalysis;
using Delta.Text.Contract;

namespace Delta.Text;

internal sealed class GlyphImageCache
{
    private const int MaxEntries = 256;
    private const int MaxBytes = 8 * 1024 * 1024;

    private readonly Dictionary<GlyphImageCacheKey, GlyphImage> _images = new();
    private readonly Queue<GlyphImageCacheKey> _order = new();
    private int _bytes;

    internal bool TryGet(
        in GlyphImageCacheKey key,
        [NotNullWhen(true)] out GlyphImage? image)
        => _images.TryGetValue(key, out image);

    internal void Add(in GlyphImageCacheKey key, GlyphImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var size = image.Pixels.Length;
        if (size > MaxBytes || _images.ContainsKey(key))
        {
            return;
        }

        while (_images.Count >= MaxEntries || _bytes > MaxBytes - size)
        {
            if (_order.Count == 0)
            {
                break;
            }

            var evictedKey = _order.Dequeue();
            if (_images.Remove(evictedKey, out var evicted))
            {
                _bytes -= evicted.Pixels.Length;
            }
        }

        _images.Add(key, image);
        _order.Enqueue(key);
        _bytes += size;
    }

    internal void Clear()
    {
        _images.Clear();
        _order.Clear();
        _bytes = 0;
    }
}
