namespace Delta.Text;

internal enum ContourPointKind : byte { Line = 0, QuadraticControl = 1, CubicControl = 2, CubicEnd = 3 }

internal readonly record struct ContourPoint(float X, float Y, ContourPointKind Kind);

internal sealed class GlyphContours
{
    private readonly List<List<ContourPoint>> _contours = new();
    private float _currentX;
    private float _currentY;
    private bool _hasCurrentPoint;
    public IReadOnlyList<IReadOnlyList<ContourPoint>> Contours => _contours;
    public float AdvanceX { get; set; }
    public float AdvanceY { get; set; }

    public void BeginContour(float x, float y)
    {
        var contour = new List<ContourPoint>(8) { new(x, y, ContourPointKind.Line) };
        _contours.Add(contour);
        _currentX = x;
        _currentY = y;
        _hasCurrentPoint = true;
    }

    public void LineTo(float x, float y)
    {
        Current().Add(new ContourPoint(x, y, ContourPointKind.Line));
        SetCurrentPoint(x, y);
    }

    public void QuadraticTo(float cx, float cy, float x, float y)
    {
        var contour = Current();
        contour.Add(new ContourPoint(cx, cy, ContourPointKind.QuadraticControl));
        contour.Add(new ContourPoint(x, y, ContourPointKind.Line));
        SetCurrentPoint(x, y);
    }

    public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
    {
        var contour = Current();
        contour.Add(new ContourPoint(c1x, c1y, ContourPointKind.CubicControl));
        contour.Add(new ContourPoint(c2x, c2y, ContourPointKind.CubicControl));
        contour.Add(new ContourPoint(x, y, ContourPointKind.CubicEnd));
        SetCurrentPoint(x, y);
    }

    internal bool TryGetCurrentPoint(out float x, out float y)
    {
        x = _currentX;
        y = _currentY;
        return _hasCurrentPoint;
    }

    internal void Translate(float x, float y)
    {
        for (var contourIndex = 0; contourIndex < _contours.Count; contourIndex++)
        {
            var contour = _contours[contourIndex];
            for (var pointIndex = 0; pointIndex < contour.Count; pointIndex++)
            {
                var point = contour[pointIndex];
                contour[pointIndex] = point with { X = point.X + x, Y = point.Y + y };
            }
        }

        if (_hasCurrentPoint)
        {
            _currentX += x;
            _currentY += y;
        }
    }

    private List<ContourPoint> Current() => _contours.Count == 0 ? throw new InvalidOperationException("Contour not started.") : _contours[^1];

    private void SetCurrentPoint(float x, float y)
    {
        _currentX = x;
        _currentY = y;
        _hasCurrentPoint = true;
    }
}
