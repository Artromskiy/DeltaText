using System.Collections.Concurrent;

namespace Delta.Text;

public sealed class TextShaper
{
    private readonly ConcurrentDictionary<ShapeCacheKey, ShapedGlyphRun> _cache = new();

    public ShapedGlyphRun Shape(FontFace face, TextShapingRequest request)
    {
        ArgumentNullException.ThrowIfNull(face);
        var key = new ShapeCacheKey(face.Key, request);
        return _cache.GetOrAdd(key, static (cacheKey, state) => state.face.Shape(state.request), (face, request));
    }

    public void Clear() => _cache.Clear();

    private readonly record struct ShapeCacheKey(
        FontKey Font,
        string Text,
        int SizeBits,
        string Culture,
        TextDirection Direction,
        string Features)
    {
        public ShapeCacheKey(FontKey font, TextShapingRequest request)
            : this(font, request.Text, BitConverter.SingleToInt32Bits(request.Size), request.Culture.Name, request.Direction, MakeFeatureKey(request.Features.Span))
        {
        }

        private static string MakeFeatureKey(ReadOnlySpan<TextFeature> features)
        {
            if (features.IsEmpty)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(features.Length * 6);
            foreach (var feature in features)
            {
                builder.Append(feature.Tag).Append(feature.Enabled ? '1' : '0').Append(';');
            }

            return builder.ToString();
        }
    }
}
