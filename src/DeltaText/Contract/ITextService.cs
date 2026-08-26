namespace Delta.Text.Contract;

/// <summary>
/// Provides renderer-neutral font shaping and unpacked glyph images.
/// </summary>
/// <remarks>
/// Implementations own opened font data until <see cref="CloseFont"/> is called.
/// Consumers own all atlas packing, UV assignment, caching, upload and rendering.
/// </remarks>
public interface ITextService : IDisposable
{
    /// <summary>Opens an exact font instance and returns its opaque identity.</summary>
    FontInstanceId OpenFont(in FontOpenRequest request);

    /// <summary>Releases an opened font instance.</summary>
    void CloseFont(FontInstanceId font);

    /// <summary>Returns font metrics scaled to the requested pixels-per-em size.</summary>
    FontMetrics GetFontMetrics(FontInstanceId font, float pixelsPerEm);

    /// <summary>Shapes text into ordered runs of positioned glyph data.</summary>
    ShapedText Shape(in TextShapeRequest request);

    /// <summary>Generates one tightly packed, unpacked glyph image.</summary>
    GlyphImage GenerateGlyphImage(in GlyphImageRequest request);
}
