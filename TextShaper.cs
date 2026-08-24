namespace Delta.Text;

/// <summary>Caches shaped glyph runs by face and shaping request.</summary>
public sealed class TextShaper
{
    private readonly BoundedCache<ShapeCacheKey, ShapedGlyphRun> _cache;

    /// <summary>Creates a shaper with a bounded deterministic result cache.</summary>
    public TextShaper(TextCacheBudget? budget = null)
    {
        var selected = budget ?? TextCacheBudget.Default;
        _cache = new BoundedCache<ShapeCacheKey, ShapedGlyphRun>(selected);
    }

    /// <summary>Shapes a request, reusing an identical cached result.</summary>
    /// <param name="face">The loaded font face.</param>
    /// <param name="request">The shaping settings.</param>
    /// <returns>The cached or newly shaped glyph run.</returns>
    public ShapedGlyphRun Shape(FontFace face, TextShapingRequest request)
    {
        ArgumentNullException.ThrowIfNull(face);
        var key = new ShapeCacheKey(face.Key, request);
        return _cache.GetOrAdd(key, () => face.Shape(request), static run => EstimateSize(run));
    }

    /// <summary>Removes all cached shaped runs.</summary>
    public void Clear() => _cache.Clear();

    private static long EstimateSize(ShapedGlyphRun run)
        => 256L + run.Glyphs.Length * 32L + run.PositionedGlyphs.Length * 32L;

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
