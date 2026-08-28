using Delta.Text.Contract;

namespace Delta.Text;

internal static class CpuGlyphImageLoader
{
    internal static GlyphImage Load(
        ITextService textService,
        FontInstanceId font,
        uint glyphId,
        float pixelsPerEm,
        CpuTextRenderOptions options)
    {
        var image = textService.GenerateGlyphImage(new GlyphImageRequest(
            font,
            glyphId,
            pixelsPerEm,
            options.Mode,
            options.DistanceRange,
            options.Mode == GlyphImageMode.Color
                ? new ColorGlyphOptions(0, options.Foreground)
                : null));
        CpuGlyphImageValidator.Validate(image, font, glyphId, options.Mode);
        return image;
    }
}
