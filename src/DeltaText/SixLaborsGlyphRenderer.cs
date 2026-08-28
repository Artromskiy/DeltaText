using Delta.Maths;
using Delta.Text.Contract;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixVector2 = System.Numerics.Vector2;

namespace Delta.Text;

/// <summary>Collects SixLabors outline callbacks into DeltaText's internal geometry.</summary>
internal sealed class SixLaborsGlyphRenderer : IGlyphRenderer
{
    private readonly List<GlyphLayer> _layers = new();
    private readonly List<CapturedGlyph> _glyphs = new();
    private GlyphLayer? _currentLayer;
    private GlyphContours? _currentContour;
    private CapturedGlyph? _currentGlyph;
    private bool _skipCurrentGlyph;

    internal IReadOnlyList<CapturedGlyph> Glyphs => _glyphs;

    public bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
        _layers.Clear();
        _currentLayer = null;
        _currentContour = null;
        _currentGlyph = null;
        _skipCurrentGlyph = IsBidiFormatting(parameters.CodePoint.Value);
        if (_skipCurrentGlyph)
        {
            return false;
        }

        var textRun = parameters.TextRun;
        if (textRun is null || textRun.Font is null)
        {
            throw new InvalidOperationException("SixLabors did not provide a text run for a glyph.");
        }

        _currentGlyph = new CapturedGlyph(
            parameters.GlyphId,
            parameters.GraphemeIndex,
            textRun.Font.Family,
            bounds);
        return true;
    }

    public void EndGlyph()
    {
        if (_skipCurrentGlyph)
        {
            _skipCurrentGlyph = false;
            return;
        }

        if (_currentGlyph is null)
        {
            throw new InvalidOperationException("SixLabors ended a glyph that was not started.");
        }

        if (TryBuildOutline(_currentGlyph.GlyphId, out var outline) && outline is not null)
        {
            _currentGlyph.Outline = outline;
        }

        _glyphs.Add(_currentGlyph);

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
        _currentContour = null;
    }

    public void EndFigure()
    {
        _currentContour?.Close();
        _currentContour = null;
    }

    public void MoveTo(SixVector2 point)
    {
        var layer = EnsureLayer();
        layer.Contours.BeginContour(point.X, point.Y);
        _currentContour = layer.Contours;
    }

    public void LineTo(SixVector2 point)
    {
        EnsureContour().LineTo(point.X, point.Y);
    }

    public void QuadraticBezierTo(SixVector2 controlPoint, SixVector2 point)
    {
        EnsureContour().QuadraticTo(controlPoint.X, controlPoint.Y, point.X, point.Y);
    }

    public void CubicBezierTo(SixVector2 controlPoint1, SixVector2 controlPoint2, SixVector2 point)
    {
        EnsureContour().CubicTo(
            controlPoint1.X,
            controlPoint1.Y,
            controlPoint2.X,
            controlPoint2.Y,
            point.X,
            point.Y);
    }

    public void ArcTo(float radiusX, float radiusY, float rotation, bool largeArc, bool sweep, SixVector2 point)
    {
        var contour = EnsureContour();
        if (!contour.TryGetCurrentPoint(out var startX, out var startY))
        {
            throw new InvalidOperationException("The font renderer emitted an arc before MoveTo.");
        }

        if (!float.IsFinite(radiusX) || !float.IsFinite(radiusY) || !float.IsFinite(rotation)
            || !float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new InvalidOperationException("The font renderer emitted a non-finite arc.");
        }

        var end = new float2(point.X, point.Y);
        var start = new float2(startX, startY);
        var radiiX = MathF.Abs(radiusX);
        var radiiY = MathF.Abs(radiusY);
        if (radiiX <= float.Epsilon || radiiY <= float.Epsilon)
        {
            contour.LineTo(end.x, end.y);
            return;
        }

        var delta = end - start;
        if (float2.SqrLength(delta) <= 1e-8f)
        {
            return;
        }

        var phi = DeltaMaths.Radians(rotation);
        var cosPhi = DeltaMaths.Cos(phi);
        var sinPhi = DeltaMaths.Sin(phi);
        var halfDelta = delta * 0.5f;
        var prime = new float2(
            cosPhi * halfDelta.x + sinPhi * halfDelta.y,
            -sinPhi * halfDelta.x + cosPhi * halfDelta.y);
        var lambda = prime.x * prime.x / (radiiX * radiiX)
            + prime.y * prime.y / (radiiY * radiiY);
        if (lambda > 1f)
        {
            var scale = DeltaMaths.Sqrt(lambda);
            radiiX *= scale;
            radiiY *= scale;
        }

        var radiiXSquared = radiiX * radiiX;
        var radiiYSquared = radiiY * radiiY;
        var denominator = radiiXSquared * prime.y * prime.y + radiiYSquared * prime.x * prime.x;
        if (denominator <= 1e-8f)
        {
            contour.LineTo(end.x, end.y);
            return;
        }

        var numerator = radiiXSquared * radiiYSquared
            - radiiXSquared * prime.y * prime.y
            - radiiYSquared * prime.x * prime.x;
        var sign = largeArc == sweep ? -1f : 1f;
        var coefficient = sign * DeltaMaths.Sqrt(MathF.Max(0f, numerator / denominator));
        var centerPrime = new float2(
            coefficient * radiiX * prime.y / radiiY,
            coefficient * -radiiY * prime.x / radiiX);
        var center = new float2(
            cosPhi * centerPrime.x - sinPhi * centerPrime.y + (start.x + end.x) * 0.5f,
            sinPhi * centerPrime.x + cosPhi * centerPrime.y + (start.y + end.y) * 0.5f);
        var startVector = new float2(
            (prime.x - centerPrime.x) / radiiX,
            (prime.y - centerPrime.y) / radiiY);
        var endVector = new float2(
            (-prime.x - centerPrime.x) / radiiX,
            (-prime.y - centerPrime.y) / radiiY);
        var startAngle = DeltaMaths.Atan2(startVector.y, startVector.x);
        var sweepAngle = DeltaMaths.Atan2(
            startVector.x * endVector.y - startVector.y * endVector.x,
            startVector.x * endVector.x + startVector.y * endVector.y);
        if (!sweep && sweepAngle > 0f)
        {
            sweepAngle -= 2f * MathF.PI;
        }
        else if (sweep && sweepAngle < 0f)
        {
            sweepAngle += 2f * MathF.PI;
        }

        var segmentCount = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweepAngle) / (MathF.PI * 0.5f)));
        var segmentAngle = sweepAngle / segmentCount;
        var tangentScale = 4f / 3f * MathF.Tan(segmentAngle * 0.25f);
        var angle = startAngle;
        for (var i = 0; i < segmentCount; i++)
        {
            var nextAngle = angle + segmentAngle;
            var cosAngle = DeltaMaths.Cos(angle);
            var sinAngle = DeltaMaths.Sin(angle);
            var cosNext = DeltaMaths.Cos(nextAngle);
            var sinNext = DeltaMaths.Sin(nextAngle);
            var first = TransformEllipse(center, radiiX, radiiY, cosPhi, sinPhi, cosAngle, sinAngle);
            var last = i == segmentCount - 1
                ? end
                : TransformEllipse(center, radiiX, radiiY, cosPhi, sinPhi, cosNext, sinNext);
            var firstTangent = TransformEllipseTangent(radiiX, radiiY, cosPhi, sinPhi, -sinAngle, cosAngle);
            var lastTangent = TransformEllipseTangent(radiiX, radiiY, cosPhi, sinPhi, -sinNext, cosNext);
            contour.CubicTo(
                first.x + tangentScale * firstTangent.x,
                first.y + tangentScale * firstTangent.y,
                last.x - tangentScale * lastTangent.x,
                last.y - tangentScale * lastTangent.y,
                last.x,
                last.y);
            angle = nextAngle;
        }
    }

    public void BeginLayer(Paint? paint, FillRule fillRule)
    {
        _currentLayer = new GlyphLayer(ToColor(paint));
        _layers.Add(_currentLayer);
        _currentContour = null;
    }

    public void EndLayer()
    {
        _currentContour = null;
    }

    public void BeginGroup(CompositeMode mode)
    {
    }

    public void EndGroup()
    {
    }

    public TextDecorations EnabledDecorations() => TextDecorations.None;

    public void SetDecoration(
        TextDecorations textDecorations,
        SixVector2 start,
        SixVector2 end,
        float thickness,
        ReadOnlyMemory<float> dashPattern)
    {
    }

    private bool TryBuildOutline(uint glyphId, out GlyphOutline? outline)
    {
        outline = null;
        if (_layers.Count == 0)
        {
            return false;
        }

        var layers = new List<GlyphLayer>(_layers.Count);
        for (var i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].Contours.Contours.Count > 0)
            {
                layers.Add(_layers[i]);
            }
        }

        if (layers.Count == 0)
        {
            return false;
        }

        outline = new GlyphOutline(glyphId, layers.ToArray());
        return true;
    }

    private GlyphLayer EnsureLayer()
    {
        if (_currentLayer is not null)
        {
            return _currentLayer;
        }

        _currentLayer = new GlyphLayer(new Rgba32(255, 255, 255, 255));
        _layers.Add(_currentLayer);
        return _currentLayer;
    }

    private GlyphContours EnsureContour()
        => _currentContour ?? throw new InvalidOperationException("The font renderer emitted a path command before MoveTo.");

    private static bool IsBidiFormatting(int codePoint)
        => UnicodeBidiData.Get(codePoint) is
            BidiClass.Bn or BidiClass.Lre or BidiClass.Rle or BidiClass.Lro or BidiClass.Rlo
            or BidiClass.Pdf or BidiClass.Lri or BidiClass.Rli or BidiClass.Fsi or BidiClass.Pdi;

    private static float2 TransformEllipse(
        float2 center,
        float radiusX,
        float radiusY,
        float cosPhi,
        float sinPhi,
        float cosAngle,
        float sinAngle)
        => new(
            center.x + cosPhi * radiusX * cosAngle - sinPhi * radiusY * sinAngle,
            center.y + sinPhi * radiusX * cosAngle + cosPhi * radiusY * sinAngle);

    private static float2 TransformEllipseTangent(
        float radiusX,
        float radiusY,
        float cosPhi,
        float sinPhi,
        float negativeSinAngle,
        float cosAngle)
        => new(
            cosPhi * radiusX * negativeSinAngle - sinPhi * radiusY * cosAngle,
            sinPhi * radiusX * negativeSinAngle + cosPhi * radiusY * cosAngle);

    private static Rgba32 ToColor(Paint? paint)
    {
        if (paint is not SolidPaint solid)
        {
            return new Rgba32(255, 255, 255, 255);
        }

        var color = solid.Color;
        var alpha = (byte)Math.Clamp((int)MathF.Round(color.A * solid.Opacity), 0, 255);
        return new Rgba32(color.R, color.G, color.B, alpha);
    }
}

internal sealed class CapturedGlyph
{
    internal CapturedGlyph(uint glyphId, int graphemeIndex, FontFamily family, FontRectangle bounds)
    {
        GlyphId = glyphId;
        GraphemeIndex = graphemeIndex;
        Family = family;
        Bounds = bounds;
    }

    internal uint GlyphId { get; }
    internal int GraphemeIndex { get; }
    internal FontFamily Family { get; }
    internal FontRectangle Bounds { get; }
    internal GlyphOutline? Outline { get; set; }
}

internal sealed class GlyphOutline
{
    internal GlyphOutline(uint glyphId, GlyphLayer[] layers)
    {
        GlyphId = glyphId;
        Layers = layers;
    }

    internal uint GlyphId { get; }
    internal GlyphLayer[] Layers { get; }

    internal void Translate(float x, float y)
    {
        for (var i = 0; i < Layers.Length; i++)
        {
            Layers[i].Contours.Translate(x, y);
        }
    }
}

internal sealed class GlyphLayer
{
    internal GlyphLayer(Rgba32 color)
    {
        Color = color;
        Contours = new GlyphContours();
    }

    internal Rgba32 Color { get; }
    internal GlyphContours Contours { get; }
}
