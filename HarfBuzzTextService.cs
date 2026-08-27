using Delta.Text.Contract;
using SkiaSharp;

namespace Delta.Text;

/// <summary>HarfBuzz-backed implementation of the canonical DeltaText service.</summary>
public sealed class HarfBuzzTextService : ITextService
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
    public FontMetrics GetFontMetrics(FontInstanceId font, float pixelsPerEm)
    {
        ValidatePixelsPerEm(pixelsPerEm);
        lock (_gate)
        {
            ThrowIfDisposed();
            ValidateOpenFont(font);
            var face = GetFont(font);
            var scale = pixelsPerEm / face.UnitsPerEm;
            return new FontMetrics(
                checked((uint)face.Metrics.UnitsPerEm),
                face.Metrics.Ascent * scale,
                face.Metrics.Descent * scale,
                face.Metrics.LineGap * scale,
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

            var runs = new List<ShapedRun>();
            foreach (var bidiRun in BidiResolver.Resolve(text, request.Direction))
            {
                var segments = SplitFallbackSegments(text, bidiRun.Start, bidiRun.Length, fallback);
                if ((bidiRun.Level & 1) != 0)
                {
                    segments.Reverse();
                }

                foreach (var segment in segments)
                {
                    var raw = new List<RawShapedGlyph>(segment.Length);
                    NativeHarfBuzz.Shape(
                        segment.Face.NativeFont,
                        text.Substring(segment.Start, segment.Length),
                        segment.Start,
                        bidiRun.Direction,
                        request.Features.Span,
                        raw);
                    runs.Add(BuildRun(segment, bidiRun.Direction, bidiRun.Level, request.PixelsPerEm, raw));
                }
            }

            return new ShapedText(text.Length, runs.ToArray());
        }
    }

    /// <inheritdoc />
    public GlyphImage GenerateGlyphImage(in GlyphImageRequest request)
    {
        ValidateGlyphImageRequest(request);
        lock (_gate)
        {
            ThrowIfDisposed();
            ValidateOpenFont(request.Font);
            var face = GetFont(request.Font);
            return request.Mode switch
            {
                GlyphImageMode.Coverage => RenderRaster(face, request, false),
                GlyphImageMode.Sdf => RenderRaster(face, request, true),
                GlyphImageMode.Msdf => RenderMsdf(face, request),
                GlyphImageMode.Color => RenderColor(face, request),
                _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown glyph image mode.")
            };
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
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
            ValidateOpenFont(ids[i]);
        }
    }

    private void ValidateOpenFont(FontInstanceId id)
    {
        if (!id.IsValid || !_fonts.ContainsKey(id))
        {
            throw new ArgumentException($"Font instance {id} is not open.", nameof(id));
        }
    }

    private static List<FallbackSegment> SplitFallbackSegments(string text, int rangeStart, int rangeLength, FontFace[] fallback)
    {
        var rangeEnd = checked(rangeStart + rangeLength);
        var segments = new List<FallbackSegment>();
        var start = rangeStart;
        var selected = SelectFont(text, rangeStart, fallback);
        for (var offset = rangeStart; offset < rangeEnd;)
        {
            var length = CodePointLength(text, offset);
            var current = SelectFont(text, offset, fallback);
            if (!ReferenceEquals(current, selected) && offset > start)
            {
                segments.Add(new FallbackSegment(start, offset - start, selected));
                start = offset;
                selected = current;
            }

            offset += length;
        }

        if (rangeEnd > start)
        {
            segments.Add(new FallbackSegment(start, rangeEnd - start, selected));
        }

        return segments;
    }

    private static FontFace SelectFont(string text, int offset, FontFace[] fallback)
    {
        var codepoint = ReadCodePoint(text, offset);
        foreach (var face in fallback)
        {
            if (face.GetGlyphId((uint)codepoint) != 0)
            {
                return face;
            }
        }

        return fallback[0];
    }

    private ShapedRun BuildRun(FallbackSegment segment, TextDirection direction, int bidiLevel, float pixelsPerEm, List<RawShapedGlyph> raw)
    {
        var scale = pixelsPerEm / segment.Face.UnitsPerEm;
        var glyphs = new ShapedGlyph[raw.Count];
        var advanceX = 0f;
        var advanceY = 0f;
        var left = 0f;
        var top = 0f;
        var right = 0f;
        var bottom = 0f;
        var hasBounds = false;
        for (var i = 0; i < raw.Count; i++)
        {
            var item = raw[i];
            var glyph = new ShapedGlyph(
                item.GlyphId,
                item.ClusterUtf16,
                item.AdvanceX * scale,
                item.AdvanceY * scale,
                item.OffsetX * scale,
                item.OffsetY * scale,
                item.Safety);
            glyphs[i] = glyph;

            var metrics = segment.Face.GetGlyphMetrics(item.GlyphId);
            var glyphLeft = advanceX + (metrics.BearingX + item.OffsetX) * scale;
            var glyphTop = -(metrics.BearingY + item.OffsetY) * scale;
            var glyphRight = glyphLeft + Math.Abs(metrics.Width) * scale;
            var glyphBottom = glyphTop + Math.Abs(metrics.Height) * scale;
            if (!hasBounds)
            {
                left = glyphLeft;
                top = glyphTop;
                right = glyphRight;
                bottom = glyphBottom;
                hasBounds = true;
            }
            else
            {
                left = Math.Min(left, glyphLeft);
                top = Math.Min(top, glyphTop);
                right = Math.Max(right, glyphRight);
                bottom = Math.Max(bottom, glyphBottom);
            }

            advanceX += glyph.AdvanceX;
            advanceY += glyph.AdvanceY;
        }

        if (hasBounds)
        {
            top = Math.Min(top, -segment.Face.Metrics.Ascent * scale);
            bottom = Math.Max(bottom, segment.Face.Metrics.Descent * scale);
        }

        return new ShapedRun(
            new TextRange(segment.Start, segment.Length),
            FindFontId(segment.Face),
            direction,
            checked((byte)bidiLevel),
            pixelsPerEm,
            advanceX,
            advanceY,
            new TextBounds(left, top, Math.Max(right, advanceX), bottom),
            glyphs);
    }

    private FontInstanceId FindFontId(FontFace face)
    {
        foreach (var pair in _fonts)
        {
            if (ReferenceEquals(pair.Value, face))
            {
                return pair.Key;
            }
        }

        throw new InvalidOperationException("The shaped font instance is no longer open.");
    }

    private static GlyphImage RenderRaster(FontFace face, GlyphImageRequest request, bool signedDistance)
    {
        var typeface = face.CreateTypeface();
        try
        {
            using var font = new SKFont(typeface, request.PixelsPerEm)
            {
                Edging = SKFontEdging.SubpixelAntialias,
                Hinting = SKFontHinting.Slight,
                LinearMetrics = true,
                Subpixel = true
            };
            using var path = font.GetGlyphPath(checked((ushort)request.GlyphId));
            if (path is null || path.IsEmpty)
            {
                return EmptyImage(request,
                    signedDistance ? GlyphImageEncoding.SdfR8 : GlyphImageEncoding.CoverageR8,
                    signedDistance ? request.DistanceRange : 0);
            }

            var bounds = path.Bounds;
            var padding = signedDistance ? (int)MathF.Ceiling(request.DistanceRange) : 0;
            var width = Math.Max(1, (int)MathF.Ceiling(bounds.Width)) + padding * 2;
            var height = Math.Max(1, (int)MathF.Ceiling(bounds.Height)) + padding * 2;
            using var bitmap = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Black);
                canvas.Translate(-bounds.Left + padding, -bounds.Top + padding);
                using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };
                canvas.DrawPath(path, paint);
                canvas.Flush();
            }

            var pixels = signedDistance
                ? BuildSignedDistanceField(bitmap.GetPixelSpan(), width, height, request.DistanceRange)
                : bitmap.GetPixelSpan().ToArray();
            var planeBounds = new TextBounds(
                bounds.Left - padding,
                bounds.Top - padding,
                bounds.Right + padding,
                bounds.Bottom + padding);
            return new GlyphImage(request.Font, request.GlyphId, request.PixelsPerEm,
                signedDistance ? GlyphImageEncoding.SdfR8 : GlyphImageEncoding.CoverageR8,
                signedDistance ? request.DistanceRange : 0, width, height, planeBounds, pixels);
        }
        finally
        {
            typeface.Dispose();
        }
    }

    private static GlyphImage RenderColor(FontFace face, GlyphImageRequest request)
    {
        var typeface = face.CreateTypeface();
        var paths = new List<ColorPath>();
        try
        {
            using var font = new SKFont(typeface, request.PixelsPerEm)
            {
                Edging = SKFontEdging.Antialias,
                Hinting = SKFontHinting.Slight,
                LinearMetrics = true,
                Subpixel = true
            };

            var layers = ColorFont.GetLayers(face.FontData, request.GlyphId, request.Color);
            if (layers.Length == 0)
            {
                layers = [new ColorGlyphLayer(checked((ushort)request.GlyphId), GetForeground(request.Color))];
            }

            var left = float.MaxValue;
            var top = float.MaxValue;
            var right = float.MinValue;
            var bottom = float.MinValue;
            foreach (var layer in layers)
            {
                var path = font.GetGlyphPath(layer.GlyphId);
                if (path is null || path.IsEmpty)
                {
                    path?.Dispose();
                    continue;
                }

                var bounds = path.Bounds;
                left = Math.Min(left, bounds.Left);
                top = Math.Min(top, bounds.Top);
                right = Math.Max(right, bounds.Right);
                bottom = Math.Max(bottom, bounds.Bottom);
                try
                {
                    paths.Add(new ColorPath(path, ToSkColor(layer.Color)));
                }
                catch
                {
                    path.Dispose();
                    throw;
                }
            }

            if (paths.Count == 0)
            {
                return EmptyImage(request, GlyphImageEncoding.ColorRgba8PremultipliedSrgb, 0);
            }

            var width = Math.Max(1, (int)MathF.Ceiling(right - left));
            var height = Math.Max(1, (int)MathF.Ceiling(bottom - top));
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Translate(-left, -top);
                using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
                foreach (var colorPath in paths)
                {
                    paint.Color = colorPath.Color;
                    canvas.DrawPath(colorPath.Path, paint);
                }

                canvas.Flush();
            }

            return new GlyphImage(request.Font, request.GlyphId, request.PixelsPerEm,
                GlyphImageEncoding.ColorRgba8PremultipliedSrgb, 0, width, height,
                new TextBounds(left, top, right, bottom), bitmap.GetPixelSpan().ToArray());
        }
        finally
        {
            foreach (var colorPath in paths)
            {
                colorPath.Path.Dispose();
            }

            typeface.Dispose();
        }
    }

    private static Rgba32 GetForeground(ColorGlyphOptions? options)
        => options?.Foreground ?? new Rgba32(255, 255, 255, 255);

    private static SKColor ToSkColor(Rgba32 color)
        => new(color.Red, color.Green, color.Blue, color.Alpha);

    private readonly record struct ColorPath(SKPath Path, SKColor Color);

    private static GlyphImage RenderMsdf(FontFace face, GlyphImageRequest request)
    {
        var contours = new GlyphContours();
        if (!NativeHarfBuzzOutline.TryRead(face.NativeFont, request.GlyphId, contours))
        {
            return EmptyImage(request, GlyphImageEncoding.MsdfRgb8, request.DistanceRange);
        }

        var padding = checked((int)MathF.Ceiling(request.DistanceRange));
        if (!NativeMsdf.TryGenerate(contours, checked((int)MathF.Ceiling(request.PixelsPerEm)), face.UnitsPerEm,
                padding, request.DistanceRange, out var width, out var height, out var pixels))
        {
            throw new InvalidOperationException("The native msdfgen backend could not generate the glyph image.");
        }

        var metrics = face.GetGlyphMetrics(request.GlyphId);
        var scale = request.PixelsPerEm / face.UnitsPerEm;
        var left = metrics.BearingX * scale - padding;
        var top = -metrics.BearingY * scale - padding;
        return new GlyphImage(request.Font, request.GlyphId, request.PixelsPerEm,
            GlyphImageEncoding.MsdfRgb8, request.DistanceRange, width, height,
            new TextBounds(left, top, left + width, top + height), pixels);
    }

    private static GlyphImage EmptyImage(in GlyphImageRequest request, GlyphImageEncoding encoding, float distanceRange)
        => new(request.Font, request.GlyphId, request.PixelsPerEm,
            encoding, distanceRange, 0, 0, default, Array.Empty<byte>());

    private static byte[] BuildSignedDistanceField(ReadOnlySpan<byte> source, int width, int height, float distanceRange)
    {
        var output = new byte[checked(width * height)];
        var range = Math.Max(1e-4f, distanceRange);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = source[y * width + x] > 127;
                var best = float.MaxValue;
                for (var yy = 0; yy < height; yy++)
                {
                    for (var xx = 0; xx < width; xx++)
                    {
                        if ((source[yy * width + xx] > 127) == inside)
                        {
                            continue;
                        }

                        var dx = xx - x;
                        var dy = yy - y;
                        best = Math.Min(best, MathF.Sqrt(dx * dx + dy * dy));
                    }
                }

                if (best == float.MaxValue)
                {
                    best = range;
                }

                var normalized = 0.5f + (inside ? best : -best) / (2f * range);
                output[y * width + x] = (byte)Math.Clamp((int)MathF.Round(normalized * 255f), 0, 255);
            }
        }

        return output;
    }

    private FontFace GetFont(FontInstanceId id)
        => _fonts.TryGetValue(id, out var face)
            ? face
            : throw new InvalidOperationException($"Font instance {id} is not open.");

    private static int CodePointLength(string text, int offset)
        => offset + 1 < text.Length && char.IsHighSurrogate(text[offset]) && char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;

    private static int ReadCodePoint(string text, int offset)
    {
        return CodePointLength(text, offset) == 2
            ? char.ConvertToUtf32(text[offset], text[offset + 1])
            : text[offset];
    }

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

        ValidateRange(request.Text.Length, request.Features.Span);
        if (request.Language is not null && string.IsNullOrWhiteSpace(request.Language))
        {
            throw new ArgumentException("Language must be null or a non-empty BCP 47 tag.", nameof(request));
        }
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
        if (request.Mode == GlyphImageMode.Unknown)
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

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private readonly record struct FallbackSegment(int Start, int Length, FontFace Face);
}
