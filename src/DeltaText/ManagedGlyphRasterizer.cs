using Delta.Maths;
using Delta.Text.Contract;

namespace Delta.Text;

internal static class ManagedGlyphRasterizer
{
    private const uint EdgeSeed = 0xD37A5EEDu;

    internal static GlyphImage Render(
        FontInstanceId font,
        uint glyphId,
        float pixelsPerEm,
        GlyphImageMode mode,
        float distanceRange,
        GlyphOutline outline,
        Rgba32 foreground)
    {
        var hasDistance = mode is GlyphImageMode.Sdf or GlyphImageMode.Msdf;
        var padding = hasDistance ? checked((int)MathF.Ceiling(distanceRange)) : 0;
        var pixelSize = Math.Max(1, (int)MathF.Ceiling(pixelsPerEm));
        var effectiveRange = hasDistance ? distanceRange : 1;
        var layers = outline.Layers;
        var first = FindFirstLayer(layers);
        if (first is null)
        {
            return Empty(font, glyphId, pixelsPerEm, mode, distanceRange);
        }

        if (!MsdfGeometry.TryCreate(first.Contours, pixelSize, pixelSize, padding, EdgeSeed, effectiveRange, out var geometry)
            || geometry is null)
        {
            return Empty(font, glyphId, pixelsPerEm, mode, distanceRange);
        }

        var bounds = geometry.PlaneBounds;
        return mode switch
        {
            GlyphImageMode.Coverage => new GlyphImage(
                font,
                glyphId,
                pixelsPerEm,
                GlyphImageEncoding.CoverageR8,
                0,
                geometry.Width,
                geometry.Height,
                bounds,
                RenderCoverage(geometry)),
            GlyphImageMode.Sdf => new GlyphImage(
                font,
                glyphId,
                pixelsPerEm,
                GlyphImageEncoding.SdfR8,
                distanceRange,
                geometry.Width,
                geometry.Height,
                bounds,
                RenderSdf(geometry, distanceRange)),
            GlyphImageMode.Msdf => RenderMsdf(font, glyphId, pixelsPerEm, distanceRange, first.Contours, pixelSize, padding, bounds),
            GlyphImageMode.Color => RenderColor(font, glyphId, pixelsPerEm, geometry, layers, foreground, bounds),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private static GlyphImage RenderMsdf(
        FontInstanceId font,
        uint glyphId,
        float pixelsPerEm,
        float distanceRange,
        GlyphContours contours,
        int pixelSize,
        int padding,
        TextBounds bounds)
    {
        if (!ManagedMsdf.TryGenerate(contours, pixelSize, pixelSize, padding, distanceRange, EdgeSeed,
                out var width, out var height, out var pixels)
            || pixels is null)
        {
            return Empty(font, glyphId, pixelsPerEm, GlyphImageMode.Msdf, distanceRange);
        }

        return new GlyphImage(
            font,
            glyphId,
            pixelsPerEm,
            GlyphImageEncoding.MsdfRgb8,
            distanceRange,
            width,
            height,
            bounds,
            pixels);
    }

    private static GlyphImage RenderColor(
        FontInstanceId font,
        uint glyphId,
        float pixelsPerEm,
        MsdfGeometry geometry,
        GlyphLayer[] layers,
        Rgba32 foreground,
        TextBounds bounds)
    {
        // INCOMPLETE / OBSOLETE-CANDIDATE: color layers are currently
        // flattened from outline callbacks. Replace this fallback with full
        // COLR v1/SVG paint traversal, layer transforms and palette handling
        // when the font backend exposes those data without losing ownership.
        var pixels = new byte[checked(geometry.Width * geometry.Height * 4)];
        var coverage = Array.Empty<byte>();
        for (var i = 0; i < layers.Length; i++)
        {
            var layer = layers[i];
            if (layer is null || layer.Contours.Contours.Count == 0)
            {
                continue;
            }

            if (!MsdfGeometry.TryCreate(layer.Contours, geometry.Width, geometry.Width, 0, EdgeSeed, 1, out var layerGeometry)
                || layerGeometry is null)
            {
                continue;
            }

            var coverageLength = checked(layerGeometry.Width * layerGeometry.Height);
            if (coverage.Length < coverageLength)
            {
                coverage = new byte[coverageLength];
            }

            RenderCoverage(layerGeometry, coverage.AsSpan(0, coverageLength));
            var color = layer.Color == new Rgba32(255, 255, 255, 255) ? foreground : layer.Color;
            BlendColor(pixels, coverage.AsSpan(0, coverageLength), color, geometry.Width, geometry.Height);
        }

        return new GlyphImage(
            font,
            glyphId,
            pixelsPerEm,
            GlyphImageEncoding.ColorRgba8PremultipliedSrgb,
            0,
            geometry.Width,
            geometry.Height,
            bounds,
            pixels);
    }

    private static void BlendColor(byte[] pixels, ReadOnlySpan<byte> coverage, Rgba32 color, int width, int height)
    {
        var count = Math.Min(coverage.Length, checked(width * height));
        for (var i = 0; i < count; i++)
        {
            var sourceAlpha = coverage[i] * color.Alpha / 255;
            if (sourceAlpha == 0)
            {
                continue;
            }

            var offset = i * 4;
            var destinationAlpha = pixels[offset + 3];
            var inverse = 255 - sourceAlpha;
            pixels[offset] = (byte)Math.Clamp((color.Red * sourceAlpha + pixels[offset] * inverse) / 255, 0, 255);
            pixels[offset + 1] = (byte)Math.Clamp((color.Green * sourceAlpha + pixels[offset + 1] * inverse) / 255, 0, 255);
            pixels[offset + 2] = (byte)Math.Clamp((color.Blue * sourceAlpha + pixels[offset + 2] * inverse) / 255, 0, 255);
            pixels[offset + 3] = (byte)Math.Clamp(sourceAlpha + destinationAlpha * inverse / 255, 0, 255);
        }
    }

    private static byte[] RenderCoverage(MsdfGeometry geometry)
    {
        var pixels = new byte[checked(geometry.Width * geometry.Height)];
        RenderCoverage(geometry, pixels);
        return pixels;
    }

    private static void RenderCoverage(MsdfGeometry geometry, Span<byte> pixels)
    {
        var length = checked(geometry.Width * geometry.Height);
        if (pixels.Length < length)
        {
            throw new ArgumentException("Coverage scratch storage is too small.", nameof(pixels));
        }

        const int samples = 4;
        for (var y = 0; y < geometry.Height; y++)
        {
            for (var x = 0; x < geometry.Width; x++)
            {
                var inside = 0;
                for (var sampleY = 0; sampleY < samples; sampleY++)
                {
                    for (var sampleX = 0; sampleX < samples; sampleX++)
                    {
                        var point = new float2(
                            x + (sampleX + 0.5f) / samples,
                            y + (sampleY + 0.5f) / samples);
                        if (MsdfRasterizer.IsInside(geometry.Edges, point))
                        {
                            inside++;
                        }
                    }
                }

                pixels[y * geometry.Width + x] = (byte)(inside * 255 / (samples * samples));
            }
        }
    }

    private static byte[] RenderSdf(MsdfGeometry geometry, float distanceRange)
    {
        var pixels = new byte[checked(geometry.Width * geometry.Height)];
        for (var y = 0; y < geometry.Height; y++)
        {
            for (var x = 0; x < geometry.Width; x++)
            {
                var point = new float2(x + 0.5f, y + 0.5f);
                var nearest = float.MaxValue;
                var winding = 0;
                for (var i = 0; i < geometry.Edges.Length; i++)
                {
                    var edge = geometry.Edges[i];
                    nearest = DeltaMaths.Min(nearest, MsdfRasterizer.DistanceSquared(point, edge.Start, edge.End));
                    if ((edge.Start.y <= point.y && edge.End.y > point.y)
                        || (edge.Start.y > point.y && edge.End.y <= point.y))
                    {
                        var intersectionX = edge.Start.x
                            + (point.y - edge.Start.y) * (edge.End.x - edge.Start.x)
                            / (edge.End.y - edge.Start.y);
                        if (intersectionX > point.x)
                        {
                            winding += edge.End.y > edge.Start.y ? 1 : -1;
                        }
                    }
                }

                var distance = DeltaMaths.Sqrt(nearest);
                if (winding == 0)
                {
                    distance = -distance;
                }

                pixels[y * geometry.Width + x] = MsdfEncoder.Encode(distance, distanceRange);
            }
        }

        return pixels;
    }

    private static GlyphLayer? FindFirstLayer(GlyphLayer[] layers)
    {
        for (var i = 0; i < layers.Length; i++)
        {
            if (layers[i] is { Contours.Contours.Count: > 0 })
            {
                return layers[i];
            }
        }

        return null;
    }

    private static GlyphImage Empty(
        FontInstanceId font,
        uint glyphId,
        float pixelsPerEm,
        GlyphImageMode mode,
        float distanceRange)
        => new(
            font,
            glyphId,
            pixelsPerEm,
            mode switch
            {
                GlyphImageMode.Coverage => GlyphImageEncoding.CoverageR8,
                GlyphImageMode.Sdf => GlyphImageEncoding.SdfR8,
                GlyphImageMode.Msdf => GlyphImageEncoding.MsdfRgb8,
                GlyphImageMode.Color => GlyphImageEncoding.ColorRgba8PremultipliedSrgb,
                _ => GlyphImageEncoding.Unknown
            },
            mode is GlyphImageMode.Sdf or GlyphImageMode.Msdf ? distanceRange : 0,
            0,
            0,
            default,
            Array.Empty<byte>());
}
