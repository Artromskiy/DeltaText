using System.Globalization;

namespace Delta.Text;

public readonly record struct FontKey
{
    public FontKey(string family, string style, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(family)) throw new ArgumentException("A font family is required.", nameof(family));
        if (string.IsNullOrWhiteSpace(style)) throw new ArgumentException("A font style is required.", nameof(style));
        if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("A stable font source identity is required.", nameof(sourceId));
        Family = family;
        Style = style;
        SourceId = sourceId;
    }

    public string Family { get; }
    public string Style { get; }
    public string SourceId { get; }
}

public readonly record struct FontMetrics(int UnitsPerEm, int Ascender, int Descender, int LineGap);

public readonly record struct GlyphMetrics(
    uint GlyphId,
    int AdvanceX,
    int AdvanceY,
    int BearingX,
    int BearingY,
    int Width,
    int Height,
    int UnitsPerEm);

public enum TextDirection
{
    Auto,
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop
}

public readonly record struct TextFeature
{
    public TextFeature(string tag, bool enabled = true)
    {
        if (tag is null || tag.Length != 4) throw new ArgumentException("OpenType feature tags must contain four characters.", nameof(tag));
        Tag = tag;
        Enabled = enabled;
    }

    public string Tag { get; }
    public bool Enabled { get; }
}

public readonly record struct TextShapingRequest
{
    public TextShapingRequest(
        string text,
        float size,
        CultureInfo? culture = null,
        TextDirection direction = TextDirection.Auto,
        ReadOnlyMemory<TextFeature> features = default)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (!(size > 0) || float.IsNaN(size) || float.IsInfinity(size)) throw new ArgumentOutOfRangeException(nameof(size));
        Text = text;
        Size = size;
        Culture = culture ?? CultureInfo.InvariantCulture;
        Direction = direction;
        Features = features;
    }

    public string Text { get; }
    public float Size { get; }
    public CultureInfo Culture { get; }
    public TextDirection Direction { get; }
    public ReadOnlyMemory<TextFeature> Features { get; }
}

public readonly record struct ShapedGlyph(
    uint GlyphId,
    int Cluster,
    float AdvanceX,
    float AdvanceY,
    float OffsetX,
    float OffsetY);

public readonly record struct PositionedGlyph(
    uint GlyphId,
    int Cluster,
    float X,
    float Y,
    float AdvanceX,
    float AdvanceY,
    float OffsetX,
    float OffsetY);

public readonly record struct TextBounds(float Left, float Bottom, float Right, float Top)
{
    public float Width => Right - Left;
    public float Height => Top - Bottom;
}

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

    public FontKey Font { get; }
    public float Size { get; }
    public int TextLength { get; }
    public ReadOnlyMemory<ShapedGlyph> Glyphs => _glyphs;
    public ReadOnlyMemory<PositionedGlyph> PositionedGlyphs => _positionedGlyphs;
    public float AdvanceX { get; }
    public float AdvanceY { get; }
    public TextBounds Bounds { get; }
}

public readonly record struct GlyphAtlasRequest
{
    public GlyphAtlasRequest(FontKey font, ReadOnlyMemory<uint> glyphIds, int pixelSize, int padding, float distanceRange, GlyphAtlasMode mode)
    {
        if (pixelSize <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSize));
        if (padding < 0) throw new ArgumentOutOfRangeException(nameof(padding));
        if (!(distanceRange > 0)) throw new ArgumentOutOfRangeException(nameof(distanceRange));
        Font = font;
        GlyphIds = glyphIds;
        PixelSize = pixelSize;
        Padding = padding;
        DistanceRange = distanceRange;
        Mode = mode;
    }

    public FontKey Font { get; }
    public ReadOnlyMemory<uint> GlyphIds { get; }
    public int PixelSize { get; }
    public int Padding { get; }
    public float DistanceRange { get; }
    public GlyphAtlasMode Mode { get; }
}

public enum GlyphAtlasMode
{
    Grayscale,
    Msdf,
    Mtsdf
}

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

public readonly record struct GlyphAtlasPage(
    int PageIndex,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Pixels);

public readonly record struct GlyphAtlasResult(
    GlyphAtlasRequest Request,
    ReadOnlyMemory<GlyphAtlasPage> Pages,
    ReadOnlyMemory<GlyphAtlasGlyph> Glyphs);

public interface IGlyphAtlasGenerator
{
    GlyphAtlasResult Generate(FontFace face, in GlyphAtlasRequest request);
}
