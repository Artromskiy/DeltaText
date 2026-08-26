using System.Buffers.Binary;
using DeltaText.Contract;
using SkiaSharp;

namespace DeltaText;

/// <summary>Implementation-owned HarfBuzz face and immutable source storage.</summary>
internal sealed class FontFace : IDisposable
{
    private readonly byte[] _fontData;
    private readonly IntPtr _blob;
    private readonly IntPtr _face;
    private readonly IntPtr _font;
    private int _disposed;

    private FontFace(byte[] data, IntPtr blob, IntPtr face, IntPtr font, int unitsPerEm, RawFontMetrics metrics)
    {
        _fontData = data;
        _blob = blob;
        _face = face;
        _font = font;
        UnitsPerEm = unitsPerEm;
        Metrics = metrics;
    }

    internal int UnitsPerEm { get; }
    internal RawFontMetrics Metrics { get; }
    internal IntPtr NativeFont => _font;

    internal static FontFace FromRequest(in FontOpenRequest request)
    {
        var ownedData = request.Data.ToArray();
        var font = NativeHarfBuzz.CreateFont(
            ownedData,
            request.FaceIndex,
            request.Variations.Span,
            out var blob,
            out var face,
            out var unitsPerEm);
        try
        {
            return new FontFace(ownedData, blob, face, font, unitsPerEm, ReadFontMetrics(ownedData, unitsPerEm));
        }
        catch
        {
            NativeHarfBuzz.DestroyFont(font, face, blob);
            throw;
        }
    }

    internal uint GetGlyphId(uint codepoint)
    {
        ThrowIfDisposed();
        return NativeHarfBuzz.GetGlyph(_font, codepoint);
    }

    internal RawGlyphMetrics GetGlyphMetrics(uint glyphId)
    {
        ThrowIfDisposed();
        return NativeHarfBuzz.GetGlyphMetrics(_font, glyphId, UnitsPerEm);
    }

    internal SKTypeface CreateTypeface()
    {
        ThrowIfDisposed();
        using var data = SKData.CreateCopy(_fontData);
        return SKTypeface.FromData(data) ?? throw new InvalidOperationException("Skia could not create a typeface for the font data.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            NativeHarfBuzz.DestroyFont(_font, _face, _blob);
        }

        GC.KeepAlive(_fontData);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static RawFontMetrics ReadFontMetrics(byte[] data, int unitsPerEm)
    {
        var hhea = FindTable(data, "hhea");
        var post = FindTable(data, "post");
        var ascent = unitsPerEm;
        var descent = unitsPerEm / 4;
        var lineGap = 0;
        var underlinePosition = 0;
        var underlineThickness = 0;
        if (hhea >= 0 && hhea + 10 <= data.Length)
        {
            ascent = ReadInt16(data, hhea + 4);
            descent = Math.Max(0, -ReadInt16(data, hhea + 6));
            lineGap = ReadInt16(data, hhea + 8);
        }

        if (post >= 0 && post + 12 <= data.Length)
        {
            underlinePosition = ReadInt16(data, post + 8);
            underlineThickness = ReadInt16(data, post + 10);
        }

        return new RawFontMetrics(unitsPerEm, ascent, descent, lineGap, underlinePosition, underlineThickness);
    }

    private static int FindTable(byte[] data, string tag)
    {
        if (data.Length < 12)
        {
            return -1;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4, 2));
        for (var i = 0; i < count; i++)
        {
            var record = 12 + i * 16;
            if (record + 16 > data.Length || ReadTag(data, record) != tag)
            {
                continue;
            }

            var offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8, 4));
            return offset <= int.MaxValue ? checked((int)offset) : -1;
        }

        return -1;
    }

    private static string ReadTag(byte[] data, int offset)
        => new(data.AsSpan(offset, 4).ToArray().Select(static value => (char)value).ToArray());

    private static short ReadInt16(byte[] data, int offset)
        => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));
}

internal readonly record struct RawFontMetrics(
    int UnitsPerEm,
    int Ascent,
    int Descent,
    int LineGap,
    int UnderlinePosition,
    int UnderlineThickness);

internal readonly record struct RawGlyphMetrics(
    uint GlyphId,
    int AdvanceX,
    int AdvanceY,
    int BearingX,
    int BearingY,
    int Width,
    int Height,
    int UnitsPerEm);

internal readonly record struct RawShapedGlyph(
    uint GlyphId,
    int ClusterUtf16,
    int AdvanceX,
    int AdvanceY,
    int OffsetX,
    int OffsetY,
    GlyphSafety Safety);
