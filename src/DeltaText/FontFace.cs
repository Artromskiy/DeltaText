using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Delta.Text.Contract;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.Fonts.Unicode;
using SixFont = SixLabors.Fonts.Font;
using SixFontMetrics = SixLabors.Fonts.FontMetrics;
using SixFontVariation = SixLabors.Fonts.FontVariation;
using ContractFontVariation = Delta.Text.Contract.FontVariation;

namespace Delta.Text;

/// <summary>Implementation-owned SixLabors font face and immutable source storage.</summary>
internal sealed class FontFace : IDisposable
{
    private readonly byte[] _fontData;
    private readonly MemoryStream _fontStream;
    private readonly FontCollection _collection;
    private readonly FontFamily _family;
    private readonly SixFontVariation[] _variations;
    private readonly Dictionary<GlyphOutlineKey, GlyphOutline> _outlines = new();
    private int _disposed;

    private FontFace(
        byte[] data,
        MemoryStream fontStream,
        FontCollection collection,
        FontFamily family,
        SixFontMetrics metrics,
        SixFontVariation[] variations)
    {
        _fontData = data;
        _fontStream = fontStream;
        _collection = collection;
        _family = family;
        Metrics = metrics;
        _variations = variations;
    }

    internal int UnitsPerEm => Metrics.UnitsPerEm;
    internal SixFontMetrics Metrics { get; }
    internal FontFamily Family => _family;

    internal float GetBaselineOffset(float pixelsPerEm)
    {
        ThrowIfDisposed();
        return Metrics.HorizontalMetrics.Ascender * pixelsPerEm / UnitsPerEm;
    }

    internal static FontFace FromRequest(in FontOpenRequest request)
    {
        var ownedData = request.Data.ToArray();
        var stream = new MemoryStream(ownedData, writable: false);
        var collection = new FontCollection();
        try
        {
            var family = AddFamily(collection, stream, ownedData, request.FaceIndex);
            var baseFont = family.CreateFont(1);
            var variations = ConvertVariations(request.Variations.Span);
            return new FontFace(ownedData, stream, collection, family, baseFont.FontMetrics, variations);
        }
        catch (Exception exception) when (
            exception is EndOfStreamException
            or InvalidDataException
            or FormatException
            or OverflowException)
        {
            stream.Dispose();
            throw new ArgumentException("Font data is not a supported font.", nameof(request), exception);
        }
    }

    internal SixFont CreateFont(float pixelsPerEm)
    {
        ThrowIfDisposed();
        return _variations.Length == 0
            ? _family.CreateFont(pixelsPerEm)
            : _family.CreateFont(pixelsPerEm, _variations);
    }

    internal bool TryGetLeftSideBearing(SixFont font, CodePoint codepoint, out float left)
    {
        ThrowIfDisposed();
        if (!font.TryGetGlyph(
                codepoint,
                TextAttributes.None,
                LayoutMode.HorizontalTopBottom,
                ColorFontSupport.None,
                out var glyph)
            || glyph is not { } value)
        {
            left = 0;
            return false;
        }

        left = value.GlyphMetrics.LeftSideBearing;
        return true;
    }

    internal bool TryGetCachedOutline(
        float pixelsPerEm,
        uint glyphId,
        ColorFontSupport support,
        [NotNullWhen(true)] out GlyphOutline? outline)
    {
        ThrowIfDisposed();
        return _outlines.TryGetValue(new GlyphOutlineKey(glyphId, pixelsPerEm, support), out outline);
    }

    internal void CacheOutline(float pixelsPerEm, uint glyphId, ColorFontSupport support, GlyphOutline outline)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(outline);
        _outlines[new GlyphOutlineKey(glyphId, pixelsPerEm, support)] = outline;
    }

    internal bool TryCreateOutline(
        float pixelsPerEm,
        uint glyphId,
        ColorFontSupport support,
        [NotNullWhen(true)] out GlyphOutline? outline)
    {
        ThrowIfDisposed();
        if (TryGetCachedOutline(pixelsPerEm, glyphId, support, out outline))
        {
            return true;
        }

        // INCOMPLETE / OBSOLETE-CANDIDATE: the fallback lookup scans every
        // available code point to recover a glyph by ID. Replace it with a
        // direct glyph-ID outline API or a bounded per-face glyph index before
        // using this path as a high-volume production cache warmer.
        var font = CreateFont(pixelsPerEm);
        var codepoints = Metrics.GetAvailableCodePoints().Span;
        for (var i = 0; i < codepoints.Length; i++)
        {
            if (!font.TryGetGlyph(codepoints[i], TextAttributes.None, LayoutMode.HorizontalTopBottom, ColorFontSupport.None, out var glyph)
                || glyph is not { } value
                || value.GlyphMetrics.GlyphId != glyphId)
            {
                continue;
            }

            var collector = new SixLaborsGlyphRenderer();
            var codepointText = codepoints[i].ToString();
            var options = new TextOptions(font)
            {
                Dpi = 72,
                TextDirection = SixLabors.Fonts.TextDirection.LeftToRight,
                ColorFontSupport = support
            };
            new TextRenderer(collector).Render(codepointText, options);
            if (collector.Glyphs.Count == 0 || collector.Glyphs[0].GlyphId != glyphId
                || collector.Glyphs[0].Outline is not { } captured)
            {
                continue;
            }

            var metrics = TextMeasurer.GetGlyphMetrics(codepointText, options);
            if (metrics.Length == 0)
            {
                continue;
            }

            captured.Translate(
                -metrics.Span[0].Advance.X,
                -metrics.Span[0].Advance.Y - GetBaselineOffset(pixelsPerEm));
            outline = captured;
            if (outline is not null)
            {
                CacheOutline(pixelsPerEm, glyphId, support, outline);
                return true;
            }
        }

        outline = null;
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _outlines.Clear();
            _fontStream.Dispose();
        }

        GC.KeepAlive(_fontData);
        GC.KeepAlive(_collection);
    }

    private static FontFamily AddFamily(FontCollection collection, MemoryStream stream, byte[] data, uint faceIndex)
    {
        if (!IsFontCollection(data))
        {
            if (faceIndex != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(faceIndex), "A single-face font only contains face index zero.");
            }

            return collection.Add(stream);
        }

        var families = collection.AddCollection(stream, CultureInfo.InvariantCulture);
        if (faceIndex >= (uint)families.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(faceIndex), "The requested font face is not present in the collection.");
        }

        return families.Span[checked((int)faceIndex)];
    }

    private static SixFontVariation[] ConvertVariations(ReadOnlySpan<ContractFontVariation> variations)
    {
        if (variations.Length == 0)
        {
            return Array.Empty<SixFontVariation>();
        }

        var result = new SixFontVariation[variations.Length];
        for (var i = 0; i < variations.Length; i++)
        {
            result[i] = new SixFontVariation(ToTag(variations[i].Axis), variations[i].Value);
        }

        return result;
    }

    private static string ToTag(OpenTypeTag tag)
    {
        var value = tag.Value;
        return new string(
        [
            (char)(value >> 24),
            (char)(value >> 16),
            (char)(value >> 8),
            (char)value
        ]);
    }

    private static bool IsFontCollection(ReadOnlySpan<byte> data)
        => data.Length >= 4 && data[0] == (byte)'t' && data[1] == (byte)'t'
            && data[2] == (byte)'c' && data[3] == (byte)'f';

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}

internal readonly record struct GlyphOutlineKey(uint GlyphId, float PixelsPerEm, ColorFontSupport Support);
