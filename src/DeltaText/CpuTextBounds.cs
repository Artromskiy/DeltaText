using global::Delta;

namespace Delta.Text;

internal static class CpuTextBounds
{
    internal static PixelBounds Calculate(List<PlacedGlyph> placements)
    {
        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;
        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            var plane = placement.Image.PlaneBounds;
            left = Maths.Min(left, placement.Origin.x + plane.Left);
            top = Maths.Min(top, placement.Origin.y + plane.Top);
            right = Maths.Max(right, placement.Origin.x + plane.Right);
            bottom = Maths.Max(bottom, placement.Origin.y + plane.Bottom);
        }

        var pixelLeft = checked((int)Maths.Floor(left));
        var pixelTop = checked((int)Maths.Floor(top));
        var pixelRight = checked((int)Maths.Ceil(right));
        var pixelBottom = checked((int)Maths.Ceil(bottom));
        if (pixelRight <= pixelLeft || pixelBottom <= pixelTop)
        {
            throw new InvalidDataException("Glyph images produced an invalid CPU text bounds rectangle.");
        }

        return new PixelBounds(pixelLeft, pixelTop, pixelRight, pixelBottom);
    }
}
