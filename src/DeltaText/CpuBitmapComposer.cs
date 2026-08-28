using Delta.Maths;
using Delta.Text.Contract;

namespace Delta.Text;

internal static class CpuBitmapComposer
{
    internal static CpuTextImage Compose(List<PlacedGlyph> placements, CpuTextRenderOptions options)
    {
        if (placements.Count == 0)
        {
            return new CpuTextImage(0, 0, default, Array.Empty<byte>());
        }

        var bounds = GetPixelBounds(placements);
        var pixels = new byte[checked(bounds.Width * bounds.Height * 4)];
        for (var i = 0; i < placements.Count; i++)
        {
            CpuGlyphBlender.Blend(pixels, bounds, placements[i], options);
        }

        return new CpuTextImage(
            bounds.Width,
            bounds.Height,
            new TextBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            pixels);
    }

    private static PixelBounds GetPixelBounds(List<PlacedGlyph> placements)
    {
        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;
        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            var plane = placement.Image.PlaneBounds;
            left = DeltaMaths.Min(left, placement.Origin.x + plane.Left);
            top = DeltaMaths.Min(top, placement.Origin.y + plane.Top);
            right = DeltaMaths.Max(right, placement.Origin.x + plane.Right);
            bottom = DeltaMaths.Max(bottom, placement.Origin.y + plane.Bottom);
        }

        var pixelLeft = checked((int)DeltaMaths.Floor(left));
        var pixelTop = checked((int)DeltaMaths.Floor(top));
        var pixelRight = checked((int)DeltaMaths.Ceil(right));
        var pixelBottom = checked((int)DeltaMaths.Ceil(bottom));
        if (pixelRight <= pixelLeft || pixelBottom <= pixelTop)
        {
            throw new InvalidDataException("Glyph images produced an invalid CPU text bounds rectangle.");
        }

        return new PixelBounds(pixelLeft, pixelTop, pixelRight, pixelBottom);
    }
}
