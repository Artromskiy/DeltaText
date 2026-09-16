using global::Delta;

namespace Delta.Text;

internal static class MsdfRasterizer
{
    internal static byte[] Render(MsdfGeometry geometry, float distanceRange)
        => Render(geometry, distanceRange, false);

    internal static byte[] RenderMtsdf(MsdfGeometry geometry, float distanceRange)
        => Render(geometry, distanceRange, true);

    private static byte[] Render(MsdfGeometry geometry, float distanceRange, bool includeTrueDistance)
    {
        // INCOMPLETE / OBSOLETE-CANDIDATE: the current 3x3 grid neighborhood
        // and winding test are a deterministic managed baseline. Replace or
        // augment them with measured broad-phase and corner-quality validation
        // before treating this as a final high-volume rasterizer.
        var bytesPerPixel = includeTrueDistance ? 4 : 3;
        var pixels = new byte[checked(geometry.Width * geometry.Height * bytesPerPixel)];
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
                var trueNearest = rangeSquared;
                var cellX = Maths.Clamp(x / grid.CellSize, 0, grid.Columns - 1);
                var cellY = Maths.Clamp(y / grid.CellSize, 0, grid.Rows - 1);
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
                            nearest = Maths.Min(nearest, distanceSquared);
                            switch (edge.Channel)
                            {
                                case 0:
                                    red = Maths.Min(red, distanceSquared);
                                    break;
                                case 1:
                                    green = Maths.Min(green, distanceSquared);
                                    break;
                                default:
                                    blue = Maths.Min(blue, distanceSquared);
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

                if (includeTrueDistance)
                {
                    for (var i = 0; i < edges.Length; i++)
                    {
                        trueNearest = Maths.Min(trueNearest, DistanceSquared(sample, edges[i].Start, edges[i].End));
                    }
                }

                // All channels use the contour winding sign. A distant colored
                // edge must not amplify that sign change across a boundary texel:
                // bilinear filtering would move the zero crossing and create spikes.
                // Protect the contour's bilinear footprint with the true distance;
                // keep the separated channels outside this filter and derivative band.
                var contourDistance = nearest;
                if (contourDistance <= 2f)
                {
                    red = contourDistance;
                    green = contourDistance;
                    blue = contourDistance;
                }

                var pixel = checked((y * geometry.Width + x) * bytesPerPixel);
                pixels[pixel] = MsdfEncoder.Encode(sign * Maths.Sqrt(red), distanceRange);
                pixels[pixel + 1] = MsdfEncoder.Encode(sign * Maths.Sqrt(green), distanceRange);
                pixels[pixel + 2] = MsdfEncoder.Encode(sign * Maths.Sqrt(blue), distanceRange);
                if (includeTrueDistance)
                {
                    pixels[pixel + 3] = MsdfEncoder.Encode(sign * Maths.Sqrt(trueNearest), distanceRange);
                }
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
        projection = Maths.Clamp(projection, 0, 1);
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
        var normalized = Maths.Clamp(0.5f + signedDistance / (2f * distanceRange), 0, 1);
        return (byte)Maths.Clamp((int)Maths.Round(normalized * 255f), 0, 255);
    }
}
