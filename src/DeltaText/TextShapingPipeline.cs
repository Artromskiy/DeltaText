using System.Globalization;
using System.Runtime.InteropServices;
using Delta.Text.Contract;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixTag = SixLabors.Fonts.Tables.AdvancedTypographic.Tag;
using SixTextDirection = SixLabors.Fonts.TextDirection;
using SixFont = SixLabors.Fonts.Font;
using ContractShapedGlyph = Delta.Text.Contract.ShapedGlyph;
using ContractTextDirection = Delta.Text.Contract.TextDirection;

namespace Delta.Text;

/// <summary>
/// Owns the mutable state used by one serialized shaping worker.
/// </summary>
/// <remarks>
/// The service keeps this worker behind its gate. Keeping the scratch state here
/// makes the font registry and glyph-image path independent from shaping details,
/// while preserving the existing single-worker SixLabors lifetime contract.
/// </remarks>
internal sealed class TextShapingPipeline
{
    private const uint KernTag = 0x6B65726E;

    private readonly Func<FontInstanceId, FontFace> _getFont;
    private readonly SixLaborsGlyphRenderer _glyphRenderer = new(captureOutlines: false);
    private readonly TextRenderer _textRenderer;
    private readonly List<FontFamily> _fallbackFamilies = new();
    private readonly List<SixTag> _featureTags = new();
    private readonly List<ShapedRun> _runScratch = new();
    private readonly RunBuilder _runBuilder = new();
    private readonly Dictionary<FontFamily, int> _fontIndices = new();
    private readonly Dictionary<int, int> _clusterIndices = new();
    private ResolvedFont[] _fallbackScratch = Array.Empty<ResolvedFont>();
    private SixLabors.Fonts.GlyphMetrics[] _filteredMetricsScratch = Array.Empty<SixLabors.Fonts.GlyphMetrics>();
    private int[] _bidiRunMap = Array.Empty<int>();
    private int[] _clusterStarts = Array.Empty<int>();
    private int[] _clusterCounts = Array.Empty<int>();
    private int[] _metricClusterIndices = Array.Empty<int>();
    private GlyphSafety[] _safetyByCluster = Array.Empty<GlyphSafety>();
    private GlyphSafety[] _glyphSafety = Array.Empty<GlyphSafety>();
    private TextOptions? _textOptions;
    private int _fallbackCount;

    internal TextShapingPipeline(Func<FontInstanceId, FontFace> getFont)
    {
        ArgumentNullException.ThrowIfNull(getFont);
        _getFont = getFont;
        _textRenderer = new TextRenderer(_glyphRenderer);
    }

    internal ShapedText Shape(in TextShapeRequest request)
    {
        var text = GetText(request.Text);
        var fallback = ResolveFallback(request.FontFallback.Span, request.PixelsPerEm);
        if (text.Length == 0)
        {
            return new ShapedText(0, Array.Empty<ShapedRun>());
        }

        var primary = fallback[0].Font;
        var options = CreateTextOptions(primary, fallback, _fallbackCount, request);
        var metrics = FilterFormattingMetrics(TextMeasurer.GetGlyphMetrics(request.Text.Span, options));
        _glyphRenderer.Reset();
        _textRenderer.Render(request.Text.Span, options);
        if (metrics.Length != _glyphRenderer.Glyphs.Count)
        {
            throw new InvalidOperationException(
                $"SixLabors returned inconsistent glyph layout and renderer output ({metrics.Length} metrics, {_glyphRenderer.Glyphs.Count} glyphs).");
        }

        var bidiRuns = BidiResolver.Resolve(text, request.Direction);
        var bidiRunMap = BuildBidiRunMap(bidiRuns, text.Length);
        var glyphSafety = ResolveGlyphSafety(text, metrics).Span;
        _runScratch.Clear();
        var result = _runScratch;
        var current = _runBuilder;
        current.Reset();
        var metricSpan = metrics.Span;
        for (var i = 0; i < metrics.Length; i++)
        {
            var metric = metricSpan[i];
            var captured = _glyphRenderer.Glyphs[i];
            var fontIndex = FindFontIndex(metric.Font.Family);
            var resolvedFont = fallback[fontIndex];
            var faceId = resolvedFont.Id;
            var bidi = FindBidiRun(bidiRuns, bidiRunMap, metric.StringIndex);
            if (current.HasGlyphs && (current.Font != faceId || current.BidiLevel != bidi.Level
                    || current.Direction != bidi.Direction))
            {
                result.Add(current.Build());
                current.Reset();
            }

            if (!current.HasGlyphs)
            {
                current.Start(bidi, faceId, request.PixelsPerEm, metric.Advance.X);
            }

            var advanceX = IsVertical(bidi.Direction) ? 0 : metric.Advance.Height;
            var advanceY = IsVertical(bidi.Direction) ? metric.Advance.Height : 0;
            if (!IsVertical(bidi.Direction))
            {
                advanceX = metric.Advance.Width;
            }

            var glyphFace = resolvedFont.Face;
            var offsetX = metric.Bounds.X - metric.Advance.X;
            if (glyphFace.TryGetLeftSideBearing(resolvedFont.Font, metric.CodePoint, out var leftBearing))
            {
                offsetX -= leftBearing * request.PixelsPerEm / glyphFace.UnitsPerEm;
            }

            if (!captured.HasOutline)
            {
                offsetX = 0;
            }

            var baselineOffset = glyphFace.GetBaselineOffset(request.PixelsPerEm);
            // SixLabors reports horizontal glyph bounds relative to the line origin.
            // Vertical positioning offsets are not exposed separately by its public layout API.
            var offsetY = 0f;
            current.Add(
                new ContractShapedGlyph(
                    captured.GlyphId,
                    metric.StringIndex,
                    advanceX,
                    advanceY,
                    offsetX,
                    offsetY,
                    glyphSafety[i]),
                metric.Bounds,
                advanceX,
                advanceY,
                baselineOffset);
        }

        if (current.HasGlyphs)
        {
            result.Add(current.Build());
        }

        var shapedRuns = result.ToArray();
        result.Clear();
        current.Reset();
        return new ShapedText(text.Length, shapedRuns);
    }

    private TextOptions CreateTextOptions(
        SixFont primary,
        ResolvedFont[] fallback,
        int fallbackCount,
        in TextShapeRequest request)
    {
        var kerningMode = KerningMode.Standard;
        _featureTags.Clear();
        if (request.Features.Length > 0)
        {
            foreach (var feature in request.Features.Span)
            {
                if (feature.Tag.Value == KernTag)
                {
                    kerningMode = feature.Value == 0 ? KerningMode.None : KerningMode.Standard;
                }
                else if (feature.Value == 1)
                {
                    _featureTags.Add(new SixTag(feature.Tag.Value));
                }
            }
        }

        // INCOMPLETE / OBSOLETE-CANDIDATE: the DeltaText SixLabors.Fonts fork build
        // adapter currently passes only global Boolean feature tags here. Keep
        // rejecting ranged, valued and language/script-specific requests until
        // the adapter can preserve their semantics instead of silently dropping
        // them.
        var options = _textOptions ??= new TextOptions(primary);
        options.Font = primary;
        options.Dpi = SixLaborsAdapterConstants.LayoutDpi;
        options.TextDirection = ToSixDirection(request.Direction);
        options.TextBidiMode = TextBidiMode.Normal;
        options.KerningMode = kerningMode;
        options.ColorFontSupport = ColorFontSupport.None;
        _fallbackFamilies.Clear();
        for (var i = 1; i < fallbackCount; i++)
        {
            _fallbackFamilies.Add(fallback[i].Face.Family);
        }

        options.FallbackFontFamilies = _fallbackFamilies;
        options.FeatureTags = _featureTags;
        return options;
    }

    private ResolvedFont[] ResolveFallback(ReadOnlySpan<FontInstanceId> ids, float pixelsPerEm)
    {
        if (_fallbackScratch.Length < ids.Length)
        {
            _fallbackScratch = new ResolvedFont[ids.Length];
        }

        var result = _fallbackScratch;
        _fallbackCount = ids.Length;
        _fontIndices.Clear();
        for (var i = 0; i < ids.Length; i++)
        {
            var face = _getFont(ids[i]);
            result[i] = new ResolvedFont(ids[i], face, face.GetOrCreateFont(pixelsPerEm));
            _fontIndices.TryAdd(face.Family, i);
        }

        return result;
    }

    private int FindFontIndex(FontFamily family)
        => _fontIndices.TryGetValue(family, out var index)
            ? index
            : throw new InvalidOperationException("SixLabors returned a glyph from an unknown fallback font.");

    private static BidiRun FindBidiRun(BidiRun[] runs, int[] runMap, int cluster)
    {
        if ((uint)cluster < (uint)runMap.Length)
        {
            var runIndex = runMap[cluster];
            if (runIndex >= 0)
            {
                return runs[runIndex];
            }
        }

        return runs.Length == 0 ? new BidiRun(0, 0, 0, ContractTextDirection.LeftToRight) : runs[^1];
    }

    private int[] BuildBidiRunMap(BidiRun[] runs, int textLength)
    {
        if (_bidiRunMap.Length < textLength)
        {
            _bidiRunMap = new int[textLength];
        }

        var map = _bidiRunMap;
        Array.Fill(map, -1, 0, textLength);
        for (var i = 0; i < runs.Length; i++)
        {
            var start = Math.Max(0, runs[i].Start);
            var end = Math.Min(textLength, checked(runs[i].Start + runs[i].Length));
            for (var index = start; index < end; index++)
            {
                if (map[index] < 0)
                {
                    map[index] = i;
                }
            }
        }

        return map;
    }

    private static bool IsVertical(ContractTextDirection direction)
        => direction is ContractTextDirection.TopToBottom or ContractTextDirection.BottomToTop;

    private ReadOnlyMemory<SixLabors.Fonts.GlyphMetrics> FilterFormattingMetrics(
        ReadOnlyMemory<SixLabors.Fonts.GlyphMetrics> metrics)
    {
        var span = metrics.Span;
        var formattingCount = 0;
        for (var i = 0; i < metrics.Length; i++)
        {
            if (IsBidiFormatting(span[i].CodePoint.Value))
            {
                formattingCount++;
            }
        }

        if (formattingCount == 0)
        {
            return metrics;
        }

        var filteredLength = metrics.Length - formattingCount;
        if (_filteredMetricsScratch.Length < filteredLength)
        {
            _filteredMetricsScratch = new SixLabors.Fonts.GlyphMetrics[filteredLength];
        }

        var filtered = _filteredMetricsScratch;
        var count = 0;
        for (var i = 0; i < span.Length; i++)
        {
            if (!IsBidiFormatting(span[i].CodePoint.Value))
            {
                filtered[count++] = span[i];
            }
        }

        return new ReadOnlyMemory<SixLabors.Fonts.GlyphMetrics>(filtered, 0, count);
    }

    private static bool IsBidiFormatting(int codePoint)
        => UnicodeBidiData.Get(codePoint) is
            BidiClass.Bn or BidiClass.Lre or BidiClass.Rle or BidiClass.Lro or BidiClass.Rlo
            or BidiClass.Pdf or BidiClass.Lri or BidiClass.Rli or BidiClass.Fsi or BidiClass.Pdi;

    private ReadOnlyMemory<GlyphSafety> ResolveGlyphSafety(
        string text,
        ReadOnlyMemory<SixLabors.Fonts.GlyphMetrics> metrics)
    {
        var metricSpan = metrics.Span;
        if (metricSpan.Length == 0)
        {
            return ReadOnlyMemory<GlyphSafety>.Empty;
        }

        EnsureSafetyScratch(metricSpan.Length);
        _clusterIndices.Clear();
        var clusterStarts = _clusterStarts;
        var clusterCounts = _clusterCounts;
        var clusterCount = 0;
        var metricClusterIndices = _metricClusterIndices;
        for (var i = 0; i < metricSpan.Length; i++)
        {
            var start = metricSpan[i].StringIndex;
            if (!_clusterIndices.TryGetValue(start, out var clusterIndex))
            {
                clusterIndex = clusterCount++;
                _clusterIndices.Add(start, clusterIndex);
                clusterStarts[clusterIndex] = start;
                clusterCounts[clusterIndex] = 0;
            }

            clusterCounts[clusterIndex]++;
            metricClusterIndices[i] = clusterIndex;
        }

        Array.Sort(clusterStarts, 0, clusterCount);
        var safetyByCluster = _safetyByCluster;
        for (var i = 0; i < clusterCount; i++)
        {
            var start = clusterStarts[i];
            var end = i + 1 < clusterCount ? clusterStarts[i + 1] : text.Length;
            var clusterIndex = _clusterIndices[start];
            var source = text.AsSpan(start, end - start);
            var requiresReshaping = clusterCounts[clusterIndex] > 1
                || CountSourceScalars(source) > 1
                || ContainsCombiningMark(text, start, end)
                || ContainsArabicJoiningContext(text, start, end);
            safetyByCluster[clusterIndex] = requiresReshaping
                ? GlyphSafety.UnsafeToBreak | GlyphSafety.UnsafeToConcat
                : GlyphSafety.None;
        }

        var result = _glyphSafety;
        for (var i = 0; i < metricSpan.Length; i++)
        {
            result[i] = safetyByCluster[metricClusterIndices[i]];
        }

        return new ReadOnlyMemory<GlyphSafety>(result, 0, metricSpan.Length);
    }

    private void EnsureSafetyScratch(int length)
    {
        if (_clusterStarts.Length < length)
        {
            _clusterStarts = new int[length];
            _clusterCounts = new int[length];
            _metricClusterIndices = new int[length];
            _safetyByCluster = new GlyphSafety[length];
            _glyphSafety = new GlyphSafety[length];
        }
    }

    private static int CountSourceScalars(ReadOnlySpan<char> source)
    {
        var count = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (!char.IsLowSurrogate(source[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static bool ContainsCombiningMark(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(text, i);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
            {
                return true;
            }

            if (char.IsHighSurrogate(text[i]))
            {
                i++;
            }
        }

        return false;
    }

    private static bool ContainsArabicJoiningContext(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var codePoint = char.ConvertToUtf32(text, i);
            if (UnicodeBidiData.Get(codePoint) == BidiClass.Al)
            {
                return true;
            }

            if (char.IsHighSurrogate(text[i]) && i + 1 < end)
            {
                i++;
            }
        }

        return false;
    }

    private static SixTextDirection ToSixDirection(ContractTextDirection direction)
        => direction switch
        {
            ContractTextDirection.LeftToRight => SixTextDirection.LeftToRight,
            ContractTextDirection.RightToLeft => SixTextDirection.RightToLeft,
            // INCOMPLETE / OBSOLETE-CANDIDATE: vertical requests currently
            // collapse to SixLabors Auto. Add real vertical layout mapping,
            // then remove this fallback so the contract cannot lose direction.
            _ => SixTextDirection.Auto
        };

    private static string GetText(ReadOnlyMemory<char> source)
    {
        if (MemoryMarshal.TryGetString(source, out var text, out var start, out var length)
            && start == 0
            && length == text.Length)
        {
            return text;
        }

        return source.ToString();
    }

    private readonly struct ResolvedFont
    {
        internal ResolvedFont(FontInstanceId id, FontFace face, SixFont font)
        {
            Id = id;
            Face = face;
            Font = font;
        }

        internal FontInstanceId Id { get; }
        internal FontFace Face { get; }
        internal SixFont Font { get; }
    }

    private sealed class RunBuilder
    {
        private readonly List<ContractShapedGlyph> _glyphs = new();
        private TextRange _sourceRange;
        private FontInstanceId _font;
        private ContractTextDirection _direction;
        private byte _bidiLevel;
        private float _pixelsPerEm;
        private float _advanceX;
        private float _advanceY;
        private float _left;
        private float _top;
        private float _right;
        private float _bottom;
        private float _originX;
        private bool _hasBounds;

        internal bool HasGlyphs => _glyphs.Count > 0;
        internal FontInstanceId Font => _font;
        internal ContractTextDirection Direction => _direction;
        internal byte BidiLevel => _bidiLevel;

        internal void Reset()
        {
            _glyphs.Clear();
            _sourceRange = default;
            _font = default;
            _direction = default;
            _bidiLevel = 0;
            _pixelsPerEm = 0;
            _advanceX = 0;
            _advanceY = 0;
            _left = 0;
            _top = 0;
            _right = 0;
            _bottom = 0;
            _originX = 0;
            _hasBounds = false;
        }

        internal void Start(BidiRun bidi, FontInstanceId font, float pixelsPerEm, float originX)
        {
            _sourceRange = new TextRange(bidi.Start, bidi.Length);
            _font = font;
            _direction = bidi.Direction;
            _bidiLevel = checked((byte)bidi.Level);
            _pixelsPerEm = pixelsPerEm;
            _originX = originX;
        }

        internal void Add(
            ContractShapedGlyph glyph,
            FontRectangle bounds,
            float advanceX,
            float advanceY,
            float baselineOffset)
        {
            _glyphs.Add(glyph);
            _advanceX += advanceX;
            _advanceY += advanceY;
            var left = bounds.X - _originX;
            var top = bounds.Y - baselineOffset;
            var right = bounds.X + bounds.Width - _originX;
            var bottom = bounds.Y + bounds.Height - baselineOffset;
            if (!_hasBounds)
            {
                _left = left;
                _top = top;
                _right = right;
                _bottom = bottom;
                _hasBounds = true;
            }
            else
            {
                _left = Math.Min(_left, left);
                _top = Math.Min(_top, top);
                _right = Math.Max(_right, right);
                _bottom = Math.Max(_bottom, bottom);
            }
        }

        internal ShapedRun Build()
            => new(
                _sourceRange,
                _font,
                _direction,
                _bidiLevel,
                _pixelsPerEm,
                _advanceX,
                _advanceY,
                new TextBounds(_left, _top, Math.Max(_right, _advanceX), _bottom),
                _glyphs.ToArray());
    }
}
