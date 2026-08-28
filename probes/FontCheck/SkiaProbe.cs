using System.Buffers.Binary;
using SkiaSharp;

namespace FontCheck;

/// <summary>
/// Temporary SkiaSharp-only probe for isolating font shaping and outline differences.
/// This file belongs to FontCheck and must not become a DeltaText runtime dependency.
/// </summary>
internal static class SkiaProbe
{
    internal static SkiaProbeResult Capture(
        ReadOnlySpan<byte> fontBytes,
        string text,
        float pixelsPerEm)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (!float.IsFinite(pixelsPerEm) || pixelsPerEm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerEm));
        }

        using var stream = new MemoryStream(fontBytes.ToArray(), writable: false);
        using var typeface = SKTypeface.FromStream(stream)
            ?? throw new InvalidDataException("SkiaSharp could not create a typeface from the supplied font.");
        using var font = new SKFont(typeface, pixelsPerEm, 1, 0)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.None,
            LinearMetrics = true,
            Subpixel = false,
        };

        var glyphs = new ushort[text.Length];
        font.GetGlyphs(text, glyphs);
        if (glyphs.Length != 1)
        {
            throw new InvalidOperationException(
                $"The Skia probe expects one UTF-16 glyph for '{text}', got {glyphs.Length}.");
        }

        var glyphId = glyphs[0];
        var widths = new float[1];
        var glyphBounds = new SKRect[1];
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        font.GetGlyphWidths(glyphs, widths, glyphBounds, paint);

        using var path = font.GetGlyphPath(glyphId)
            ?? throw new InvalidDataException($"SkiaSharp returned no path for glyph {glyphId}.");
        var pathCapture = ReadPath(path);
        var bitmap = Render(path, path.Bounds);
        return new SkiaProbeResult(
            text,
            glyphId,
            widths[0],
            ToRect(glyphBounds[0]),
            pathCapture.Summary,
            pathCapture.Commands,
            bitmap.Width,
            bitmap.Height,
            bitmap.Pixels,
            bitmap.AlphaHash);
    }

    private static SkiaPathCapture ReadPath(SKPath path)
    {
        var verbs = new List<SKPathVerb>();
        var commands = new List<OutlineCommand>();
        var hash = 14695981039346656037UL;
        Span<SKPoint> points = stackalloc SKPoint[4];
        using var iterator = path.CreateRawIterator();
        while (true)
        {
            var verb = iterator.Next(points);
            verbs.Add(verb);
            hash = Append(hash, (byte)verb);
            if (verb == SKPathVerb.Done)
            {
                break;
            }

            var pointCount = verb switch
            {
                SKPathVerb.Move => 1,
                SKPathVerb.Line => 2,
                SKPathVerb.Quad => 3,
                SKPathVerb.Conic => 3,
                SKPathVerb.Cubic => 4,
                SKPathVerb.Close => 1,
                _ => 0,
            };
            switch (verb)
            {
                case SKPathVerb.Move:
                    commands.Add(OutlineCommand.Move(ToPoint(points[0])));
                    break;
                case SKPathVerb.Line:
                    commands.Add(OutlineCommand.Line(ToPoint(points[1])));
                    break;
                case SKPathVerb.Quad:
                    commands.Add(OutlineCommand.Quadratic(
                        ToPoint(points[1]),
                        ToPoint(points[2])));
                    break;
                case SKPathVerb.Cubic:
                    commands.Add(OutlineCommand.Cubic(
                        ToPoint(points[1]),
                        ToPoint(points[2]),
                        ToPoint(points[3])));
                    break;
                case SKPathVerb.Close:
                    commands.Add(OutlineCommand.Close());
                    break;
                case SKPathVerb.Conic:
                    throw new InvalidDataException(
                        "The temporary Skia probe does not normalize conic glyph segments.");
            }

            for (var index = 0; index < pointCount; index++)
            {
                hash = Append(hash, BitConverter.SingleToInt32Bits(points[index].X));
                hash = Append(hash, BitConverter.SingleToInt32Bits(points[index].Y));
            }

            if (verb == SKPathVerb.Conic)
            {
                hash = Append(hash, BitConverter.SingleToInt32Bits(iterator.ConicWeight()));
            }
        }

        return new SkiaPathCapture(
            new SkiaPathSummary(
                ToRect(path.Bounds),
                path.PointCount,
                verbs.Count - 1,
                string.Join(',', verbs.Take(verbs.Count - 1)),
                hash),
            commands.ToArray());
    }

    private static OutlinePoint ToPoint(SKPoint point) => new(point.X, point.Y);

    private static SkiaRect ToRect(SKRect rectangle)
        => new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

    private static SkiaBitmap Render(SKPath path, SKRect bounds)
    {
        const int padding = 32;
        var width = checked((int)MathF.Ceiling(bounds.Width) + padding * 2);
        var height = checked((int)MathF.Ceiling(bounds.Height) + padding * 2);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("SkiaSharp returned an empty glyph path bounds.");
        }

        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(padding - bounds.Left, padding - bounds.Top);
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawPath(path, paint);
        canvas.Flush();

        var pixels = new byte[checked(width * height * 4)];
        var source = bitmap.GetPixelSpan();
        if (source.Length != pixels.Length)
        {
            throw new InvalidDataException(
                $"SkiaSharp returned an unexpected RGBA payload size: {source.Length} instead of {pixels.Length}.");
        }

        source.CopyTo(pixels);

        return new SkiaBitmap(width, height, pixels, AlphaHash(pixels));
    }

    private static ulong AlphaHash(ReadOnlySpan<byte> pixels)
    {
        var hash = 14695981039346656037UL;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            hash ^= pixels[index];
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static ulong Append(ulong hash, byte value)
    {
        hash ^= value;
        return hash * 1099511628211UL;
    }

    private static ulong Append(ulong hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        foreach (var item in bytes)
        {
            hash = Append(hash, item);
        }

        return hash;
    }
}

internal readonly record struct SkiaProbeResult(
    string Text,
    ushort GlyphId,
    float AdvanceX,
    SkiaRect GlyphBounds,
    SkiaPathSummary Path,
    OutlineCommand[] Commands,
    int Width,
    int Height,
    byte[] Pixels,
    ulong AlphaHash);

internal readonly record struct SkiaPathSummary(
    SkiaRect Bounds,
    int PointCount,
    int CommandCount,
    string Verbs,
    ulong Hash);

internal readonly record struct SkiaRect(float Left, float Top, float Right, float Bottom)
{
    public override string ToString()
        => $"({Left:0.###}, {Top:0.###})-({Right:0.###}, {Bottom:0.###})";
}

internal readonly record struct SkiaPathCapture(
    SkiaPathSummary Summary,
    OutlineCommand[] Commands);

internal readonly record struct SkiaBitmap(
    int Width,
    int Height,
    byte[] Pixels,
    ulong AlphaHash);
