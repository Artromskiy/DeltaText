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
        var sourceLeft = (int)Math.Max(0L, -(long)targetX);
        var sourceTop = (int)Math.Max(0L, -(long)targetY);
        var sourceRight = (int)Math.Min(image.Width, (long)bounds.Width - targetX);
        var sourceBottom = (int)Math.Min(image.Height, (long)bounds.Height - targetY);
        if (sourceLeft >= sourceRight || sourceTop >= sourceBottom)
        {
            return;
        }

        var destinationX = checked(targetX + sourceLeft);
        for (var sourceY = sourceTop; sourceY < sourceBottom; sourceY++)
        {
            var outputY = checked(targetY + sourceY);
            BlendRow(
                destination,
                bounds,
                image,
                source,
                destinationX,
                outputY,
                sourceY,
                sourceLeft,
                sourceRight - sourceLeft,
                bytesPerPixel,
                options);
        }
    }

    private static void BlendRow(
        byte[] destination,
        PixelBounds bounds,
        GlyphImage image,
        ReadOnlySpan<byte> source,
        int destinationX,
        int outputY,
        int sourceY,
        int sourceX,
        int pixelCount,
        int bytesPerPixel,
        CpuTextRenderOptions options)
    {
        var sourceIndex = checked((sourceY * image.Width + sourceX) * bytesPerPixel);
        var destinationIndex = checked((outputY * bounds.Width + destinationX) * 4);
        if (image.Encoding == GlyphImageEncoding.ColorRgba8PremultipliedSrgb)
        {
            BlendColorRow(destination, source, sourceIndex, destinationIndex, pixelCount);
            return;
        }

        BlendDistanceRow(
            destination,
            image,
            source,
            sourceIndex,
            destinationIndex,
            pixelCount,
            bytesPerPixel,
            options);
    }

    private static void BlendColorRow(
        byte[] destination,
        ReadOnlySpan<byte> source,
        int sourceIndex,
        int destinationIndex,
        int pixelCount)
    {
        for (var i = 0; i < pixelCount; i++)
        {
            CpuPixelBlender.BlendPremultiplied(
                destination,
                destinationIndex,
                source[sourceIndex],
                source[sourceIndex + 1],
                source[sourceIndex + 2],
                source[sourceIndex + 3]);
            sourceIndex += 4;
            destinationIndex += 4;
        }
    }

    private static void BlendDistanceRow(
        byte[] destination,
        GlyphImage image,
        ReadOnlySpan<byte> source,
        int sourceIndex,
        int destinationIndex,
        int pixelCount,
        int bytesPerPixel,
        CpuTextRenderOptions options)
    {
        for (var i = 0; i < pixelCount; i++)
        {
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
            sourceIndex += bytesPerPixel;
            destinationIndex += 4;
        }
    }
}
