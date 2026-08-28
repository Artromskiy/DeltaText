using Delta.Maths;

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
