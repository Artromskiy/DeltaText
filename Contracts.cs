using System.Globalization;

namespace Delta.Text;

/// <summary>Stable identity for a font face.</summary>
public readonly record struct FontKey
{
    /// <summary>Creates a font identity.</summary>
    /// <param name="family">The family name.</param>
    /// <param name="style">The style name.</param>
    /// <param name="sourceId">A stable identity for the source bytes.</param>
    public FontKey(string family, string style, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            throw new ArgumentException("A font family is required.", nameof(family));
        }

        if (string.IsNullOrWhiteSpace(style))
        {
            throw new ArgumentException("A font style is required.", nameof(style));
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("A stable font source identity is required.", nameof(sourceId));
        }

        Family = family;
        Style = style;
        SourceId = sourceId;
    }

    /// <summary>The font family.</summary>
    public string Family { get; }
    /// <summary>The font style.</summary>
    public string Style { get; }
    /// <summary>The stable source identity.</summary>
    public string SourceId { get; }
}

/// <summary>Vertical metrics for a font face in font units.</summary>
/// <param name="UnitsPerEm">The font design grid size.</param>
/// <param name="Ascender">The distance above the baseline.</param>
/// <param name="Descender">The distance below the baseline.</param>
/// <param name="LineGap">The recommended additional line spacing.</param>
public readonly record struct FontMetrics(int UnitsPerEm, int Ascender, int Descender, int LineGap);

/// <summary>Metrics for one glyph in font units.</summary>
/// <param name="GlyphId">The glyph identifier.</param>
/// <param name="AdvanceX">The horizontal advance.</param>
/// <param name="AdvanceY">The vertical advance.</param>
/// <param name="BearingX">The left side bearing.</param>
/// <param name="BearingY">The top bearing.</param>
/// <param name="Width">The glyph width.</param>
/// <param name="Height">The glyph height.</param>
/// <param name="UnitsPerEm">The font design grid size.</param>
public readonly record struct GlyphMetrics(
    uint GlyphId,
    int AdvanceX,
    int AdvanceY,
    int BearingX,
    int BearingY,
    int Width,
    int Height,
    int UnitsPerEm);

/// <summary>Text flow direction passed to the shaping engine.</summary>
public enum TextDirection
{
    /// <summary>Let the shaping engine infer the direction.</summary>
    Auto,
    /// <summary>Left-to-right text.</summary>
    LeftToRight,
    /// <summary>Right-to-left text.</summary>
    RightToLeft,
    /// <summary>Top-to-bottom text.</summary>
    TopToBottom,
    /// <summary>Bottom-to-top text.</summary>
    BottomToTop
}

/// <summary>An OpenType shaping feature toggle.</summary>
public readonly record struct TextFeature
{
    /// <summary>Creates a shaping feature toggle.</summary>
    /// <param name="tag">The four-character OpenType tag.</param>
    /// <param name="enabled">Whether the feature is enabled.</param>
    public TextFeature(string tag, bool enabled = true)
    {
        if (tag is null || tag.Length != 4)
        {
            throw new ArgumentException("OpenType feature tags must contain four characters.", nameof(tag));
        }

        Tag = tag;
        Enabled = enabled;
    }

    /// <summary>The four-character OpenType tag.</summary>
    public string Tag { get; }
    /// <summary>Whether the feature is enabled.</summary>
    public bool Enabled { get; }
}

/// <summary>Input settings for one shaping operation.</summary>
public readonly record struct TextShapingRequest
{
    /// <summary>Creates shaping settings.</summary>
    /// <param name="text">The text to shape.</param>
    /// <param name="size">The requested em size in device units.</param>
    /// <param name="culture">The language and script culture, or invariant culture.</param>
    /// <param name="direction">The requested text direction.</param>
    /// <param name="features">The OpenType feature toggles.</param>
    public TextShapingRequest(
        string text,
        float size,
        CultureInfo? culture = null,
        TextDirection direction = TextDirection.Auto,
        ReadOnlyMemory<TextFeature> features = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!(size > 0) || float.IsNaN(size) || float.IsInfinity(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        Text = text;
        Size = size;
        Culture = culture ?? CultureInfo.InvariantCulture;
        Direction = direction;
        Features = features;
    }

    /// <summary>The text to shape.</summary>
    public string Text { get; }
    /// <summary>The requested em size.</summary>
    public float Size { get; }
    /// <summary>The language and script culture.</summary>
    public CultureInfo Culture { get; }
    /// <summary>The requested text direction.</summary>
    public TextDirection Direction { get; }
    /// <summary>The feature toggles.</summary>
    public ReadOnlyMemory<TextFeature> Features { get; }
}

/// <summary>A glyph and its shaping offsets and advances.</summary>
/// <param name="GlyphId">The glyph identifier.</param>
/// <param name="Cluster">The source-text cluster index.</param>
/// <param name="AdvanceX">The horizontal advance.</param>
/// <param name="AdvanceY">The vertical advance.</param>
/// <param name="OffsetX">The horizontal positioning offset.</param>
/// <param name="OffsetY">The vertical positioning offset.</param>
public readonly record struct ShapedGlyph(
    uint GlyphId,
    int Cluster,
    float AdvanceX,
    float AdvanceY,
    float OffsetX,
    float OffsetY);

/// <summary>A shaped glyph positioned relative to the run origin.</summary>
/// <param name="GlyphId">The glyph identifier.</param>
/// <param name="Cluster">The source-text cluster index.</param>
/// <param name="X">The glyph pen position on the x axis.</param>
/// <param name="Y">The glyph pen position on the y axis.</param>
/// <param name="AdvanceX">The horizontal advance.</param>
/// <param name="AdvanceY">The vertical advance.</param>
/// <param name="OffsetX">The horizontal positioning offset.</param>
/// <param name="OffsetY">The vertical positioning offset.</param>
public readonly record struct PositionedGlyph(
    uint GlyphId,
    int Cluster,
    float X,
    float Y,
    float AdvanceX,
    float AdvanceY,
    float OffsetX,
    float OffsetY);

/// <summary>Bounds of a shaped run in device units.</summary>
/// <param name="Left">The left edge.</param>
/// <param name="Bottom">The bottom edge.</param>
/// <param name="Right">The right edge.</param>
/// <param name="Top">The top edge.</param>
public readonly record struct TextBounds(float Left, float Bottom, float Right, float Top)
{
    /// <summary>The width of the bounds.</summary>
    public float Width => Right - Left;
    /// <summary>The height of the bounds.</summary>
    public float Height => Top - Bottom;
}

/// <summary>Immutable shaped glyph data and positioned glyph data.</summary>
public sealed class ShapedGlyphRun
{
    private readonly ShapedGlyph[] _glyphs;
    private readonly PositionedGlyph[] _positionedGlyphs;

    internal ShapedGlyphRun(
        FontKey font,
        float size,
        int textLength,
        ShapedGlyph[] glyphs,
        PositionedGlyph[] positionedGlyphs,
        float advanceX,
        float advanceY,
        TextBounds bounds)
    {
        Font = font;
        Size = size;
        TextLength = textLength;
        _glyphs = glyphs;
        _positionedGlyphs = positionedGlyphs;
        AdvanceX = advanceX;
        AdvanceY = advanceY;
        Bounds = bounds;
    }

    /// <summary>The font used for shaping.</summary>
    public FontKey Font { get; }
    /// <summary>The requested em size.</summary>
    public float Size { get; }
    /// <summary>The source text length in UTF-16 code units.</summary>
    public int TextLength { get; }
    /// <summary>The shaped glyph sequence.</summary>
    public ReadOnlyMemory<ShapedGlyph> Glyphs => _glyphs;
    /// <summary>The positioned glyph sequence.</summary>
    public ReadOnlyMemory<PositionedGlyph> PositionedGlyphs => _positionedGlyphs;
    /// <summary>The total horizontal advance.</summary>
    public float AdvanceX { get; }
    /// <summary>The total vertical advance.</summary>
    public float AdvanceY { get; }
    /// <summary>The run bounds.</summary>
    public TextBounds Bounds { get; }
}

/// <summary>Settings for generating a glyph atlas.</summary>
public readonly record struct GlyphAtlasRequest
{
    /// <summary>Creates atlas generation settings.</summary>
    /// <param name="font">The font identity.</param>
    /// <param name="glyphIds">The glyph identifiers to include.</param>
    /// <param name="pixelSize">The rasterization size.</param>
    /// <param name="padding">The padding around each glyph.</param>
    /// <param name="distanceRange">The signed-distance range in pixels.</param>
    /// <param name="mode">The atlas pixel mode.</param>
    public GlyphAtlasRequest(FontKey font, ReadOnlyMemory<uint> glyphIds, int pixelSize, int padding, float distanceRange, GlyphAtlasMode mode)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);
        ArgumentOutOfRangeException.ThrowIfNegative(padding);
        if (!(distanceRange > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(distanceRange));
        }

        Font = font;
        GlyphIds = glyphIds;
        PixelSize = pixelSize;
        Padding = padding;
        DistanceRange = distanceRange;
        Mode = mode;
    }

    /// <summary>The font identity.</summary>
    public FontKey Font { get; }
    /// <summary>The requested glyph identifiers.</summary>
    public ReadOnlyMemory<uint> GlyphIds { get; }
    /// <summary>The rasterization size in pixels.</summary>
    public int PixelSize { get; }
    /// <summary>The padding in pixels.</summary>
    public int Padding { get; }
    /// <summary>The signed-distance range in pixels.</summary>
    public float DistanceRange { get; }
    /// <summary>The pixel encoding mode.</summary>
    public GlyphAtlasMode Mode { get; }
}

/// <summary>Pixel encoding used by a glyph atlas.</summary>
public enum GlyphAtlasMode
{
    /// <summary>Single-channel grayscale coverage or distance pixels.</summary>
    Grayscale,
    /// <summary>Three-channel multi-channel signed-distance pixels.</summary>
    Msdf,
    /// <summary>Four-channel multi-channel signed-distance pixels.</summary>
    Mtsdf
}

/// <summary>Limits retained shaping or glyph bitmap results.</summary>
public readonly record struct TextCacheBudget
{
    /// <summary>Creates a cache budget.</summary>
    public TextCacheBudget(int maxEntries, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        MaxEntries = maxEntries;
        MaxBytes = maxBytes;
    }

    /// <summary>Maximum number of retained results.</summary>
    public int MaxEntries { get; }
    /// <summary>Maximum estimated retained bytes.</summary>
    public long MaxBytes { get; }
    /// <summary>Default bounded budget for interactive text.</summary>
    public static TextCacheBudget Default => new(256, 16 * 1024 * 1024);
}

/// <summary>One un-packed CPU glyph bitmap. DeltaRender owns packing and UVs.</summary>
public sealed class GlyphBitmap
{
    internal GlyphBitmap(GlyphAtlasRequest request, uint glyphId, int width, int height, int stride,
        float bearingX, float bearingY, float advanceX, ReadOnlyMemory<byte> pixels)
    {
        Request = request;
        GlyphId = glyphId;
        Width = width;
        Height = height;
        Stride = stride;
        BearingX = bearingX;
        BearingY = bearingY;
        AdvanceX = advanceX;
        Pixels = pixels;
    }

    /// <summary>Creates an immutable, validated CPU glyph bitmap.</summary>
    /// <param name="request">The bitmap format and generation settings.</param>
    /// <param name="glyphId">The font glyph identifier; zero is a valid missing-glyph identifier.</param>
    /// <param name="width">The bitmap width in pixels.</param>
    /// <param name="height">The bitmap height in pixels.</param>
    /// <param name="stride">The row stride in bytes.</param>
    /// <param name="bearingX">The horizontal bearing in device units.</param>
    /// <param name="bearingY">The vertical bearing in device units.</param>
    /// <param name="advanceX">The horizontal advance in device units.</param>
    /// <param name="pixels">Exactly <c>height * stride</c> bytes in row-major order.</param>
    /// <returns>An immutable bitmap whose pixels are owned by the returned object.</returns>
    public static GlyphBitmap Create(GlyphAtlasRequest request, uint glyphId, int width, int height, int stride,
        float bearingX, float bearingY, float advanceX, ReadOnlyMemory<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);
        if (!float.IsFinite(bearingX) || !float.IsFinite(bearingY) || !float.IsFinite(advanceX))
        {
            throw new ArgumentException("Glyph metrics must be finite.", nameof(bearingX));
        }

        var channels = request.Mode switch
        {
            GlyphAtlasMode.Grayscale => 1,
            GlyphAtlasMode.Msdf => 3,
            GlyphAtlasMode.Mtsdf => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        var minimumStride = checked(width * channels);
        if (stride < minimumStride)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), $"Stride must be at least {minimumStride} bytes.");
        }

        var expectedLength = checked(height * stride);
        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException($"Pixel memory must contain exactly {expectedLength} bytes.", nameof(pixels));
        }

        return new GlyphBitmap(request, glyphId, width, height, stride, bearingX, bearingY, advanceX, pixels.ToArray());
    }

    /// <summary>The source atlas settings, without page ownership.</summary>
    public GlyphAtlasRequest Request { get; }
    /// <summary>The glyph identifier.</summary>
    public uint GlyphId { get; }
    /// <summary>Bitmap width in pixels.</summary>
    public int Width { get; }
    /// <summary>Bitmap height in pixels.</summary>
    public int Height { get; }
    /// <summary>Row stride in bytes.</summary>
    public int Stride { get; }
    /// <summary>Horizontal bearing in device units.</summary>
    public float BearingX { get; }
    /// <summary>Vertical bearing in device units.</summary>
    public float BearingY { get; }
    /// <summary>Horizontal advance in device units.</summary>
    public float AdvanceX { get; }
    /// <summary>Un-packed CPU pixels. The format is determined by Request.Mode.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }
}

/// <summary>Result of requesting one CPU glyph bitmap.</summary>
public readonly record struct GlyphBitmapResult(GlyphBitmapStatus Status, GlyphBitmap? Bitmap, string? Message)
{
    /// <summary>Whether a bitmap was produced.</summary>
    public bool Succeeded => Status == GlyphBitmapStatus.Succeeded && Bitmap is not null;
}

/// <summary>Outcome of a glyph bitmap request.</summary>
public enum GlyphBitmapStatus
{
    /// <summary>A bitmap was produced.</summary>
    Succeeded,
    /// <summary>The requested mode is intentionally not implemented.</summary>
    UnsupportedMode
}

/// <summary>Renderer-neutral positioned glyph handoff. It contains no pages or UVs.</summary>
public readonly record struct PositionedGlyphBitmap(PositionedGlyph Glyph, GlyphBitmap Bitmap);

/// <summary>Shaping plus CPU glyph data passed to a renderer-owned atlas stage.</summary>
public sealed class GlyphRenderData
{
    private readonly PositionedGlyphBitmap[] _glyphs;

    /// <summary>Creates a renderer-neutral handoff.</summary>
    public GlyphRenderData(ShapedGlyphRun run, ReadOnlyMemory<PositionedGlyphBitmap> glyphs)
    {
        ArgumentNullException.ThrowIfNull(run);
        _glyphs = glyphs.ToArray();
        Run = run;
    }

    /// <summary>The shaped run.</summary>
    public ShapedGlyphRun Run { get; }
    /// <summary>Positioned glyphs and their CPU bitmaps.</summary>
    public ReadOnlyMemory<PositionedGlyphBitmap> Glyphs => _glyphs;
}

/// <summary>Placement and pixels for one glyph in an atlas.</summary>
/// <param name="GlyphId">The glyph identifier.</param>
/// <param name="PageIndex">The containing page index.</param>
/// <param name="U0">The left UV coordinate.</param>
/// <param name="V0">The top UV coordinate.</param>
/// <param name="U1">The right UV coordinate.</param>
/// <param name="V1">The bottom UV coordinate.</param>
/// <param name="Width">The glyph bitmap width.</param>
/// <param name="Height">The glyph bitmap height.</param>
/// <param name="Stride">The row stride in bytes.</param>
/// <param name="BearingX">The horizontal bearing.</param>
/// <param name="BearingY">The vertical bearing.</param>
/// <param name="AdvanceX">The horizontal advance.</param>
/// <param name="Pixels">The glyph pixels.</param>
public readonly record struct GlyphAtlasGlyph(
    uint GlyphId,
    int PageIndex,
    float U0,
    float V0,
    float U1,
    float V1,
    int Width,
    int Height,
    int Stride,
    float BearingX,
    float BearingY,
    float AdvanceX,
    ReadOnlyMemory<byte> Pixels);

/// <summary>A packed atlas page and its pixels.</summary>
/// <param name="PageIndex">The page index.</param>
/// <param name="Width">The page width.</param>
/// <param name="Height">The page height.</param>
/// <param name="Pixels">The page pixels.</param>
public readonly record struct GlyphAtlasPage(
    int PageIndex,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Pixels);

/// <summary>The result of one atlas generation request.</summary>
/// <param name="Request">The request that produced the result.</param>
/// <param name="Pages">The packed pages.</param>
/// <param name="Glyphs">The glyph placements.</param>
public readonly record struct GlyphAtlasResult(
    GlyphAtlasRequest Request,
    ReadOnlyMemory<GlyphAtlasPage> Pages,
    ReadOnlyMemory<GlyphAtlasGlyph> Glyphs);

/// <summary>Generates deterministic glyph atlas data.</summary>
public interface IGlyphAtlasGenerator
{
    /// <summary>Generates or retrieves an atlas for a request.</summary>
    /// <param name="face">The loaded font face.</param>
    /// <param name="request">The atlas settings.</param>
    /// <returns>The generated atlas result.</returns>
    GlyphAtlasResult Generate(FontFace face, in GlyphAtlasRequest request);
}

/// <summary>Generates individual CPU glyph bitmaps; packing remains a renderer concern.</summary>
public interface IGlyphBitmapGenerator
{
    /// <summary>Attempts to generate one bitmap without allocating an atlas page.</summary>
    GlyphBitmapResult TryGenerateGlyph(FontFace face, in GlyphAtlasRequest request, uint glyphId);
}
