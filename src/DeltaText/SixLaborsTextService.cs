using Delta.Text.Contract;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixTag = SixLabors.Fonts.Tables.AdvancedTypographic.Tag;
using SixTextDirection = SixLabors.Fonts.TextDirection;
using ContractFontMetrics = Delta.Text.Contract.FontMetrics;
using ContractShapedGlyph = Delta.Text.Contract.ShapedGlyph;
using ContractTextDirection = Delta.Text.Contract.TextDirection;
using System.Globalization;

namespace Delta.Text;

/// <summary>SixLabors.Fonts-backed implementation of the canonical DeltaText service.</summary>
public class SixLaborsTextService : ITextService
{
    private readonly object _gate = new();
    private readonly Dictionary<FontInstanceId, FontFace> _fonts = new();
    private ulong _nextFontValue = 1;
    private int _disposed;

    /// <inheritdoc />
    public FontInstanceId OpenFont(in FontOpenRequest request)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            ValidateOpenRequest(request);
            var face = FontFace.FromRequest(request);
            var id = new FontInstanceId(_nextFontValue++, 1);
            _fonts.Add(id, face);
            return id;
        }
    }

    /// <inheritdoc />
    public void CloseFont(FontInstanceId font)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!font.IsValid)
            {
                throw new ArgumentException($"Font instance {font} is not valid.", nameof(font));
            }

            if (!_fonts.Remove(font, out var face))
            {
                throw new ArgumentException($"Font instance {font} is not open.", nameof(font));
            }

            face.Dispose();
        }
    }

    /// <inheritdoc />
    public ContractFontMetrics GetFontMetrics(FontInstanceId font, float pixelsPerEm)
    {
        ValidatePixelsPerEm(pixelsPerEm);
        lock (_gate)
        {
            ThrowIfDisposed();
            var face = GetFont(font);
            var scale = pixelsPerEm / face.UnitsPerEm;
            var horizontal = face.Metrics.HorizontalMetrics;
            return new ContractFontMetrics(
                face.Metrics.UnitsPerEm,
                horizontal.Ascender * scale,
                Math.Max(0, -horizontal.Descender) * scale,
                horizontal.LineGap * scale,
                face.Metrics.UnderlinePosition * scale,
                face.Metrics.UnderlineThickness * scale);
        }
    }

    /// <inheritdoc />
    public ShapedText Shape(in TextShapeRequest request)
    {
        ValidateShapeRequest(request);
        lock (_gate)
        {
            ThrowIfDisposed();
            ValidateFallback(request.FontFallback.Span);
            var fallback = ResolveFallback(request.FontFallback.Span);
            var text = request.Text.ToString();
            if (text.Length == 0)
            {
                return new ShapedText(0, Array.Empty<ShapedRun>());
            }

            var primary = fallback[0].CreateFont(request.PixelsPerEm);
            var options = CreateTextOptions(primary, fallback, request);
            var metrics = FilterFormattingMetrics(TextMeasurer.GetGlyphMetrics(text, options));
            var renderer = new SixLaborsGlyphRenderer();
            new TextRenderer(renderer).Render(text, options);
            if (metrics.Length != renderer.Glyphs.Count)
            {
                throw new InvalidOperationException(
                    $"SixLabors returned inconsistent glyph layout and renderer output ({metrics.Length} metrics, {renderer.Glyphs.Count} glyphs).");
            }

            var bidiRuns = BidiResolver.Resolve(text, request.Direction);
            var result = new List<ShapedRun>(bidiRuns.Length);
            var current = new RunBuilder();
            for (var i = 0; i < metrics.Length; i++)
            {
                var metric = metrics[i];
                var captured = renderer.Glyphs[i];
                var faceId = FindFontId(metric.Font.Family);
                var bidi = FindBidiRun(bidiRuns, metric.StringIndex);
                if (current.HasGlyphs && (current.Font != faceId || current.BidiLevel != bidi.Level
                        || current.Direction != bidi.Direction))
                {
                    result.Add(current.Build());
                    current = new RunBuilder();
                }

                if (!current.HasGlyphs)
                {
                    current.Start(bidi, faceId, request.PixelsPerEm, metric.Advance.X, metric.Advance.Y);
                }

                var advanceX = IsVertical(bidi.Direction) ? 0 : metric.Advance.Height;
                var advanceY = IsVertical(bidi.Direction) ? metric.Advance.Height : 0;
                if (!IsVertical(bidi.Direction))
                {
                    advanceX = metric.Advance.Width;
                }

                var glyphFace = GetFont(faceId);
                var glyphFont = glyphFace.CreateFont(request.PixelsPerEm);
                var offsetX = metric.Bounds.X - metric.Advance.X;
                if (glyphFace.TryGetLeftSideBearing(glyphFont, metric.CodePoint, out var leftBearing))
                {
                    offsetX -= leftBearing * request.PixelsPerEm / glyphFace.UnitsPerEm;
                }

                if (captured.Outline is null)
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
                        ResolveGlyphSafety(text, metrics, i)),
                    metric.Bounds,
                    advanceX,
                    advanceY,
                    baselineOffset);

                if (captured.Outline is not null)
                {
                    captured.Outline.Translate(
                        -metric.Advance.X,
                        -metric.Advance.Y - baselineOffset);
                    var face = GetFont(faceId);
                    face.CacheOutline(request.PixelsPerEm, captured.GlyphId, ColorFontSupport.None, captured.Outline);
                }
            }

            if (current.HasGlyphs)
            {
                result.Add(current.Build());
            }

            return new ShapedText(text.Length, result.ToArray());
        }
    }

    /// <inheritdoc />
    public GlyphImage GenerateGlyphImage(in GlyphImageRequest request)
    {
        ValidateGlyphImageRequest(request);
        lock (_gate)
        {
            ThrowIfDisposed();
            var face = GetFont(request.Font);
            if (request.Mode == GlyphImageMode.Color && request.Color is { PaletteIndex: not 0 })
            {
                throw new NotSupportedException(
                    "The DeltaText SixLabors.Fonts fork build exposes the default color palette only.");
            }

            var colorSupport = request.Mode == GlyphImageMode.Color
                ? ColorFontSupport.ColrV0 | ColorFontSupport.ColrV1 | ColorFontSupport.Svg
                : ColorFontSupport.None;
            GlyphOutline? outline = null;
            var hasOutline = false;
            try
            {
                hasOutline = face.TryCreateOutline(request.PixelsPerEm, request.GlyphId, colorSupport, out outline);
            }
            catch (Exception exception) when (
                request.Mode == GlyphImageMode.Color
                && exception is InvalidOperationException or NotSupportedException)
            {
                hasOutline = false;
            }

            if (!hasOutline && request.Mode == GlyphImageMode.Color)
            {
                hasOutline = face.TryCreateOutline(
                    request.PixelsPerEm,
                    request.GlyphId,
                    ColorFontSupport.None,
                    out outline);
            }

            if (!hasOutline || outline is null)
            {
                return EmptyImage(request);
            }

            return ManagedGlyphRasterizer.Render(
                request.Font,
                request.GlyphId,
                request.PixelsPerEm,
                request.Mode,
                request.DistanceRange,
                outline,
                request.Color?.Foreground ?? new Rgba32(255, 255, 255, 255));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the service-owned font instances.</summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            foreach (var face in _fonts.Values)
            {
                face.Dispose();
            }

            _fonts.Clear();
        }
    }

    private static TextOptions CreateTextOptions(
        SixLabors.Fonts.Font primary,
        FontFace[] fallback,
        in TextShapeRequest request)
    {
        var kerningMode = KerningMode.Standard;
        var tags = request.Features.Length == 0
            ? null
            : new List<SixTag>(request.Features.Length);
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
                    tags?.Add(new SixTag(feature.Tag.Value));
                }
            }
        }

        // INCOMPLETE / OBSOLETE-CANDIDATE: the DeltaText SixLabors.Fonts fork build
        // adapter currently passes only global Boolean feature tags here. Keep
        // rejecting ranged, valued and language/script-specific requests until
        // the adapter can preserve their semantics instead of silently dropping
        // them.
        var options = new TextOptions(primary)
        {
            Dpi = 72,
            TextDirection = ToSixDirection(request.Direction),
            TextBidiMode = TextBidiMode.Normal,
            KerningMode = kerningMode,
            ColorFontSupport = ColorFontSupport.None
        };

        if (fallback.Length > 1)
        {
            var families = new FontFamily[fallback.Length - 1];
            for (var i = 1; i < fallback.Length; i++)
            {
                families[i - 1] = fallback[i].Family;
            }

            options.FallbackFontFamilies = families;
        }

        if (tags is not null)
        {
            options.FeatureTags = tags;
        }

        return options;
    }

    private FontFace[] ResolveFallback(ReadOnlySpan<FontInstanceId> ids)
    {
        var result = new FontFace[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            result[i] = GetFont(ids[i]);
        }

        return result;
    }

    private void ValidateFallback(ReadOnlySpan<FontInstanceId> ids)
    {
        for (var i = 0; i < ids.Length; i++)
        {
            if (!ids[i].IsValid || !_fonts.ContainsKey(ids[i]))
            {
                throw new ArgumentException($"Font instance {ids[i]} is not open.", nameof(ids));
            }
        }
    }

    private FontInstanceId FindFontId(FontFamily family)
    {
        foreach (var pair in _fonts)
        {
            if (pair.Value.Family.Equals(family))
            {
                return pair.Key;
            }
        }

        throw new InvalidOperationException("SixLabors returned a glyph from an unknown fallback font.");
    }

    private FontFace GetFont(FontInstanceId id)
        => _fonts.TryGetValue(id, out var face)
            ? face
            : throw new ArgumentException($"Font instance {id} is not open.", nameof(id));

    private static BidiRun FindBidiRun(BidiRun[] runs, int cluster)
    {
        for (var i = 0; i < runs.Length; i++)
        {
            if (cluster >= runs[i].Start && cluster < runs[i].Start + runs[i].Length)
            {
                return runs[i];
            }
        }

        return runs.Length == 0 ? new BidiRun(0, 0, 0, ContractTextDirection.LeftToRight) : runs[^1];
    }

    private static bool IsVertical(ContractTextDirection direction)
        => direction is ContractTextDirection.TopToBottom or ContractTextDirection.BottomToTop;

    private static SixLabors.Fonts.GlyphMetrics[] FilterFormattingMetrics(
        ReadOnlyMemory<SixLabors.Fonts.GlyphMetrics> metrics)
    {
        var filtered = new SixLabors.Fonts.GlyphMetrics[metrics.Length];
        var count = 0;
        for (var i = 0; i < metrics.Length; i++)
        {
            var metric = metrics.Span[i];
            if (!IsBidiFormatting(metric.CodePoint.Value))
            {
                filtered[count++] = metric;
            }
        }

        if (count == filtered.Length)
        {
            return filtered;
        }

        Array.Resize(ref filtered, count);
        return filtered;
    }

    private static bool IsBidiFormatting(int codePoint)
        => UnicodeBidiData.Get(codePoint) is
            BidiClass.Bn or BidiClass.Lre or BidiClass.Rle or BidiClass.Lro or BidiClass.Rlo
            or BidiClass.Pdf or BidiClass.Lri or BidiClass.Rli or BidiClass.Fsi or BidiClass.Pdi;

    private static GlyphSafety ResolveGlyphSafety(
        string text,
        SixLabors.Fonts.GlyphMetrics[] metrics,
        int index)
    {
        var start = metrics[index].StringIndex;
        var end = text.Length;
        var sharesCluster = false;
        for (var i = 0; i < metrics.Length; i++)
        {
            if (i == index)
            {
                continue;
            }

            var next = metrics[i].StringIndex;
            if (next == start)
            {
                sharesCluster = true;
            }
            else if (next > start)
            {
                end = Math.Min(end, next);
            }
        }

        var source = text.AsSpan(start, end - start);
        var requiresReshaping = sharesCluster
            || CountSourceScalars(source) > 1
            || ContainsCombiningMark(text, start, end)
            || ContainsArabicJoiningContext(text, start, end);
        return requiresReshaping
            ? GlyphSafety.UnsafeToBreak | GlyphSafety.UnsafeToConcat
            : GlyphSafety.None;
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

    private static GlyphImage EmptyImage(in GlyphImageRequest request)
        => new(
            request.Font,
            request.GlyphId,
            request.PixelsPerEm,
            request.Mode switch
            {
                GlyphImageMode.Coverage => GlyphImageEncoding.CoverageR8,
                GlyphImageMode.Sdf => GlyphImageEncoding.SdfR8,
                GlyphImageMode.Msdf => GlyphImageEncoding.MsdfRgb8,
                GlyphImageMode.Color => GlyphImageEncoding.ColorRgba8PremultipliedSrgb,
                _ => GlyphImageEncoding.Unknown
            },
            request.Mode is GlyphImageMode.Sdf or GlyphImageMode.Msdf ? request.DistanceRange : 0,
            0,
            0,
            default,
            Array.Empty<byte>());

    private static void ValidateOpenRequest(in FontOpenRequest request)
    {
        if (!request.Source.IsValid)
        {
            throw new ArgumentException("Font source identity must contain a non-empty Guid.", nameof(request));
        }

        if (request.Data.IsEmpty)
        {
            throw new ArgumentException("Font data cannot be empty.", nameof(request));
        }

        foreach (var variation in request.Variations.Span)
        {
            if (variation.Axis.IsAuto || !float.IsFinite(variation.Value))
            {
                throw new ArgumentException("Font variation axes must be explicit and finite.", nameof(request));
            }
        }
    }

    private static void ValidateShapeRequest(in TextShapeRequest request)
    {
        ValidatePixelsPerEm(request.PixelsPerEm);
        if (!Enum.IsDefined(request.Direction))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Text direction is not supported.");
        }

        if (request.FontFallback.IsEmpty)
        {
            throw new ArgumentException("At least one font fallback instance is required.", nameof(request));
        }

        if (request.Text.Span.IndexOfAny('\uFFFE', '\uFFFF') >= 0)
        {
            throw new ArgumentException("Text contains noncharacters that cannot be shaped.", nameof(request));
        }

        for (var i = 0; i < request.Text.Length; i++)
        {
            if (char.IsHighSurrogate(request.Text.Span[i]))
            {
                if (i + 1 >= request.Text.Length || !char.IsLowSurrogate(request.Text.Span[i + 1]))
                {
                    throw new ArgumentException("Text contains an unpaired high surrogate.", nameof(request));
                }

                i++;
            }
            else if (char.IsLowSurrogate(request.Text.Span[i]))
            {
                throw new ArgumentException("Text contains an unpaired low surrogate.", nameof(request));
            }
        }

        if (request.Language is not null && string.IsNullOrWhiteSpace(request.Language))
        {
            throw new ArgumentException("Language must be null or a non-empty BCP 47 tag.", nameof(request));
        }

        ValidateRange(request.Text.Length, request.Features.Span);
        ValidateBackendShapingOptions(request);
    }

    private static void ValidateGlyphImageRequest(in GlyphImageRequest request)
    {
        if (!request.Font.IsValid)
        {
            throw new ArgumentException("A valid font instance is required.", nameof(request));
        }

        ValidatePixelsPerEm(request.PixelsPerEm);
        if (request.GlyphId > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Glyph ID is outside the supported font range.");
        }

        if (!Enum.IsDefined(request.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Glyph image mode must be specified.");
        }

        if (request.Mode is GlyphImageMode.Sdf or GlyphImageMode.Msdf
            && (!float.IsFinite(request.DistanceRange) || request.DistanceRange <= 0 || request.DistanceRange > 4096))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Distance range must be finite and greater than zero.");
        }
    }

    private static void ValidatePixelsPerEm(float pixelsPerEm)
    {
        if (!float.IsFinite(pixelsPerEm) || pixelsPerEm <= 0 || pixelsPerEm > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerEm));
        }
    }

    private static void ValidateRange(int textLength, ReadOnlySpan<OpenTypeFeature> features)
    {
        foreach (var feature in features)
        {
            if (feature.Tag.IsAuto)
            {
                throw new ArgumentException("OpenType feature tags must be explicit.", nameof(features));
            }

            if (feature.Range is not { } range)
            {
                continue;
            }

            var end = (long)range.StartUtf16 + range.LengthUtf16;
            if (range.StartUtf16 < 0 || range.LengthUtf16 < 0 || end > textLength)
            {
                throw new ArgumentException("OpenType feature ranges must fit the UTF-16 input.", nameof(features));
            }
        }
    }

    private static void ValidateBackendShapingOptions(in TextShapeRequest request)
    {
        if (!request.Script.IsAuto)
        {
            throw new NotSupportedException(
                "The DeltaText SixLabors.Fonts fork build currently uses automatic script inference.");
        }

        if (request.Language is not null)
        {
            throw new NotSupportedException(
                "The DeltaText SixLabors.Fonts fork build currently uses automatic language inference.");
        }

        foreach (var feature in request.Features.Span)
        {
            if (feature.Range is not null)
            {
                throw new NotSupportedException(
                "The DeltaText SixLabors.Fonts fork build supports feature tags only for the complete text span.");
            }

            if (feature.Value > 1)
            {
                throw new NotSupportedException(
                "The DeltaText SixLabors.Fonts fork build supports only Boolean OpenType feature values.");
            }

            if (feature.Value == 0 && feature.Tag.Value != KernTag)
            {
                throw new NotSupportedException(
                "The DeltaText SixLabors.Fonts fork build cannot disable an arbitrary default OpenType feature.");
            }
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private const uint KernTag = 0x6B65726E;

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

        internal void Start(BidiRun bidi, FontInstanceId font, float pixelsPerEm, float originX, float originY)
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
