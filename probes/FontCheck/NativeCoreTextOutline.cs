using System.Runtime.InteropServices;
using System.Globalization;
using System.Text;
using Delta.Text;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;

namespace FontCheck;

internal static class OutlineComparison
{
    private const int CurveSamples = 32;

    internal static OutlineComparisonSummary Run(
        ReadOnlySpan<byte> fontBytes,
        string fontPath,
        uint glyphId,
        float pixelsPerEm)
    {
        return Summarize(Capture(fontBytes, fontPath, glyphId, pixelsPerEm));
    }

    internal static OutlineComparisonData Capture(
        ReadOnlySpan<byte> fontBytes,
        string fontPath,
        uint glyphId,
        float pixelsPerEm)
    {
        var sixLabors = CaptureSixLabors(fontBytes, glyphId, pixelsPerEm);
        using var nativeFont = new NativeCoreTextFont(fontPath);
        var coreText = NativeCoreTextOutlineReader.Read(nativeFont, glyphId, pixelsPerEm);
        return new OutlineComparisonData(glyphId, ToCommands(sixLabors), coreText.Commands);
    }

    internal static OutlineComparisonSummary Summarize(OutlineComparisonData data)
    {
        var comparison = CompareCommands(data.SixLaborsCommands, data.CoreTextCommands);
        var sixLaborsPoints = FlattenCommands(data.SixLaborsCommands);
        var coreTextPoints = FlattenCommands(data.CoreTextCommands);
        return new OutlineComparisonSummary(
            data.GlyphId,
            data.SixLaborsCommands.Length,
            data.CoreTextCommands.Length,
            sixLaborsPoints.Length,
            coreTextPoints.Length,
            comparison.SameCommandKinds,
            comparison.MaxDirectError,
            comparison.MaxMirroredError,
            comparison.BestTransform,
            BoundsOf(sixLaborsPoints),
            BoundsOf(coreTextPoints));
    }

    internal static OutlinePairSummary ComparePair(
        uint glyphId,
        OutlineCommand[] first,
        OutlineCommand[] second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var comparison = CompareCommands(first, second);
        var firstPoints = FlattenCommands(first);
        var secondPoints = FlattenCommands(second);
        var mirrorY = comparison.BestTransform == "mirror-y";
        var secondOrigin = Transform(secondPoints[0], mirrorY);
        var offset = new OutlinePoint(
            firstPoints[0].X - secondOrigin.X,
            firstPoints[0].Y - secondOrigin.Y);
        var maximumError = 0f;
        var maximumIndex = -1;
        var firstAtMaximum = default(OutlinePoint);
        var secondAtMaximum = default(OutlinePoint);
        var transformedSecondAtMaximum = default(OutlinePoint);
        var pointCount = Math.Min(firstPoints.Length, secondPoints.Length);
        for (var index = 0; index < pointCount; index++)
        {
            var transformed = Transform(secondPoints[index], mirrorY);
            transformed = new OutlinePoint(transformed.X + offset.X, transformed.Y + offset.Y);
            var error = MathF.Max(
                MathF.Abs(firstPoints[index].X - transformed.X),
                MathF.Abs(firstPoints[index].Y - transformed.Y));
            if (error <= maximumError)
            {
                continue;
            }

            maximumError = error;
            maximumIndex = index;
            firstAtMaximum = firstPoints[index];
            secondAtMaximum = secondPoints[index];
            transformedSecondAtMaximum = transformed;
        }

        return new OutlinePairSummary(
            glyphId,
            first.Length,
            second.Length,
            firstPoints.Length,
            secondPoints.Length,
            comparison.SameCommandKinds,
            comparison.MaxDirectError,
            comparison.MaxMirroredError,
            comparison.BestTransform,
            BoundsOf(firstPoints),
            BoundsOf(secondPoints),
            maximumIndex,
            firstAtMaximum,
            secondAtMaximum,
            transformedSecondAtMaximum);
    }

    internal static string FormatCommands(OutlineCommand[] commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var builder = new StringBuilder();
        for (var index = 0; index < commands.Length; index++)
        {
            var command = commands[index];
            builder.Append(index.ToString(CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(command.Kind switch
            {
                0 => "Move",
                1 => "Line",
                2 => "Quadratic",
                3 => "Cubic",
                4 => "Close",
                _ => "Unknown",
            });
            builder.Append(" p1=");
            AppendPoint(builder, command.P1);
            if (command.Kind is 2 or 3)
            {
                builder.Append(" p2=");
                AppendPoint(builder, command.P2);
            }

            if (command.Kind == 3)
            {
                builder.Append(" p3=");
                AppendPoint(builder, command.P3);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void AppendPoint(StringBuilder builder, OutlinePoint point)
    {
        builder.Append('(');
        builder.Append(point.X.ToString("R", CultureInfo.InvariantCulture));
        builder.Append(", ");
        builder.Append(point.Y.ToString("R", CultureInfo.InvariantCulture));
        builder.Append(')');
    }

    internal static byte[] RenderOverlay(OutlineComparisonData data, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var sixLaborsPaths = FlattenSubpaths(data.SixLaborsCommands);
        var coreTextPaths = FlattenSubpaths(data.CoreTextCommands);
        var sixLaborsPoints = FlattenCommands(data.SixLaborsCommands);
        var coreTextPoints = FlattenCommands(data.CoreTextCommands);
        var coreOrigin = Transform(coreTextPoints[0], mirrorY: true);
        var offset = new OutlinePoint(
            sixLaborsPoints[0].X - coreOrigin.X,
            sixLaborsPoints[0].Y - coreOrigin.Y);
        var bounds = BoundsOf(sixLaborsPoints);
        var transformedCoreBounds = BoundsOf(TransformPoints(coreTextPoints, mirrorY: true, offset));
        bounds = Union(bounds, transformedCoreBounds);
        var padding = 32f;
        var scale = MathF.Min(
            (width - 2f * padding) / MathF.Max(bounds.Width, 1f),
            (height - 2f * padding) / MathF.Max(bounds.Height, 1f));
        var pixels = new byte[checked(width * height * 4)];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 32;
            pixels[i + 1] = 32;
            pixels[i + 2] = 32;
            pixels[i + 3] = 255;
        }

        DrawPaths(pixels, width, height, sixLaborsPaths, bounds, scale, padding, mirrorY: false, default,
            70, 210, 255);
        DrawPaths(pixels, width, height, coreTextPaths, bounds, scale, padding, mirrorY: true, offset,
            255, 90, 80);
        return pixels;
    }

    private static GlyphOutline CaptureSixLabors(
        ReadOnlySpan<byte> fontBytes,
        uint glyphId,
        float pixelsPerEm)
    {
        var ownedData = fontBytes.ToArray();
        using var stream = new MemoryStream(ownedData, writable: false);
        var collection = new FontCollection();
        var family = collection.Add(stream);
        var font = family.CreateFont(pixelsPerEm);
        var renderer = new SixLaborsGlyphRenderer();
        var options = new GlyphOptions
        {
            Font = font,
            Dpi = 72,
            HintingMode = HintingMode.None,
            LayoutMode = LayoutMode.HorizontalTopBottom,
            ColorFontSupport = ColorFontSupport.None,
        };

        new TextRenderer(renderer).Render(checked((ushort)glyphId), options);
        for (var i = 0; i < renderer.Glyphs.Count; i++)
        {
            var glyph = renderer.Glyphs[i];
            if (glyph.GlyphId == glyphId && glyph.Outline is not null)
            {
                return glyph.Outline;
            }
        }

        throw new InvalidOperationException(
            $"SixLabors did not provide an outline for glyph {glyphId} in the outline comparison.");
    }

    private static OutlineCommand[] ToCommands(GlyphOutline outline)
    {
        var result = new List<OutlineCommand>();
        for (var layerIndex = 0; layerIndex < outline.Layers.Length; layerIndex++)
        {
            var contours = outline.Layers[layerIndex].Contours.Contours;
            for (var contourIndex = 0; contourIndex < contours.Count; contourIndex++)
            {
                var contour = contours[contourIndex];
                if (contour.Count == 0)
                {
                    continue;
                }

                result.Add(OutlineCommand.Move(ToPoint(contour[0])));
                for (var pointIndex = 1; pointIndex < contour.Count; pointIndex++)
                {
                    var point = contour[pointIndex];
                    switch (point.Kind)
                    {
                        case ContourPointKind.Line:
                            result.Add(OutlineCommand.Line(ToPoint(point)));
                            break;
                        case ContourPointKind.QuadraticControl:
                            if (pointIndex + 1 >= contour.Count
                                || contour[pointIndex + 1].Kind != ContourPointKind.Line)
                            {
                                throw new InvalidDataException("SixLabors emitted an incomplete quadratic outline.");
                            }

                            result.Add(OutlineCommand.Quadratic(
                                ToPoint(point),
                                ToPoint(contour[++pointIndex])));
                            break;
                        case ContourPointKind.CubicControl:
                            if (pointIndex + 2 >= contour.Count
                                || contour[pointIndex + 1].Kind != ContourPointKind.CubicControl
                                || contour[pointIndex + 2].Kind != ContourPointKind.CubicEnd)
                            {
                                throw new InvalidDataException("SixLabors emitted an incomplete cubic outline.");
                            }

                            result.Add(OutlineCommand.Cubic(
                                ToPoint(point),
                                ToPoint(contour[++pointIndex]),
                                ToPoint(contour[++pointIndex])));
                            break;
                        default:
                            throw new InvalidDataException("SixLabors emitted an unknown outline point kind.");
                    }
                }

                result.Add(OutlineCommand.Close());
            }
        }

        return result.ToArray();
    }

    private static OutlinePoint ToPoint(ContourPoint point) => new(point.X, point.Y);

    private static CommandComparison CompareCommands(
        OutlineCommand[] expected,
        OutlineCommand[] actual)
    {
        var sameCommandKinds = expected.Length == actual.Length;
        var expectedPoints = FlattenCommands(expected);
        var actualPoints = FlattenCommands(actual);
        var directError = float.PositiveInfinity;
        var mirroredError = float.PositiveInfinity;
        if (sameCommandKinds && expectedPoints.Length == actualPoints.Length)
        {
            directError = ComparePoints(expectedPoints, actualPoints, mirrorY: false);
            mirroredError = ComparePoints(expectedPoints, actualPoints, mirrorY: true);
        }
        else
        {
            sameCommandKinds = false;
        }

        var bestTransform = directError <= mirroredError ? "direct" : "mirror-y";
        return new CommandComparison(sameCommandKinds, directError, mirroredError, bestTransform);
    }

    private static float ComparePoints(
        OutlinePoint[] expected,
        OutlinePoint[] actual,
        bool mirrorY)
    {
        var actualOrigin = Transform(actual[0], mirrorY);
        var offsetX = expected[0].X - actualOrigin.X;
        var offsetY = expected[0].Y - actualOrigin.Y;
        var maximum = 0f;
        for (var i = 0; i < expected.Length; i++)
        {
            var point = Transform(actual[i], mirrorY);
            maximum = MathF.Max(maximum, MathF.Abs(expected[i].X - (point.X + offsetX)));
            maximum = MathF.Max(maximum, MathF.Abs(expected[i].Y - (point.Y + offsetY)));
        }

        return maximum;
    }

    private static OutlinePoint Transform(OutlinePoint point, bool mirrorY)
        => mirrorY ? new OutlinePoint(point.X, -point.Y) : point;

    private static OutlinePoint[] FlattenCommands(OutlineCommand[] commands)
    {
        var points = new List<OutlinePoint>(commands.Length * 4);
        var current = default(OutlinePoint);
        var start = default(OutlinePoint);
        var hasCurrent = false;
        for (var i = 0; i < commands.Length; i++)
        {
            var command = commands[i];
            switch (command.Kind)
            {
                case 0:
                    current = command.P1;
                    start = current;
                    hasCurrent = true;
                    points.Add(current);
                    break;
                case 1:
                    RequireCurrent(hasCurrent, "line");
                    current = command.P1;
                    points.Add(current);
                    break;
                case 2:
                    RequireCurrent(hasCurrent, "quadratic");
                    for (var step = 1; step <= CurveSamples; step++)
                    {
                        var t = step / (float)CurveSamples;
                        var oneMinusT = 1f - t;
                        points.Add(new OutlinePoint(
                            oneMinusT * oneMinusT * current.X
                                + 2f * oneMinusT * t * command.P1.X
                                + t * t * command.P2.X,
                            oneMinusT * oneMinusT * current.Y
                                + 2f * oneMinusT * t * command.P1.Y
                                + t * t * command.P2.Y));
                    }

                    current = command.P2;
                    break;
                case 3:
                    RequireCurrent(hasCurrent, "cubic");
                    for (var step = 1; step <= CurveSamples; step++)
                    {
                        var t = step / (float)CurveSamples;
                        var oneMinusT = 1f - t;
                        points.Add(new OutlinePoint(
                            oneMinusT * oneMinusT * oneMinusT * current.X
                                + 3f * oneMinusT * oneMinusT * t * command.P1.X
                                + 3f * oneMinusT * t * t * command.P2.X
                                + t * t * t * command.P3.X,
                            oneMinusT * oneMinusT * oneMinusT * current.Y
                                + 3f * oneMinusT * oneMinusT * t * command.P1.Y
                                + 3f * oneMinusT * t * t * command.P2.Y
                                + t * t * t * command.P3.Y));
                    }

                    current = command.P3;
                    break;
                case 4:
                    RequireCurrent(hasCurrent, "close");
                    points.Add(start);
                    current = start;
                    break;
                default:
                    throw new InvalidDataException($"Unknown normalized outline command {command.Kind}.");
            }
        }

        return points.ToArray();
    }

    private static OutlinePoint[][] FlattenSubpaths(OutlineCommand[] commands)
    {
        var paths = new List<OutlinePoint[]>();
        var current = new List<OutlinePoint>();
        var currentPoint = default(OutlinePoint);
        var start = default(OutlinePoint);
        var hasCurrent = false;
        for (var i = 0; i < commands.Length; i++)
        {
            var command = commands[i];
            switch (command.Kind)
            {
                case 0:
                    AddPath(paths, current);
                    current = [command.P1];
                    currentPoint = command.P1;
                    start = command.P1;
                    hasCurrent = true;
                    break;
                case 1:
                    RequireCurrent(hasCurrent, "line");
                    current.Add(command.P1);
                    currentPoint = command.P1;
                    break;
                case 2:
                    RequireCurrent(hasCurrent, "quadratic");
                    AddQuadratic(current, currentPoint, command.P1, command.P2);
                    currentPoint = command.P2;
                    break;
                case 3:
                    RequireCurrent(hasCurrent, "cubic");
                    AddCubic(current, currentPoint, command.P1, command.P2, command.P3);
                    currentPoint = command.P3;
                    break;
                case 4:
                    RequireCurrent(hasCurrent, "close");
                    current.Add(start);
                    currentPoint = start;
                    break;
                default:
                    throw new InvalidDataException($"Unknown normalized outline command {command.Kind}.");
            }
        }

        AddPath(paths, current);
        return paths.ToArray();
    }

    private static void AddQuadratic(
        List<OutlinePoint> points,
        OutlinePoint start,
        OutlinePoint control,
        OutlinePoint end)
    {
        for (var step = 1; step <= CurveSamples; step++)
        {
            var t = step / (float)CurveSamples;
            var oneMinusT = 1f - t;
            points.Add(new OutlinePoint(
                oneMinusT * oneMinusT * start.X
                    + 2f * oneMinusT * t * control.X
                    + t * t * end.X,
                oneMinusT * oneMinusT * start.Y
                    + 2f * oneMinusT * t * control.Y
                    + t * t * end.Y));
        }
    }

    private static void AddCubic(
        List<OutlinePoint> points,
        OutlinePoint start,
        OutlinePoint control1,
        OutlinePoint control2,
        OutlinePoint end)
    {
        for (var step = 1; step <= CurveSamples; step++)
        {
            var t = step / (float)CurveSamples;
            var oneMinusT = 1f - t;
            points.Add(new OutlinePoint(
                oneMinusT * oneMinusT * oneMinusT * start.X
                    + 3f * oneMinusT * oneMinusT * t * control1.X
                    + 3f * oneMinusT * t * t * control2.X
                    + t * t * t * end.X,
                oneMinusT * oneMinusT * oneMinusT * start.Y
                    + 3f * oneMinusT * oneMinusT * t * control1.Y
                    + 3f * oneMinusT * t * t * control2.Y
                    + t * t * t * end.Y));
        }
    }

    private static void AddPath(List<OutlinePoint[]> paths, List<OutlinePoint> path)
    {
        if (path.Count > 1)
        {
            paths.Add(path.ToArray());
        }
    }

    private static OutlinePoint[] TransformPoints(
        OutlinePoint[] points,
        bool mirrorY,
        OutlinePoint offset)
    {
        var transformed = new OutlinePoint[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var point = Transform(points[i], mirrorY);
            transformed[i] = new OutlinePoint(point.X + offset.X, point.Y + offset.Y);
        }

        return transformed;
    }

    private static OutlineBounds Union(OutlineBounds first, OutlineBounds second)
        => new(
            MathF.Min(first.Left, second.Left),
            MathF.Min(first.Top, second.Top),
            MathF.Max(first.Right, second.Right),
            MathF.Max(first.Bottom, second.Bottom));

    private static void DrawPaths(
        byte[] pixels,
        int width,
        int height,
        OutlinePoint[][] paths,
        OutlineBounds bounds,
        float scale,
        float padding,
        bool mirrorY,
        OutlinePoint offset,
        byte red,
        byte green,
        byte blue)
    {
        for (var pathIndex = 0; pathIndex < paths.Length; pathIndex++)
        {
            var path = paths[pathIndex];
            for (var pointIndex = 1; pointIndex < path.Length; pointIndex++)
            {
                var first = ToCanvas(TransformPoints([path[pointIndex - 1]], mirrorY, offset)[0], bounds, scale, padding);
                var second = ToCanvas(TransformPoints([path[pointIndex]], mirrorY, offset)[0], bounds, scale, padding);
                DrawLine(pixels, width, height, first, second, red, green, blue);
            }
        }
    }

    private static OutlinePoint ToCanvas(OutlinePoint point, OutlineBounds bounds, float scale, float padding)
        => new(
            padding + (point.X - bounds.Left) * scale,
            padding + (point.Y - bounds.Top) * scale);

    private static void DrawLine(
        byte[] pixels,
        int width,
        int height,
        OutlinePoint first,
        OutlinePoint second,
        byte red,
        byte green,
        byte blue)
    {
        var distance = MathF.Sqrt(
            (second.X - first.X) * (second.X - first.X)
            + (second.Y - first.Y) * (second.Y - first.Y));
        var steps = Math.Max(1, (int)MathF.Ceiling(distance * 2f));
        for (var step = 0; step <= steps; step++)
        {
            var amount = step / (float)steps;
            SetPixel(pixels, width, height,
                (int)MathF.Round(first.X + (second.X - first.X) * amount),
                (int)MathF.Round(first.Y + (second.Y - first.Y) * amount),
                red, green, blue);
        }
    }

    private static void SetPixel(byte[] pixels, int width, int height, int x, int y, byte red, byte green, byte blue)
    {
        for (var offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                var pixelX = x + offsetX;
                var pixelY = y + offsetY;
                if ((uint)pixelX >= (uint)width || (uint)pixelY >= (uint)height)
                {
                    continue;
                }

                var index = (pixelY * width + pixelX) * 4;
                var isBackground = pixels[index] == 32
                    && pixels[index + 1] == 32
                    && pixels[index + 2] == 32;
                if (isBackground)
                {
                    pixels[index] = red;
                    pixels[index + 1] = green;
                    pixels[index + 2] = blue;
                }
                else
                {
                    pixels[index] = 255;
                    pixels[index + 1] = 255;
                    pixels[index + 2] = 255;
                }
            }
        }
    }

    private static void RequireCurrent(bool hasCurrent, string command)
    {
        if (!hasCurrent)
        {
            throw new InvalidDataException($"Outline emitted {command} before a move command.");
        }
    }

    private static OutlineBounds BoundsOf(OutlinePoint[] points)
    {
        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;
        for (var i = 0; i < points.Length; i++)
        {
            Include(points[i], ref left, ref top, ref right, ref bottom);
        }

        return new OutlineBounds(left, top, right, bottom);
    }

    private static void Include(
        OutlinePoint point,
        ref float left,
        ref float top,
        ref float right,
        ref float bottom)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new InvalidDataException("Outline contains a non-finite point.");
        }

        left = MathF.Min(left, point.X);
        top = MathF.Min(top, point.Y);
        right = MathF.Max(right, point.X);
        bottom = MathF.Max(bottom, point.Y);
    }
}

internal static class NativeCoreTextOutlineReader
{
    internal static NativeOutline Read(NativeCoreTextFont font, uint glyphId, float pixelsPerEm)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(glyphId, ushort.MaxValue);

        var nativeFont = font.GetFont(pixelsPerEm);
        var path = CTFontCreatePathForGlyph(nativeFont, checked((ushort)glyphId), IntPtr.Zero);
        if (path == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CoreText returned no path for glyph {glyphId}.");
        }

        try
        {
            var collector = new PathCollector();
            var callback = new CGPathApplierFunction(collector.Apply);
            CGPathApply(path, IntPtr.Zero, callback);
            return collector.Build();
        }
        finally
        {
            CFRelease(path);
        }
    }

    private sealed class PathCollector
    {
        private readonly List<OutlineCommand> _commands = [];
        private Exception? _error;

        internal void Apply(IntPtr info, IntPtr elementPointer)
        {
            if (_error is not null)
            {
                return;
            }

            try
            {
                var element = Marshal.PtrToStructure<NativePathElement>(elementPointer);
                var points = element.Points;
                switch (element.Type)
                {
                    case NativePathElementType.MoveTo:
                        _commands.Add(OutlineCommand.Move(ReadPoint(points)));
                        break;
                    case NativePathElementType.AddLineTo:
                        _commands.Add(OutlineCommand.Line(ReadPoint(points)));
                        break;
                    case NativePathElementType.AddQuadCurveTo:
                        _commands.Add(OutlineCommand.Quadratic(
                            ReadPoint(points),
                            ReadPoint(IntPtr.Add(points, Marshal.SizeOf<NativeCoreTextPoint>()))));
                        break;
                    case NativePathElementType.AddCurveTo:
                        var pointSize = Marshal.SizeOf<NativeCoreTextPoint>();
                        _commands.Add(OutlineCommand.Cubic(
                            ReadPoint(points),
                            ReadPoint(IntPtr.Add(points, pointSize)),
                            ReadPoint(IntPtr.Add(points, pointSize * 2))));
                        break;
                    case NativePathElementType.CloseSubpath:
                        _commands.Add(OutlineCommand.Close());
                        break;
                    default:
                        throw new InvalidDataException($"CoreText returned unknown CGPath element {element.Type}.");
                }
            }
            catch (Exception exception)
            {
                _error = exception;
            }
        }

        internal NativeOutline Build()
        {
            if (_error is not null)
            {
                throw new InvalidDataException("CoreText path extraction failed.", _error);
            }

            if (_commands.Count == 0)
            {
                throw new InvalidDataException("CoreText returned an empty glyph path.");
            }

            return new NativeOutline(_commands.ToArray());
        }

        private static OutlinePoint ReadPoint(IntPtr pointer)
        {
            var point = Marshal.PtrToStructure<NativeCoreTextPoint>(pointer);
            return new OutlinePoint((float)point.X, (float)point.Y);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CGPathApplierFunction(IntPtr info, IntPtr element);

    private enum NativePathElementType : int
    {
        MoveTo = 0,
        AddLineTo = 1,
        AddQuadCurveTo = 2,
        AddCurveTo = 3,
        CloseSubpath = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePathElement
    {
        internal NativePathElementType Type;
        internal IntPtr Points;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCoreTextPoint
    {
        internal double X;
        internal double Y;
    }

    [DllImport("/System/Library/Frameworks/CoreText.framework/CoreText", ExactSpelling = true)]
    private static extern IntPtr CTFontCreatePathForGlyph(IntPtr font, ushort glyph, IntPtr transform);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", ExactSpelling = true)]
    private static extern void CGPathApply(IntPtr path, IntPtr info, CGPathApplierFunction function);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", ExactSpelling = true)]
    private static extern void CFRelease(IntPtr handle);
}

internal readonly record struct NativeOutline(OutlineCommand[] Commands);

internal readonly record struct OutlineComparisonData(
    uint GlyphId,
    OutlineCommand[] SixLaborsCommands,
    OutlineCommand[] CoreTextCommands);

internal readonly record struct OutlineComparisonSummary(
    uint GlyphId,
    int SixLaborsCommandCount,
    int CoreTextCommandCount,
    int SixLaborsPointCount,
    int CoreTextPointCount,
    bool SameCommandKinds,
    float DirectMaximumError,
    float MirroredMaximumError,
    string BestTransform,
    OutlineBounds SixLaborsBounds,
    OutlineBounds CoreTextBounds);

internal readonly record struct OutlinePairSummary(
    uint GlyphId,
    int FirstCommandCount,
    int SecondCommandCount,
    int FirstPointCount,
    int SecondPointCount,
    bool SameCommandKinds,
    float DirectMaximumError,
    float MirroredMaximumError,
    string BestTransform,
    OutlineBounds FirstBounds,
    OutlineBounds SecondBounds,
    int MaximumErrorPointIndex,
    OutlinePoint FirstPointAtMaximum,
    OutlinePoint SecondPointAtMaximum,
    OutlinePoint TransformedSecondPointAtMaximum);

internal readonly record struct OutlineBounds(float Left, float Top, float Right, float Bottom)
{
    internal float Width => Right - Left;

    internal float Height => Bottom - Top;
}

internal readonly record struct OutlinePoint(float X, float Y);

internal readonly record struct OutlineCommand(
    byte Kind,
    OutlinePoint P1,
    OutlinePoint P2,
    OutlinePoint P3)
{
    internal static OutlineCommand Move(OutlinePoint point) => new(0, point, default, default);

    internal static OutlineCommand Line(OutlinePoint point) => new(1, point, default, default);

    internal static OutlineCommand Quadratic(OutlinePoint control, OutlinePoint end)
        => new(2, control, end, default);

    internal static OutlineCommand Cubic(OutlinePoint control1, OutlinePoint control2, OutlinePoint end)
        => new(3, control1, control2, end);

    internal static OutlineCommand Close() => new(4, default, default, default);
}

internal readonly record struct CommandComparison(
    bool SameCommandKinds,
    float MaxDirectError,
    float MaxMirroredError,
    string BestTransform);
