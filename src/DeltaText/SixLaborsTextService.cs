using Delta.Text.Contract;
using SixLabors.Fonts;
using ContractFontMetrics = Delta.Text.Contract.FontMetrics;

namespace Delta.Text;

/// <summary>SixLabors.Fonts-backed implementation of the canonical DeltaText service.</summary>
public class SixLaborsTextService : ITextService
{
    private readonly object _gate = new();
    private readonly Dictionary<FontInstanceId, FontFace> _fonts = new();
    private readonly TextShapingPipeline _shaping;
    private ulong _nextFontValue = 1;
    private int _disposed;

    /// <summary>Creates a thread-safe text service with implementation-owned scratch storage.</summary>
    public SixLaborsTextService()
    {
        _shaping = new TextShapingPipeline(GetFont);
    }

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
            return _shaping.Shape(request);
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

            var cacheKey = new GlyphImageCacheKey(
                request.GlyphId,
                request.PixelsPerEm,
                request.Mode,
                request.DistanceRange,
                request.Color);
            if (face.TryGetCachedGlyphImage(cacheKey, out var cachedImage))
            {
                return cachedImage;
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
                var empty = EmptyImage(request);
                face.CacheGlyphImage(cacheKey, empty);
                return empty;
            }

            var image = ManagedGlyphRasterizer.Render(
                request.Font,
                request.GlyphId,
                request.PixelsPerEm,
                request.Mode,
                request.DistanceRange,
                outline,
                request.Color?.Foreground ?? new Rgba32(255, 255, 255, 255));
            face.CacheGlyphImage(cacheKey, image);
            return image;
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

    private FontFace GetFont(FontInstanceId id)
        => _fonts.TryGetValue(id, out var face)
            ? face
            : throw new ArgumentException($"Font instance {id} is not open.", nameof(id));

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
}
