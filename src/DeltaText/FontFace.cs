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
    private readonly Dictionary<float, SixFont> _fontsByPixelsPerEm = new();
    private readonly Dictionary<int, float> _leftSideBearings = new();
    private readonly Dictionary<GlyphOutlineKey, GlyphOutline> _outlines = new();
    private readonly GlyphImageCache _glyphImageCache = new();
    private readonly SixLaborsGlyphRenderer _outlineRenderer = new();
    private readonly TextRenderer _outlineTextRenderer;
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
        _outlineTextRenderer = new TextRenderer(_outlineRenderer);
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
        var transferred = false;
        try
        {
            var family = AddFamily(collection, stream, ownedData, request.FaceIndex);
            var baseFont = family.CreateFont(1);
            var variations = ConvertVariations(request.Variations.Span);
            var result = new FontFace(ownedData, stream, collection, family, baseFont.FontMetrics, variations);
            transferred = true;
            return result;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException
            or InvalidDataException
            or FormatException
            or OverflowException)
        {
            throw new ArgumentException("Font data is not a supported font.", nameof(request), exception);
        }
        finally
        {
            if (!transferred)
            {
                stream.Dispose();
            }
        }
    }

    internal SixFont GetOrCreateFont(float pixelsPerEm)
    {
        ThrowIfDisposed();
        if (_fontsByPixelsPerEm.TryGetValue(pixelsPerEm, out var font))
        {
            return font;
        }

        font = _variations.Length == 0
            ? _family.CreateFont(pixelsPerEm)
            : _family.CreateFont(pixelsPerEm, _variations);
        _fontsByPixelsPerEm.Add(pixelsPerEm, font);
        return font;
    }

    internal bool TryGetLeftSideBearing(SixFont font, CodePoint codepoint, out float left)
    {
        ThrowIfDisposed();
        if (_leftSideBearings.TryGetValue(codepoint.Value, out left))
        {
            return true;
        }

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
        _leftSideBearings[codepoint.Value] = left;
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

    internal bool TryGetCachedGlyphImage(
        in GlyphImageCacheKey key,
        [NotNullWhen(true)] out GlyphImage? image)
    {
        ThrowIfDisposed();
        return _glyphImageCache.TryGet(key, out image);
    }

    internal void CacheGlyphImage(in GlyphImageCacheKey key, GlyphImage image)
    {
        ThrowIfDisposed();
        _glyphImageCache.Add(key, image);
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

        if (glyphId > ushort.MaxValue
            || !Metrics.TryGetGlyphMetrics(
                (ushort)glyphId,
                TextAttributes.None,
                TextDecorations.None,
                LayoutMode.HorizontalTopBottom,
                support,
                null,
                out _))
        {
            outline = null;
            return false;
        }

        var font = GetOrCreateFont(pixelsPerEm);
        _outlineRenderer.Reset();
        var options = new GlyphOptions
        {
            Font = font,
            Dpi = SixLaborsAdapterConstants.LayoutDpi,
            LayoutMode = LayoutMode.HorizontalTopBottom,
            ColorFontSupport = support,
            TextBaseline = TextBaseline.LineBox,
            GraphemeIndex = 0
        };
        _outlineTextRenderer.Render((ushort)glyphId, options);
        if (_outlineRenderer.Glyphs.Count == 0 || _outlineRenderer.Glyphs[0].GlyphId != glyphId
            || _outlineRenderer.Glyphs[0].Outline is not { } captured)
        {
            outline = null;
            return false;
        }

        var renderedMetrics = TextMeasurer.GetGlyphMetrics((ushort)glyphId, options);
        captured.Translate(
            -renderedMetrics.Advance.X,
            -renderedMetrics.Advance.Y - GetBaselineOffset(pixelsPerEm));
        outline = captured;
        CacheOutline(pixelsPerEm, glyphId, support, outline);
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _fontsByPixelsPerEm.Clear();
            _leftSideBearings.Clear();
            _outlines.Clear();
            _glyphImageCache.Clear();
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

internal readonly record struct GlyphImageCacheKey(
    uint GlyphId,
    float PixelsPerEm,
    GlyphImageMode Mode,
    float DistanceRange,
    ColorGlyphOptions? Color);
