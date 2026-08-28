using Delta.Maths;

namespace Delta.Text;

internal static class MsdfRasterizer
{
    internal static byte[] Render(MsdfGeometry geometry, float distanceRange)
    {
        // INCOMPLETE / OBSOLETE-CANDIDATE: the current 3x3 grid neighborhood
        // and winding test are a deterministic managed baseline. Replace or
        // augment them with measured broad-phase and corner-quality validation
        // before treating this as a final high-volume rasterizer.
        var pixels = new byte[checked(geometry.Width * geometry.Height * 3)];
        var rangeSquared = distanceRange * distanceRange;
        var edges = geometry.Edges;
        var grid = geometry.Grid;
        for (var y = 0; y < geometry.Height; y++)
        {
            for (var x = 0; x < geometry.Width; x++)
            {
                var sample = new float2(x + 0.5f, y + 0.5f);
                var red = rangeSquared;
                var green = rangeSquared;
                var blue = rangeSquared;
                var nearest = rangeSquared;
                var cellX = Math.Clamp(x / grid.CellSize, 0, grid.Columns - 1);
                var cellY = Math.Clamp(y / grid.CellSize, 0, grid.Rows - 1);
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    var neighborY = cellY + offsetY;
                    if ((uint)neighborY >= (uint)grid.Rows)
                    {
                        continue;
                    }

                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        var neighborX = cellX + offsetX;
                        if ((uint)neighborX >= (uint)grid.Columns)
                        {
                            continue;
                        }

                        var cell = neighborY * grid.Columns + neighborX;
                        for (var i = grid.Offsets[cell]; i < grid.Offsets[cell + 1]; i++)
                        {
                            var edge = edges[grid.EdgeIndices[i]];
                            var distanceSquared = DistanceSquared(sample, edge.Start, edge.End);
                            nearest = DeltaMaths.Min(nearest, distanceSquared);
                            switch (edge.Channel)
                            {
                                case 0:
                                    red = DeltaMaths.Min(red, distanceSquared);
                                    break;
                                case 1:
                                    green = DeltaMaths.Min(green, distanceSquared);
                                    break;
                                default:
                                    blue = DeltaMaths.Min(blue, distanceSquared);
                                    break;
                            }
                        }
                    }
                }

                var sign = IsInside(edges, sample) ? 1f : -1f;
                if (red == rangeSquared)
                {
                    red = nearest;
                }

                if (green == rangeSquared)
                {
                    green = nearest;
                }

                if (blue == rangeSquared)
                {
                    blue = nearest;
                }

                var pixel = checked((y * geometry.Width + x) * 3);
                pixels[pixel] = MsdfEncoder.Encode(sign * DeltaMaths.Sqrt(red), distanceRange);
                pixels[pixel + 1] = MsdfEncoder.Encode(sign * DeltaMaths.Sqrt(green), distanceRange);
                pixels[pixel + 2] = MsdfEncoder.Encode(sign * DeltaMaths.Sqrt(blue), distanceRange);
            }
        }

        return pixels;
    }

    internal static float DistanceSquared(float2 point, float2 start, float2 end)
    {
        var edge = end - start;
        var lengthSquared = float2.SqrLength(edge);
        if (lengthSquared <= 1e-8f)
        {
            return float2.SqrLength(point - start);
        }

        var projection = float2.Dot(point - start, edge) / lengthSquared;
        projection = Math.Clamp(projection, 0, 1);
        return float2.SqrLength(point - (start + edge * projection));
    }

    internal static bool IsInside(MsdfEdge[] edges, float2 point)
    {
        var winding = 0;
        for (var i = 0; i < edges.Length; i++)
        {
            var edge = edges[i];
            var start = edge.Start;
            var end = edge.End;
            if ((start.y <= point.y && end.y > point.y) || (start.y > point.y && end.y <= point.y))
            {
                var intersectionX = start.x + (point.y - start.y) * (end.x - start.x) / (end.y - start.y);
                if (intersectionX > point.x)
                {
                    winding += end.y > start.y ? 1 : -1;
                }
            }
        }

        return winding != 0;
    }
}

internal static class MsdfEncoder
{
    internal static byte Encode(float signedDistance, float distanceRange)
    {
        var normalized = Math.Clamp(0.5f + signedDistance / (2f * distanceRange), 0, 1);
        return (byte)Math.Clamp((int)MathF.Round(normalized * 255f), 0, 255);
    }
}
