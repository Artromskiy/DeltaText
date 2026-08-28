using Delta.Maths;
using Delta.Text.Contract;

namespace Delta.Text;

internal static class CpuGlyphBlender
{
    internal static void Blend(
        byte[] destination,
        PixelBounds bounds,
        PlacedGlyph placement,
        CpuTextRenderOptions options)
    {
        var image = placement.Image;
        var targetX = checked((int)DeltaMaths.RoundEven(
            placement.Origin.x + image.PlaneBounds.Left - bounds.Left));
        var targetY = checked((int)DeltaMaths.RoundEven(
            placement.Origin.y + image.PlaneBounds.Top - bounds.Top));
        var bytesPerPixel = CpuGlyphImageFormat.GetBytesPerPixel(image.Encoding);
        var source = image.Pixels.Span;
        for (var y = 0; y < image.Height; y++)
        {
            var outputY = (long)targetY + y;
            if (outputY < 0 || outputY >= bounds.Height)
            {
                continue;
            }

            BlendRow(destination, bounds, image, source, targetX, (int)outputY, y, bytesPerPixel, options);
        }
    }

    private static void BlendRow(
        byte[] destination,
        PixelBounds bounds,
        GlyphImage image,
        ReadOnlySpan<byte> source,
        int targetX,
        int outputY,
        int sourceY,
        int bytesPerPixel,
        CpuTextRenderOptions options)
    {
        for (var x = 0; x < image.Width; x++)
        {
            var outputX = (long)targetX + x;
            if (outputX < 0 || outputX >= bounds.Width)
            {
                continue;
            }

            BlendPixel(
                destination,
                image,
                source,
                checked((sourceY * image.Width + x) * bytesPerPixel),
                checked((outputY * bounds.Width + (int)outputX) * 4),
                options);
        }
    }

    private static void BlendPixel(
        byte[] destination,
        GlyphImage image,
        ReadOnlySpan<byte> source,
        int sourceIndex,
        int destinationIndex,
        CpuTextRenderOptions options)
    {
        if (image.Encoding == GlyphImageEncoding.ColorRgba8PremultipliedSrgb)
        {
            CpuPixelBlender.BlendPremultiplied(
                destination,
                destinationIndex,
                source[sourceIndex],
                source[sourceIndex + 1],
                source[sourceIndex + 2],
                source[sourceIndex + 3]);
            return;
        }

        var alpha = CpuDistanceDecoder.Decode(
            image.Encoding,
            source,
            sourceIndex,
            image.DistanceRange);
        var alphaByte = checked((byte)DeltaMaths.Clamp(
            (int)DeltaMaths.Round(alpha * options.Foreground.Alpha),
            0,
            255));
        CpuPixelBlender.BlendMonochrome(
            destination,
            destinationIndex,
            options.Foreground,
            alphaByte);
    }
}
