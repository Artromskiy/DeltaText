using SkiaSharp;

namespace Delta.Text;

/// <summary>Generates and caches grayscale and MSDF glyph atlases.</summary>
public sealed class GlyphAtlasGenerator : IGlyphAtlasGenerator, IGlyphBitmapGenerator
{
    private readonly BoundedCache<GlyphAtlasKey, CachedGlyph> _glyphCache;

    /// <summary>Creates a generator with a bounded glyph bitmap cache.</summary>
    public GlyphAtlasGenerator(TextCacheBudget? budget = null)
    {
        _glyphCache = new BoundedCache<GlyphAtlasKey, CachedGlyph>(budget ?? TextCacheBudget.Default);
    }

    /// <summary>Generates or retrieves a deterministic atlas.</summary>
    /// <param name="face">The loaded font face.</param>
    /// <param name="request">The atlas generation settings.</param>
    /// <returns>The generated atlas.</returns>
    public GlyphAtlasResult Generate(FontFace face, in GlyphAtlasRequest request)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (request.Mode == GlyphAtlasMode.Mtsdf)
        {
            throw new NotSupportedException("MTSDF is not enabled yet; use GlyphAtlasMode.Msdf.");
        }

        return GenerateCore(face, request);
    }

    /// <inheritdoc />
    public GlyphBitmapResult TryGenerateGlyph(FontFace face, in GlyphAtlasRequest request, uint glyphId)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (request.Mode == GlyphAtlasMode.Mtsdf)
        {
            return new GlyphBitmapResult(GlyphBitmapStatus.UnsupportedMode, null,
                "MTSDF is not enabled yet; use GlyphAtlasMode.Msdf.");
        }

        var bitmap = GetGlyph(face, request, glyphId);
        return new GlyphBitmapResult(GlyphBitmapStatus.Succeeded, bitmap.ToBitmap(request), null);
    }

    private GlyphAtlasResult GenerateCore(FontFace face, GlyphAtlasRequest request)
    {
        var glyphIds = request.GlyphIds.Span;
        var ordered = glyphIds.ToArray();
        Array.Sort(ordered);

        var cachedGlyphs = new CachedGlyph[ordered.Length];
        var totalArea = 0;
        var maxEdge = 0;
        for (var i = 0; i < ordered.Length; i++)
        {
            cachedGlyphs[i] = GetGlyph(face, request, ordered[i]);
            totalArea += Math.Max(1, cachedGlyphs[i].Width + request.Padding * 2) * Math.Max(1, cachedGlyphs[i].Height + request.Padding * 2);
            maxEdge = Math.Max(maxEdge, Math.Max(cachedGlyphs[i].Width, cachedGlyphs[i].Height));
        }

        var pageSize = NextPowerOfTwo(Math.Max(256, Math.Max(maxEdge + request.Padding * 2 + 4, (int)Math.Ceiling(Math.Sqrt(totalArea * 1.25)))));
        while (true)
        {
            var pages = PackPages(cachedGlyphs, pageSize, request.Padding, request.Mode == GlyphAtlasMode.Msdf ? 3 : 1);
            if (pages.Count > 0)
            {
                return BuildResult(request, pages);
            }

            pageSize *= 2;
        }
    }

    private CachedGlyph GetGlyph(FontFace face, GlyphAtlasRequest request, uint glyphId)
    {
        var key = new GlyphAtlasKey(face.Key, glyphId, request.PixelSize, request.Padding, request.DistanceRange, request.Mode);
        return _glyphCache.GetOrAdd(key, () => BuildGlyph(face, key), static glyph => glyph.Pixels.Length + 64L);
    }

    private CachedGlyph BuildGlyph(FontFace face, GlyphAtlasKey key)
    {
        if (key.Mode == GlyphAtlasMode.Msdf)
        {
            return BuildMsdfGlyph(face, key);
        }

        if (key.Mode == GlyphAtlasMode.Mtsdf)
        {
            throw new NotSupportedException("MTSDF is not enabled yet; use GlyphAtlasMode.Msdf.");
        }

        var typeface = face.CreateTypeface();
        try
        {
            using var font = new SKFont(typeface, key.PixelSize)
            {
                Edging = SKFontEdging.SubpixelAntialias,
                Hinting = SKFontHinting.Slight,
                LinearMetrics = true,
                Subpixel = true
            };
            return BuildGrayscaleGlyph(face, font, key);
        }
        finally
        {
            typeface.Dispose();
        }
    }

    private static CachedGlyph BuildGrayscaleGlyph(FontFace face, SKFont font, GlyphAtlasKey key)
    {
        using var path = font.GetGlyphPath(checked((ushort)key.GlyphId));
        if (path is null || path.IsEmpty)
        {
            var advance = face.GetGlyphMetrics(key.GlyphId).AdvanceX * key.PixelSize / (float)face.UnitsPerEm;
            return CachedGlyph.Empty(key.GlyphId, key.Mode, key.PixelSize, advance);
        }

        var bounds = path.Bounds;
        var padding = key.Padding;
        var distanceRange = key.DistanceRange;
        var scale = 1f;
        var glyphWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width)) + padding * 2 + 2;
        var glyphHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height)) + padding * 2 + 2;
        using var baseBitmap = new SKBitmap(glyphWidth, glyphHeight, SKColorType.Gray8, SKAlphaType.Opaque);
        baseBitmap.Erase(SKColors.Black);

        using (var canvas = new SKCanvas(baseBitmap))
        {
            canvas.Clear(SKColors.Black);
            canvas.Translate(-bounds.Left + padding + 1f, -bounds.Top + padding + 1f);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White
            };
            canvas.DrawPath(path, paint);
            canvas.Flush();
        }

        var pixels = key.Mode switch
        {
            GlyphAtlasMode.Grayscale => BuildSignedDistanceField(baseBitmap, distanceRange),
            GlyphAtlasMode.Msdf or GlyphAtlasMode.Mtsdf => throw new InvalidOperationException("Unreachable atlas mode."),
            _ => throw new NotSupportedException($"Unsupported atlas mode: {key.Mode}")
        };

        var metrics = face.GetGlyphMetrics(key.GlyphId);
        var bearingX = metrics.BearingX * scale;
        var bearingY = metrics.BearingY * scale;
        return CachedGlyph.Create(
            key.GlyphId,
            key.Mode,
            key.PixelSize,
            glyphWidth,
            glyphHeight,
            glyphWidth,
            bearingX,
            bearingY,
            metrics.AdvanceX * scale,
            pixels);
    }

    private static CachedGlyph BuildMsdfGlyph(FontFace face, GlyphAtlasKey key)
    {
        var contours = new GlyphContours();
        if (!NativeHarfBuzzOutline.TryRead(face.NativeFont, key.GlyphId, contours))
        {
            var advance = face.GetGlyphMetrics(key.GlyphId).AdvanceX * key.PixelSize / (float)face.UnitsPerEm;
            return CachedGlyph.Empty(key.GlyphId, key.Mode, key.PixelSize, advance);
        }

        if (!NativeMsdf.TryGenerate(contours, key.PixelSize, face.UnitsPerEm, key.Padding, key.DistanceRange, out var width, out var height, out var pixels))
        {
            throw new InvalidOperationException("The native msdfgen backend could not generate the glyph bitmap.");
        }

        var metrics = face.GetGlyphMetrics(key.GlyphId);
        var scale = key.PixelSize / (float)face.UnitsPerEm;
        return CachedGlyph.Create(key.GlyphId, key.Mode, key.PixelSize, width, height, width * 3,
            metrics.BearingX * scale, metrics.BearingY * scale, metrics.AdvanceX * scale, pixels);
    }

    private static ReadOnlyMemory<byte> CopyPixels(SKBitmap bitmap)
    {
        var span = bitmap.GetPixelSpan();
        var data = new byte[span.Length];
        span.CopyTo(data);
        return data;
    }

    private static ReadOnlyMemory<byte> BuildSignedDistanceField(SKBitmap bitmap, float distanceRange)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var source = bitmap.GetPixelSpan();
        var output = new byte[width * height];
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
                        var other = source[yy * width + xx] > 127;
                        if (other == inside)
                        {
                            continue;
                        }

                        var dx = xx - x;
                        var dy = yy - y;
                        var distance = MathF.Sqrt(dx * dx + dy * dy);
                        if (distance < best)
                        {
                            best = distance;
                        }
                    }
                }

                if (best == float.MaxValue)
                {
                    best = range;
                }

                var signed = inside ? best : -best;
                var value = 0.5f + signed / (2f * range);
                output[y * width + x] = (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
            }
        }

        return output;
    }

    private static List<PageResult> PackPages(CachedGlyph[] glyphs, int pageSize, int padding, int channels)
    {
        var pages = new List<PageResult>();
        var current = new PageBuild(pageSize, channels);
        foreach (var glyph in glyphs)
        {
            if (!current.TryPlace(glyph, padding))
            {
                pages.Add(current.FinalizePage(pages.Count));
                current = new PageBuild(pageSize, channels);
                if (!current.TryPlace(glyph, padding))
                {
                    return new List<PageResult>();
                }
            }
        }

        pages.Add(current.FinalizePage(pages.Count));
        return pages;
    }

    private static GlyphAtlasResult BuildResult(GlyphAtlasRequest request, List<PageResult> pages)
    {
        var pageArray = new GlyphAtlasPage[pages.Count];
        var glyphArray = new List<GlyphAtlasGlyph>();
        foreach (var page in pages)
        {
            pageArray[page.PageIndex] = page.Page;
            glyphArray.AddRange(page.Glyphs);
        }

        return new GlyphAtlasResult(request, pageArray, glyphArray.ToArray());
    }

    private static int NextPowerOfTwo(int value)
    {
        var v = 1;
        while (v < value)
        {
            v <<= 1;
        }

        return v;
    }

    private readonly record struct GlyphAtlasRequestKey(FontKey Font, string GlyphIds, int PixelSize, int Padding, int DistanceRangeBits, GlyphAtlasMode Mode)
    {
        public GlyphAtlasRequestKey(FontKey font, GlyphAtlasRequest request)
            : this(font, MakeGlyphIdsKey(request.GlyphIds.Span), request.PixelSize, request.Padding, BitConverter.SingleToInt32Bits(request.DistanceRange), request.Mode)
        {
        }

        private static string MakeGlyphIdsKey(ReadOnlySpan<uint> glyphIds)
        {
            if (glyphIds.IsEmpty)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(glyphIds.Length * 4);
            for (var i = 0; i < glyphIds.Length; i++)
            {
                if (i != 0)
                {
                    builder.Append(',');
                }

                builder.Append(glyphIds[i]);
            }
            return builder.ToString();
        }
    }

    private readonly record struct GlyphAtlasKey(FontKey Font, uint GlyphId, int PixelSize, int Padding, float DistanceRange, GlyphAtlasMode Mode);

    private sealed class CachedGlyph
    {
        private CachedGlyph(uint glyphId, GlyphAtlasMode mode, int pixelSize, int width, int height, int stride, float bearingX, float bearingY, float advanceX, ReadOnlyMemory<byte> pixels)
        {
            GlyphId = glyphId;
            Mode = mode;
            PixelSize = pixelSize;
            Width = width;
            Height = height;
            Stride = stride;
            BearingX = bearingX;
            BearingY = bearingY;
            AdvanceX = advanceX;
            Pixels = pixels;
        }

        public uint GlyphId { get; }
        public GlyphAtlasMode Mode { get; }
        public int PixelSize { get; }
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public float BearingX { get; }
        public float BearingY { get; }
        public float AdvanceX { get; }
        public ReadOnlyMemory<byte> Pixels { get; }

        public GlyphBitmap ToBitmap(GlyphAtlasRequest request)
            => new(request, GlyphId, Width, Height, Stride, BearingX, BearingY, AdvanceX, Pixels);

        public static CachedGlyph Create(uint glyphId, GlyphAtlasMode mode, int pixelSize, int width, int height, int stride, float bearingX, float bearingY, float advanceX, ReadOnlyMemory<byte> pixels)
            => new(glyphId, mode, pixelSize, width, height, stride, bearingX, bearingY, advanceX, pixels);

        public static CachedGlyph Empty(uint glyphId, GlyphAtlasMode mode, int pixelSize, float advance)
        {
            var channels = mode == GlyphAtlasMode.Msdf ? 3 : 1;
            return Create(glyphId, mode, pixelSize, 1, 1, channels, 0, 0, advance, new byte[channels]);
        }
    }

    private sealed class PageBuild
    {
        private readonly byte[] _pixels;
        private readonly int _channels;
        private int _x;
        private int _y;
        private int _rowHeight;
        private readonly List<GlyphAtlasGlyph> _glyphs = new();

        public PageBuild(int size, int channels)
        {
            Size = size;
            _channels = channels;
            _pixels = new byte[size * size * channels];
        }

        public int Size { get; }

        public bool TryPlace(CachedGlyph glyph, int padding)
        {
            var placedWidth = glyph.Width + padding * 2;
            var placedHeight = glyph.Height + padding * 2;
            if (placedWidth > Size || placedHeight > Size)
            {
                return false;
            }

            if (_x + placedWidth > Size)
            {
                _x = 0;
                _y += _rowHeight;
                _rowHeight = 0;
            }
            if (_y + placedHeight > Size)
            {
                return false;
            }

            Blit(glyph.Pixels.Span, glyph.Stride, glyph.Width, glyph.Height, _x + padding, _y + padding);
            var u0 = _x / (float)Size;
            var v0 = _y / (float)Size;
            var u1 = (_x + glyph.Width) / (float)Size;
            var v1 = (_y + glyph.Height) / (float)Size;
            _glyphs.Add(new GlyphAtlasGlyph(glyph.GlyphId, 0, u0, v0, u1, v1, glyph.Width, glyph.Height, glyph.Stride, glyph.BearingX, glyph.BearingY, glyph.AdvanceX, glyph.Pixels));
            _x += placedWidth;
            _rowHeight = Math.Max(_rowHeight, placedHeight);
            return true;
        }

        private void Blit(ReadOnlySpan<byte> source, int stride, int width, int height, int dstX, int dstY)
        {
            for (var row = 0; row < height; row++)
            {
                var srcOffset = row * stride;
                var dstOffset = ((dstY + row) * Size + dstX) * _channels;
                source.Slice(srcOffset, width * _channels).CopyTo(_pixels.AsSpan(dstOffset, width * _channels));
            }
        }

        public PageResult FinalizePage(int pageIndex)
        {
            for (var i = 0; i < _glyphs.Count; i++)
            {
                var glyph = _glyphs[i];
                _glyphs[i] = glyph with { PageIndex = pageIndex };
            }
            return new PageResult(pageIndex, new GlyphAtlasPage(pageIndex, Size, Size, _pixels), _glyphs.ToArray());
        }
    }

    private readonly record struct PageResult(int PageIndex, GlyphAtlasPage Page, GlyphAtlasGlyph[] Glyphs);
}
