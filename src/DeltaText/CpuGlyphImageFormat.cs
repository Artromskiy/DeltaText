using Delta.Text.Contract;

namespace Delta.Text;

internal static class CpuGlyphImageFormat
{
    internal static GlyphImageEncoding ExpectedEncoding(GlyphImageMode mode)
        => mode switch
        {
            GlyphImageMode.Coverage => GlyphImageEncoding.CoverageR8,
            GlyphImageMode.Sdf => GlyphImageEncoding.SdfR8,
            GlyphImageMode.Msdf => GlyphImageEncoding.MsdfRgb8,
            GlyphImageMode.Color => GlyphImageEncoding.ColorRgba8PremultipliedSrgb,
            _ => GlyphImageEncoding.Unknown
        };

    internal static int GetBytesPerPixel(GlyphImageEncoding encoding)
        => encoding switch
        {
            GlyphImageEncoding.CoverageR8 or GlyphImageEncoding.SdfR8 => 1,
            GlyphImageEncoding.MsdfRgb8 => 3,
            GlyphImageEncoding.ColorRgba8PremultipliedSrgb => 4,
            _ => throw new InvalidDataException("Glyph image encoding is unknown to the CPU renderer.")
        };
}
