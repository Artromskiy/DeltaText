using System.Diagnostics.CodeAnalysis;

namespace Delta.Text;

internal static class ManagedMsdf
{
    internal static bool TryGenerate(
        GlyphContours contours,
        int pixelSize,
        int unitsPerEm,
        int padding,
        float distanceRange,
        uint edgeSeed,
        out int width,
        out int height,
        [NotNullWhen(true)] out byte[]? pixels)
    {
        width = 0;
        height = 0;
        pixels = null;
        if (!HasValidInput(contours, pixelSize, unitsPerEm, padding, distanceRange))
        {
            return false;
        }

        if (!MsdfGeometry.TryCreate(contours, pixelSize, unitsPerEm, padding, edgeSeed, distanceRange, out var geometry)
            || geometry is null)
        {
            return false;
        }

        width = geometry.Width;
        height = geometry.Height;
        pixels = MsdfRasterizer.Render(geometry, distanceRange);
        return true;
    }

    private static bool HasValidInput(
        GlyphContours contours,
        int pixelSize,
        int unitsPerEm,
        int padding,
        float distanceRange)
    {
        return contours is not null
            && contours.Contours.Count > 0
            && pixelSize > 0
            && unitsPerEm > 0
            && padding >= 0
            && float.IsFinite(distanceRange)
            && distanceRange > 0;
    }
}
