using System.Diagnostics.CodeAnalysis;
using Delta.Maths;
using Delta.Text.Contract;

namespace Delta.Text;

internal sealed class MsdfGeometry
{
    private const int MaximumFlattenDepth = 10;
    private const float MinimumLengthSquared = 1e-8f;
    private const float CornerCosine = 0.875f;

    private MsdfGeometry(
        int width,
        int height,
        MsdfEdge[] edges,
        MsdfEdgeGrid grid,
        TextBounds planeBounds)
    {
        Width = width;
        Height = height;
        Edges = edges;
        Grid = grid;
        PlaneBounds = planeBounds;
    }

    internal int Width { get; }
    internal int Height { get; }
    internal MsdfEdge[] Edges { get; }
    internal MsdfEdgeGrid Grid { get; }
    internal TextBounds PlaneBounds { get; }

    internal static bool TryCreate(
        GlyphContours contours,
        int pixelSize,
        int unitsPerEm,
        int padding,
        uint edgeSeed,
        float distanceRange,
        [NotNullWhen(true)] out MsdfGeometry? geometry)
    {
        geometry = null;
        var logicalEdges = new List<LogicalEdge>();
        var contourRanges = new List<ContourRange>();
        if (!TryReadLogicalEdges(contours, logicalEdges, contourRanges))
        {
            return false;
        }

        var channels = ColorEdges(logicalEdges, contourRanges, edgeSeed);
        var scale = pixelSize / (float)unitsPerEm;
        var tolerance = 0.25f / scale;
        var flatEdges = new List<FlatEdge>(logicalEdges.Count * 4);
        foreach (var range in contourRanges)
        {
            var end = range.Start + range.Count;
            for (var i = range.Start; i < end; i++)
            {
                Flatten(logicalEdges[i], tolerance, channels[i], flatEdges);
            }
        }

        if (flatEdges.Count == 0)
        {
            return false;
        }

        var bounds = BoundsOf(flatEdges);
        if (!TryGetImageSize(bounds, scale, padding, out var width, out var height))
        {
            return false;
        }

        var edges = new MsdfEdge[flatEdges.Count];
        for (var i = 0; i < flatEdges.Count; i++)
        {
            var edge = flatEdges[i];
            edges[i] = new MsdfEdge(
                ToPixel(edge.Start, bounds, scale, padding),
                ToPixel(edge.End, bounds, scale, padding),
                edge.Channel);
        }

        var margin = padding + (padding > 0 ? 1 : 0);
        var planeLeft = bounds.Left * scale - margin;
        var planeTop = bounds.Top * scale - margin;
        geometry = new MsdfGeometry(
            width,
            height,
            edges,
            MsdfEdgeGrid.Create(edges, width, height, distanceRange),
            new TextBounds(planeLeft, planeTop, planeLeft + width, planeTop + height));
        return true;
    }

    private static bool TryReadLogicalEdges(
        GlyphContours contours,
        List<LogicalEdge> edges,
        List<ContourRange> ranges)
    {
        foreach (var source in contours.Contours)
        {
            if (source.Count < 2)
            {
                return false;
            }

            var contourStart = edges.Count;
            if (!IsFinite(source[0]))
            {
                return false;
            }

            var start = ToVector(source[0]);
            var current = start;
            for (var i = 1; i < source.Count;)
            {
                var point = source[i];
                if (!IsFinite(point))
                {
                    return false;
                }

                switch (point.Kind)
                {
                    case ContourPointKind.Line:
                        var lineEnd = ToVector(point);
                        AddLine(edges, current, lineEnd);
                        current = lineEnd;
                        i++;
                        break;
                    case ContourPointKind.QuadraticControl:
                        if (i + 1 >= source.Count || source[i + 1].Kind != ContourPointKind.Line)
                        {
                            return false;
                        }

                        var quadraticEnd = ToVector(source[i + 1]);
                        edges.Add(new LogicalEdge(current, ToVector(point), default, quadraticEnd, CurveKind.Quadratic));
                        current = quadraticEnd;
                        i += 2;
                        break;
                    case ContourPointKind.CubicControl:
                        if (i + 2 >= source.Count || source[i + 1].Kind != ContourPointKind.CubicControl
                            || source[i + 2].Kind != ContourPointKind.CubicEnd)
                        {
                            return false;
                        }

                        var cubicEnd = ToVector(source[i + 2]);
                        edges.Add(new LogicalEdge(
                            current,
                            ToVector(point),
                            ToVector(source[i + 1]),
                            cubicEnd,
                            CurveKind.Cubic));
                        current = cubicEnd;
                        i += 3;
                        break;
                    default:
                        return false;
                }
            }

            AddLine(edges, current, start);
            var count = edges.Count - contourStart;
            if (count == 0)
            {
                return false;
            }

            ranges.Add(new ContourRange(contourStart, count));
        }

        return edges.Count > 0;
    }

    private static void AddLine(List<LogicalEdge> edges, float2 start, float2 end)
    {
        if (float2.SqrLength(start - end) > MinimumLengthSquared)
        {
            edges.Add(new LogicalEdge(start, default, default, end, CurveKind.Line));
        }
    }

    private static byte[] ColorEdges(List<LogicalEdge> edges, List<ContourRange> ranges, uint seed)
    {
        // INCOMPLETE / OBSOLETE-CANDIDATE: this deterministic round-robin
        // coloring is sufficient for the current managed MSDF fixtures, but a
        // production-quality backend should validate edge colors at corners
        // and use a bounded fallback for difficult contours.
        var channels = new byte[edges.Count];
        var channel = (byte)(seed % 3);
        foreach (var range in ranges)
        {
            var end = range.Start + range.Count;
            for (var i = range.Start; i < end; i++)
            {
                channels[i] = channel;
                var next = range.Start + ((i - range.Start + 1) % range.Count);
                if (IsCorner(edges[i], edges[next]))
                {
                    channel = NextChannel(channel);
                }
            }
        }

        return channels;
    }

    private static bool IsCorner(LogicalEdge current, LogicalEdge next)
    {
        var incoming = EndTangent(current);
        var outgoing = StartTangent(next);
        var incomingLength = float2.SqrLength(incoming);
        var outgoingLength = float2.SqrLength(outgoing);
        if (incomingLength <= MinimumLengthSquared || outgoingLength <= MinimumLengthSquared)
        {
            return true;
        }

        var cosine = float2.Dot(incoming, outgoing) / DeltaMaths.Sqrt(incomingLength * outgoingLength);
        return cosine < CornerCosine;
    }

    private static float2 StartTangent(LogicalEdge edge)
        => edge.Kind switch
        {
            CurveKind.Line => edge.End - edge.Start,
            CurveKind.Quadratic or CurveKind.Cubic => edge.Control1 - edge.Start,
            _ => float2.zero
        };

    private static float2 EndTangent(LogicalEdge edge)
        => edge.Kind switch
        {
            CurveKind.Line => edge.End - edge.Start,
            CurveKind.Quadratic => edge.End - edge.Control1,
            CurveKind.Cubic => edge.End - edge.Control2,
            _ => float2.zero
        };

    private static byte NextChannel(byte channel) => (byte)((channel + 1) % 3);

    private static void Flatten(LogicalEdge edge, float tolerance, byte channel, List<FlatEdge> output)
    {
        var toleranceSquared = tolerance * tolerance;
        switch (edge.Kind)
        {
            case CurveKind.Line:
                AddFlatEdge(output, edge.Start, edge.End, channel);
                break;
            case CurveKind.Quadratic:
                FlattenQuadratic(edge.Start, edge.Control1, edge.End, toleranceSquared, channel, output, 0);
                break;
            case CurveKind.Cubic:
                FlattenCubic(edge.Start, edge.Control1, edge.Control2, edge.End, toleranceSquared, channel, output, 0);
                break;
        }
    }

    private static void FlattenQuadratic(
        float2 start,
        float2 control,
        float2 end,
        float toleranceSquared,
        byte channel,
        List<FlatEdge> output,
        int depth)
    {
        if (depth >= MaximumFlattenDepth || DistanceSquaredToLine(control, start, end) <= toleranceSquared)
        {
            AddFlatEdge(output, start, end, channel);
            return;
        }

        var startControl = (start + control) * 0.5f;
        var controlEnd = (control + end) * 0.5f;
        var midpoint = (startControl + controlEnd) * 0.5f;
        FlattenQuadratic(start, startControl, midpoint, toleranceSquared, channel, output, depth + 1);
        FlattenQuadratic(midpoint, controlEnd, end, toleranceSquared, channel, output, depth + 1);
    }

    private static void FlattenCubic(
        float2 start,
        float2 control1,
        float2 control2,
        float2 end,
        float toleranceSquared,
        byte channel,
        List<FlatEdge> output,
        int depth)
    {
        var flatness = DeltaMaths.Max(
            DistanceSquaredToLine(control1, start, end),
            DistanceSquaredToLine(control2, start, end));
        if (depth >= MaximumFlattenDepth || flatness <= toleranceSquared)
        {
            AddFlatEdge(output, start, end, channel);
            return;
        }

        var startControl1 = (start + control1) * 0.5f;
        var control1Control2 = (control1 + control2) * 0.5f;
        var control2End = (control2 + end) * 0.5f;
        var leftControl2 = (startControl1 + control1Control2) * 0.5f;
        var rightControl1 = (control1Control2 + control2End) * 0.5f;
        var midpoint = (leftControl2 + rightControl1) * 0.5f;
        FlattenCubic(start, startControl1, leftControl2, midpoint, toleranceSquared, channel, output, depth + 1);
        FlattenCubic(midpoint, rightControl1, control2End, end, toleranceSquared, channel, output, depth + 1);
    }

    private static void AddFlatEdge(List<FlatEdge> output, float2 start, float2 end, byte channel)
    {
        if (float2.SqrLength(start - end) > MinimumLengthSquared)
        {
            output.Add(new FlatEdge(start, end, channel));
        }
    }

    private static float DistanceSquaredToLine(float2 point, float2 start, float2 end)
    {
        var delta = end - start;
        var lengthSquared = float2.SqrLength(delta);
        if (lengthSquared <= MinimumLengthSquared)
        {
            return float2.SqrLength(point - start);
        }

        var cross = delta.x * (start.y - point.y) - delta.y * (start.x - point.x);
        return cross * cross / lengthSquared;
    }

    private static Bounds BoundsOf(List<FlatEdge> edges)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        foreach (var edge in edges)
        {
            minX = DeltaMaths.Min(minX, DeltaMaths.Min(edge.Start.x, edge.End.x));
            minY = DeltaMaths.Min(minY, DeltaMaths.Min(edge.Start.y, edge.End.y));
            maxX = DeltaMaths.Max(maxX, DeltaMaths.Max(edge.Start.x, edge.End.x));
            maxY = DeltaMaths.Max(maxY, DeltaMaths.Max(edge.Start.y, edge.End.y));
        }

        return new Bounds(minX, minY, maxX, maxY);
    }

    private static bool TryGetImageSize(Bounds bounds, float scale, int padding, out int width, out int height)
    {
        width = 0;
        height = 0;
        var widthPixels = Math.Ceiling((double)(bounds.Right - bounds.Left) * scale);
        var heightPixels = Math.Ceiling((double)(bounds.Bottom - bounds.Top) * scale);
        // Distance fields need one guard pixel on both sides of the requested
        // padding. Coverage and color images do not: their pixel rectangle is
        // the glyph bounds itself and the extra border would distort layout.
        var edgeMargin = padding > 0 ? 2L : 0L;
        var extra = (long)padding * 2 + edgeMargin;
        if (!double.IsFinite(widthPixels) || !double.IsFinite(heightPixels)
            || widthPixels < 0 || heightPixels < 0
            || widthPixels > int.MaxValue - extra || heightPixels > int.MaxValue - extra)
        {
            return false;
        }

        width = Math.Max(1, checked((int)widthPixels + (int)extra));
        height = Math.Max(1, checked((int)heightPixels + (int)extra));
        return true;
    }

    private static float2 ToPixel(float2 point, Bounds bounds, float scale, int padding)
        // SixLabors.Fonts outline callbacks use the contract's top-to-bottom
        // device coordinates. Keep Y monotonic here; inverting it would make
        // glyphs appear upside down in the CPU image.
        => new(
            (point.x - bounds.Left) * scale + padding + (padding > 0 ? 1 : 0),
            (point.y - bounds.Top) * scale + padding + (padding > 0 ? 1 : 0));

    private static float2 ToVector(ContourPoint point) => new(point.X, point.Y);

    private static bool IsFinite(ContourPoint point)
        => float.IsFinite(point.X) && float.IsFinite(point.Y) && point.Kind <= ContourPointKind.CubicEnd;

    private readonly record struct Bounds(float Left, float Top, float Right, float Bottom);
    private readonly record struct ContourRange(int Start, int Count);
    private readonly record struct FlatEdge(float2 Start, float2 End, byte Channel);

    private readonly struct LogicalEdge
    {
        internal LogicalEdge(float2 start, float2 control1, float2 control2, float2 end, CurveKind kind)
        {
            Start = start;
            Control1 = control1;
            Control2 = control2;
            End = end;
            Kind = kind;
        }

        internal float2 Start { get; }
        internal float2 Control1 { get; }
        internal float2 Control2 { get; }
        internal float2 End { get; }
        internal CurveKind Kind { get; }
    }

    private enum CurveKind : byte
    {
        Line,
        Quadratic,
        Cubic
    }
}

internal readonly struct MsdfEdge
{
    internal MsdfEdge(float2 start, float2 end, byte channel)
    {
        Start = start;
        End = end;
        Channel = channel;
    }

    internal float2 Start { get; }
    internal float2 End { get; }
    internal byte Channel { get; }
}

internal sealed class MsdfEdgeGrid
{
    private MsdfEdgeGrid(int cellSize, int columns, int rows, int[] offsets, int[] edgeIndices)
    {
        CellSize = cellSize;
        Columns = columns;
        Rows = rows;
        Offsets = offsets;
        EdgeIndices = edgeIndices;
    }

    internal int CellSize { get; }
    internal int Columns { get; }
    internal int Rows { get; }
    internal int[] Offsets { get; }
    internal int[] EdgeIndices { get; }

    internal static MsdfEdgeGrid Create(MsdfEdge[] edges, int width, int height, float distanceRange)
    {
        var cellSize = Math.Max(4, checked((int)MathF.Ceiling(distanceRange)));
        var columns = (width + cellSize - 1) / cellSize;
        var rows = (height + cellSize - 1) / cellSize;
        var buckets = new List<int>[checked(columns * rows)];
        for (var edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
        {
            var edge = edges[edgeIndex];
            var minX = Math.Clamp((int)MathF.Floor(DeltaMaths.Min(edge.Start.x, edge.End.x) / cellSize), 0, columns - 1);
            var maxX = Math.Clamp((int)MathF.Floor(DeltaMaths.Max(edge.Start.x, edge.End.x) / cellSize), 0, columns - 1);
            var minY = Math.Clamp((int)MathF.Floor(DeltaMaths.Min(edge.Start.y, edge.End.y) / cellSize), 0, rows - 1);
            var maxY = Math.Clamp((int)MathF.Floor(DeltaMaths.Max(edge.Start.y, edge.End.y) / cellSize), 0, rows - 1);
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var cell = y * columns + x;
                    var bucket = buckets[cell];
                    if (bucket is null)
                    {
                        bucket = new List<int>(4);
                        buckets[cell] = bucket;
                    }

                    bucket.Add(edgeIndex);
                }
            }
        }

        var offsets = new int[buckets.Length + 1];
        for (var i = 0; i < buckets.Length; i++)
        {
            offsets[i + 1] = checked(offsets[i] + (buckets[i]?.Count ?? 0));
        }

        var edgeIndices = new int[offsets[^1]];
        for (var i = 0; i < buckets.Length; i++)
        {
            var bucket = buckets[i];
            bucket?.CopyTo(edgeIndices, offsets[i]);
        }

        return new MsdfEdgeGrid(cellSize, columns, rows, offsets, edgeIndices);
    }
}
