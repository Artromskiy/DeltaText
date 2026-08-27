namespace Delta.Text.Contract;

/// <summary>Requested CPU representation of a glyph.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1028:Enum Storage should be Int32",
    Justification = "The byte underlying type is part of the compact glyph-image contract.")]
public enum GlyphImageMode : byte
{
    /// <summary>Invalid or unspecified representation.</summary>
    Unknown = 0,
    /// <summary>Single-channel coverage image.</summary>
    Coverage = 1,
    /// <summary>Single-channel signed-distance field.</summary>
    Sdf = 2,
    /// <summary>Three-channel multi-channel signed-distance field.</summary>
    Msdf = 3,
    /// <summary>Flattened OpenType color glyph.</summary>
    Color = 4,
}

/// <summary>Exact tightly packed pixel interpretation returned to consumers.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1028:Enum Storage should be Int32",
    Justification = "The byte underlying type is part of the compact glyph-image contract.")]
public enum GlyphImageEncoding : byte
{
    /// <summary>Invalid or unspecified encoding.</summary>
    Unknown = 0,
    /// <summary>One unsigned normalized coverage byte per pixel.</summary>
    CoverageR8 = 1,
    /// <summary>One unsigned normalized signed-distance byte per pixel.</summary>
    SdfR8 = 2,
    /// <summary>Three unsigned normalized MSDF bytes per pixel.</summary>
    MsdfRgb8 = 3,
    /// <summary>Four premultiplied sRGB color bytes per pixel.</summary>
    ColorRgba8PremultipliedSrgb = 4,
}

/// <summary>Eight-bit color used only to resolve color-font foreground paint.</summary>
public readonly record struct Rgba32(byte Red, byte Green, byte Blue, byte Alpha);

/// <summary>Palette selection used when flattening an OpenType color glyph.</summary>
public readonly record struct ColorGlyphOptions(ushort PaletteIndex, Rgba32 Foreground);

/// <summary>Request for one unpacked CPU glyph image.</summary>
/// <param name="Font">Exact font instance owning <paramref name="GlyphId"/>.</param>
/// <param name="GlyphId">Glyph identifier returned by shaping.</param>
/// <param name="PixelsPerEm">Requested device size.</param>
/// <param name="Mode">Requested image representation.</param>
/// <param name="DistanceRange">Distance range in pixels for SDF/MSDF; zero otherwise.</param>
/// <param name="Color">Color-font palette input; ignored for non-color modes.</param>
public readonly record struct GlyphImageRequest(
    FontInstanceId Font,
    uint GlyphId,
    float PixelsPerEm,
    GlyphImageMode Mode,
    float DistanceRange = 0,
    ColorGlyphOptions? Color = null);

/// <summary>Owned immutable, unpacked CPU image of one glyph.</summary>
/// <remarks>
/// Pixels are row-major from top to bottom and are always tightly packed. The
/// payload length is width times height times the encoding's bytes per pixel.
/// Atlas pages, UVs, padding between packed entries and GPU formats are owned
/// by the consumer.
/// </remarks>
public sealed class GlyphImage
{
    internal GlyphImage(
        FontInstanceId font,
        uint glyphId,
        float pixelsPerEm,
        GlyphImageEncoding encoding,
        float distanceRange,
        int width,
        int height,
        TextBounds planeBounds,
        ReadOnlyMemory<byte> pixels)
    {
        Font = font;
        GlyphId = glyphId;
        PixelsPerEm = pixelsPerEm;
        Encoding = encoding;
        DistanceRange = distanceRange;
        Width = width;
        Height = height;
        PlaneBounds = planeBounds;
        Pixels = pixels;
    }

    /// <summary>Exact font instance that owns the glyph ID.</summary>
    public FontInstanceId Font { get; }

    /// <summary>Font glyph identifier.</summary>
    public uint GlyphId { get; }

    /// <summary>Device size used to generate the image.</summary>
    public float PixelsPerEm { get; }

    /// <summary>Pixel interpretation.</summary>
    public GlyphImageEncoding Encoding { get; }

    /// <summary>Distance range in pixels, or zero for non-distance images.</summary>
    public float DistanceRange { get; }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }

    /// <summary>Placement of the complete image relative to the glyph origin.</summary>
    public TextBounds PlaneBounds { get; }

    /// <summary>Owned tightly packed pixel bytes.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    /// <summary>Returns whether this glyph has no pixels and needs no atlas entry.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;
}
