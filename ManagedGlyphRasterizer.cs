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

        var bounds = new TextBounds(0, -geometry.Height, geometry.Width, 0);
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
        var pixels = new byte[checked(geometry.Width * geometry.Height * 4)];
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

            var coverage = RenderCoverage(layerGeometry);
            var color = layer.Color == new Rgba32(255, 255, 255, 255) ? foreground : layer.Color;
            BlendColor(pixels, coverage, color, geometry.Width, geometry.Height);
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

    private static void BlendColor(byte[] pixels, byte[] coverage, Rgba32 color, int width, int height)
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

        return pixels;
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
                for (var i = 0; i < geometry.Edges.Length; i++)
                {
                    nearest = DeltaMaths.Min(nearest, MsdfRasterizer.DistanceSquared(point, geometry.Edges[i].Start, geometry.Edges[i].End));
                }

                var distance = DeltaMaths.Sqrt(nearest);
                if (!MsdfRasterizer.IsInside(geometry.Edges, point))
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
