using Delta.Text.Contract;

namespace Delta.Text;

internal static class CpuGlyphImageValidator
{
    internal static void Validate(
        GlyphImage image,
        FontInstanceId font,
        uint glyphId,
        GlyphImageMode mode)
    {
        ValidateIdentity(image, font, glyphId);
        ValidateBounds(image);

        if (image.IsEmpty)
        {
            ValidateEmpty(image);
            return;
        }

        ValidatePayload(image, mode);
    }

    private static void ValidateIdentity(GlyphImage image, FontInstanceId font, uint glyphId)
    {
        if (image.Font != font || image.GlyphId != glyphId)
        {
            throw new InvalidDataException("Text service returned a glyph image for a different glyph identity.");
        }
    }

    private static void ValidateBounds(GlyphImage image)
    {
        if (!float.IsFinite(image.PlaneBounds.Left)
            || !float.IsFinite(image.PlaneBounds.Top)
            || !float.IsFinite(image.PlaneBounds.Right)
            || !float.IsFinite(image.PlaneBounds.Bottom))
        {
            throw new InvalidDataException("Text service returned non-finite glyph image bounds.");
        }
    }

    private static void ValidateEmpty(GlyphImage image)
    {
        if (image.Width != 0 || image.Height != 0 || !image.Pixels.IsEmpty)
        {
            throw new InvalidDataException("Text service returned a malformed empty glyph image.");
        }
    }

    private static void ValidatePayload(GlyphImage image, GlyphImageMode mode)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new InvalidDataException("Text service returned a glyph image with invalid dimensions.");
        }

        var bytesPerPixel = CpuGlyphImageFormat.GetBytesPerPixel(image.Encoding);
        if (image.Encoding != CpuGlyphImageFormat.ExpectedEncoding(mode)
            || image.Pixels.Length != checked(image.Width * image.Height * bytesPerPixel))
        {
            throw new InvalidDataException("Text service returned a glyph image with an invalid encoding or payload length.");
        }
    }
}
