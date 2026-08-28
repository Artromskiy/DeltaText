using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using ImageRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace FontCheck;

/// <summary>
/// Small independent coverage oracle for the FontCheck fixture.
/// </summary>
/// <remarks>
/// This deliberately does not use DeltaText's outline or raster classes. It
/// consumes the public SixLabors.Fonts renderer callbacks, flattens curves at
/// a fixed tolerance and supersamples the resulting contours. ImageSharp is
/// used by the comparison harness as the independent bitmap surface and PNG
/// encoder; it is not a font renderer itself.
/// </remarks>
internal static class ReferenceFontRenderer
{
    internal static ReferenceBitmap Render(ReadOnlySpan<byte> fontData, string text, float pixelsPerEm)
    {
        var ownedData = fontData.ToArray();
        using var stream = new MemoryStream(ownedData, writable: false);
        var collection = new FontCollection();
        var family = collection.Add(stream);
        var font = family.CreateFont(pixelsPerEm);
        var renderer = new ReferenceGlyphRenderer();
        var options = new TextOptions(font)
        {
            Dpi = 72,
            TextDirection = SixLabors.Fonts.TextDirection.LeftToRight,
            ColorFontSupport = ColorFontSupport.None,
        };

        new TextRenderer(renderer).RenderText(text, options);
        return renderer.Build();
    }
}

internal sealed class ReferenceGlyphRenderer : IGlyphRenderer
{
    private readonly List<ReferenceGlyph> _glyphs = [];
    private ReferenceGlyph? _currentGlyph;
    private ReferenceContour? _currentContour;
    private FillRule _fillRule = FillRule.NonZero;

    public bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
        _currentGlyph = new ReferenceGlyph(bounds);
        _currentContour = null;
        return true;
    }

    public void EndGlyph()
    {
        var glyph = _currentGlyph ?? throw new InvalidOperationException("Reference renderer ended an unopened glyph.");
        glyph.CloseContour();
        _glyphs.Add(glyph);
        _currentGlyph = null;
        _currentContour = null;
    }

    public void BeginText(in FontRectangle bounds)
    {
    }

    public void EndText()
    {
    }

    public void BeginFigure()
    {
        _currentGlyph?.CloseContour();
        _currentContour = null;
    }

    public void EndFigure()
    {
        _currentGlyph?.CloseContour();
        _currentContour = null;
    }

    public void MoveTo(Vector2 point)
    {
        var glyph = CurrentGlyph();
        glyph.CloseContour();
        _currentContour = glyph.BeginContour(point, _fillRule);
    }

    public void LineTo(Vector2 point)
        => CurrentContour().Add(point);

    public void QuadraticBezierTo(Vector2 controlPoint, Vector2 point)
    {
        var contour = CurrentContour();
        var start = contour.Last;
        for (var i = 1; i <= 16; i++)
        {
            var t = i / 16f;
            var inverse = 1f - t;
            contour.Add(
                inverse * inverse * start
                + 2f * inverse * t * controlPoint
                + t * t * point);
        }
    }

    public void CubicBezierTo(Vector2 controlPoint1, Vector2 controlPoint2, Vector2 point)
    {
        var contour = CurrentContour();
        var start = contour.Last;
        for (var i = 1; i <= 24; i++)
        {
            var t = i / 24f;
            var inverse = 1f - t;
            contour.Add(
                inverse * inverse * inverse * start
                + 3f * inverse * inverse * t * controlPoint1
                + 3f * inverse * t * t * controlPoint2
                + t * t * t * point);
        }
    }

    public void ArcTo(float radiusX, float radiusY, float rotation, bool largeArc, bool sweep, Vector2 point)
    {
        // SixLabors' current TrueType/OpenType paths do not emit arcs for the
        // bundled fixtures. Keeping the callback explicit makes the oracle
        // fail closed for a future path type instead of silently inventing a
        // different shape.
        if (!float.IsFinite(radiusX) || !float.IsFinite(radiusY) || !float.IsFinite(rotation)
            || !float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new InvalidDataException("Reference renderer received a non-finite arc.");
        }

        CurrentContour().Add(point);
    }

    public void BeginLayer(Paint? paint, FillRule fillRule, ClipQuad? clipBounds)
        => _fillRule = fillRule;

    public void EndLayer()
    {
    }

    public TextDecorations EnabledDecorations() => TextDecorations.None;

    public void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
    {
    }

    internal ReferenceBitmap Build()
    {
        if (_glyphs.Count == 0)
        {
            return ReferenceBitmap.Empty;
        }

        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;
        for (var i = 0; i < _glyphs.Count; i++)
        {
            var glyph = _glyphs[i];
            left = MathF.Min(left, glyph.Bounds.Left);
            top = MathF.Min(top, glyph.Bounds.Top);
            right = MathF.Max(right, glyph.Bounds.Right);
            bottom = MathF.Max(bottom, glyph.Bounds.Bottom);
        }

        var pixelLeft = (int)MathF.Floor(left);
        var pixelTop = (int)MathF.Floor(top);
        var pixelRight = (int)MathF.Ceiling(right);
        var pixelBottom = (int)MathF.Ceiling(bottom);
        var width = checked(pixelRight - pixelLeft);
        var height = checked(pixelBottom - pixelTop);
        if (width <= 0 || height <= 0)
        {
            return ReferenceBitmap.Empty;
        }

        var pixels = new ImageRgba32[checked(width * height)];
        const int samples = 4;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var covered = 0;
                for (var sampleY = 0; sampleY < samples; sampleY++)
                {
                    for (var sampleX = 0; sampleX < samples; sampleX++)
                    {
                        var point = new Vector2(
                            pixelLeft + x + (sampleX + 0.5f) / samples,
                            pixelTop + y + (sampleY + 0.5f) / samples);
                        if (Contains(point))
                        {
                            covered++;
                        }
                    }
                }

                pixels[y * width + x] = new ImageRgba32(255, 255, 255,
                    (byte)(covered * byte.MaxValue / (samples * samples)));
            }
        }

        return new ReferenceBitmap(width, height, pixels, pixelLeft, pixelTop);
    }

    private bool Contains(Vector2 point)
    {
        for (var i = 0; i < _glyphs.Count; i++)
        {
            if (_glyphs[i].Contains(point))
            {
                return true;
            }
        }

        return false;
    }

    private ReferenceGlyph CurrentGlyph()
        => _currentGlyph ?? throw new InvalidOperationException("Reference renderer received a path outside a glyph.");

    private ReferenceContour CurrentContour()
        => _currentContour ?? throw new InvalidOperationException("Reference renderer received a path point outside a figure.");
}

internal sealed class ReferenceGlyph
{
    private readonly List<ReferenceContour> _contours = [];
    private readonly FontRectangle _bounds;

    internal ReferenceGlyph(FontRectangle bounds)
    {
        _bounds = bounds;
    }

    internal FontRectangle Bounds => _bounds;

    internal ReferenceContour BeginContour(Vector2 point, FillRule fillRule)
    {
        var contour = new ReferenceContour(fillRule);
        contour.Add(point);
        _contours.Add(contour);
        return contour;
    }

    internal void CloseContour()
    {
    }

    internal bool Contains(Vector2 point)
    {
        var winding = 0;
        var parity = false;
        for (var i = 0; i < _contours.Count; i++)
        {
            var contour = _contours[i];
            if (contour.Contains(point, out var contourWinding, out var contourParity))
            {
                winding += contourWinding;
                parity ^= contourParity;
            }
        }

        return winding != 0 || parity;
    }
}

internal sealed class ReferenceContour
{
    private readonly List<Vector2> _points = [];
    private readonly FillRule _fillRule;

    internal ReferenceContour(FillRule fillRule)
    {
        _fillRule = fillRule;
    }

    internal Vector2 Last => _points.Count == 0 ? Vector2.Zero : _points[^1];

    internal void Add(Vector2 point) => _points.Add(point);

    internal bool Contains(Vector2 point, out int winding, out bool parity)
    {
        winding = 0;
        parity = false;
        if (_points.Count < 3)
        {
            return false;
        }

        for (var i = 0; i < _points.Count; i++)
        {
            var current = _points[i];
            var next = _points[(i + 1) % _points.Count];
            if ((current.Y <= point.Y && next.Y > point.Y) || (current.Y > point.Y && next.Y <= point.Y))
            {
                var cross = (next.X - current.X) * (point.Y - current.Y)
                    - (point.X - current.X) * (next.Y - current.Y);
                if (cross == 0)
                {
                    return true;
                }

                if (current.Y <= point.Y)
                {
                    if (cross > 0)
                    {
                        winding++;
                        parity = !parity;
                    }
                }
                else if (cross < 0)
                {
                    winding--;
                    parity = !parity;
                }
            }
        }

        if (_fillRule == FillRule.EvenOdd)
        {
            winding = 0;
        }

        return winding != 0 || parity;
    }
}

internal readonly record struct ReferenceBitmap(
    int Width,
    int Height,
    ImageRgba32[] Pixels,
    int Left,
    int Top)
{
    internal static ReferenceBitmap Empty => new(0, 0, [], 0, 0);
}
