using System.Security.Cryptography;
using SkiaSharp;

namespace Delta.Text;

public sealed class FontFace : IDisposable
{
    private readonly byte[] _fontData;
    private readonly IntPtr _blob;
    private readonly IntPtr _face;
    private readonly IntPtr _font;
    private int _disposed;

    private FontFace(FontKey key, byte[] data, IntPtr blob, IntPtr face, IntPtr font, int unitsPerEm)
    {
        Key = key;
        _fontData = data;
        _blob = blob;
        _face = face;
        _font = font;
        UnitsPerEm = unitsPerEm;
        Metrics = ReadHorizontalMetrics(data, unitsPerEm);
    }

    public FontKey Key { get; }
    public int UnitsPerEm { get; }
    public FontMetrics Metrics { get; }
    internal ReadOnlyMemory<byte> FontData => _fontData;
    internal IntPtr NativeFont => _font;

    public static FontFace LoadFile(FontKey key, string path, uint faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(path);
        return FromBytes(key, File.ReadAllBytes(path), faceIndex);
    }

    public static FontFace FromBytes(FontKey key, ReadOnlyMemory<byte> data, uint faceIndex = 0)
    {
        if (data.IsEmpty) throw new ArgumentException("A font face cannot be empty.", nameof(data));
        var ownedData = data.ToArray();
        var font = NativeHarfBuzz.CreateFont(ownedData, faceIndex, out var blob, out var face, out var unitsPerEm);
        try
        {
            return new FontFace(key, ownedData, blob, face, font, unitsPerEm);
        }
        catch
        {
            NativeHarfBuzz.DestroyFont(font, face, blob);
            throw;
        }
    }

    public static FontFace LoadFile(string family, string style, string path, uint faceIndex = 0)
    {
        var sourceId = CreateSourceId(File.ReadAllBytes(path));
        return LoadFile(new FontKey(family, style, sourceId), path, faceIndex);
    }

    public uint GetGlyphId(uint codepoint)
    {
        ThrowIfDisposed();
        return NativeHarfBuzz.GetGlyph(_font, codepoint);
    }

    public GlyphMetrics GetGlyphMetrics(uint glyphId)
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

    public ShapedGlyphRun Shape(TextShapingRequest request)
    {
        ThrowIfDisposed();
        var rawGlyphs = new List<ShapedGlyph>(request.Text.Length);
        NativeHarfBuzz.Shape(_font, request.Text, request.Direction, request.Features.Span, rawGlyphs);

        var scale = request.Size / UnitsPerEm;
        var glyphs = new ShapedGlyph[rawGlyphs.Count];
        var positioned = new PositionedGlyph[rawGlyphs.Count];
        var penX = 0f;
        var penY = 0f;
        var left = 0f;
        var bottom = float.PositiveInfinity;
        var right = 0f;
        var top = float.NegativeInfinity;

        for (var i = 0; i < rawGlyphs.Count; i++)
        {
            var raw = rawGlyphs[i];
            var advanceX = raw.AdvanceX * scale;
            var advanceY = raw.AdvanceY * scale;
            var offsetX = raw.OffsetX * scale;
            var offsetY = raw.OffsetY * scale;
            glyphs[i] = new ShapedGlyph(raw.GlyphId, raw.Cluster, advanceX, advanceY, offsetX, offsetY);
            positioned[i] = new PositionedGlyph(raw.GlyphId, raw.Cluster, penX, penY, advanceX, advanceY, offsetX, offsetY);

            var metrics = GetGlyphMetrics(raw.GlyphId);
            var glyphLeft = penX + offsetX + metrics.BearingX * scale;
            var glyphTop = penY + offsetY + metrics.BearingY * scale;
            var glyphRight = glyphLeft + metrics.Width * scale;
            var glyphBottom = glyphTop + metrics.Height * scale;
            left = Math.Min(left, glyphLeft);
            right = Math.Max(right, Math.Max(glyphRight, penX + advanceX));
            bottom = Math.Min(bottom, glyphBottom);
            top = Math.Max(top, glyphTop);
            penX += advanceX;
            penY += advanceY;
        }

        if (rawGlyphs.Count == 0)
            bottom = top = 0;
        else
        {
            bottom = Math.Min(bottom, Metrics.Descender * scale);
            top = Math.Max(top, Metrics.Ascender * scale);
        }

        return new ShapedGlyphRun(Key, request.Size, request.Text.Length, glyphs, positioned, penX, penY, new TextBounds(left, bottom, right, top));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            NativeHarfBuzz.DestroyFont(_font, _face, _blob);
        GC.KeepAlive(_fontData);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static string CreateSourceId(ReadOnlySpan<byte> data)
    {
        const string Hex = "0123456789abcdef";
        var hash = SHA256.HashData(data);
        var chars = new char[hash.Length * 2];
        for (var i = 0; i < hash.Length; i++)
        {
            chars[i * 2] = Hex[hash[i] >> 4];
            chars[i * 2 + 1] = Hex[hash[i] & 0x0f];
        }

        return new string(chars);
    }

    private static FontMetrics ReadHorizontalMetrics(byte[] data, int unitsPerEm)
    {
        // hhea is a fixed, big-endian OpenType table. Reading these 10 bytes
        // locally avoids a second native ABI call while remaining valid for
        // TrueType/OpenType fonts that expose horizontal metrics.
        if (data.Length >= 12)
        {
            var tableCount = ReadUInt16(data, 4);
            for (var i = 0; i < tableCount; i++)
            {
                var record = 12 + i * 16;
                if (record + 16 > data.Length || ReadTag(data, record) != "hhea") continue;
                var offset = checked((int)ReadUInt32(data, record + 8));
                if (offset >= 0 && offset + 10 <= data.Length)
                    return new FontMetrics(unitsPerEm, ReadInt16(data, offset + 4), ReadInt16(data, offset + 6), ReadInt16(data, offset + 8));
            }
        }
        return new FontMetrics(unitsPerEm, unitsPerEm, -unitsPerEm / 4, 0);
    }

    private static string ReadTag(byte[] data, int offset)
        => new string(new[] { (char)data[offset], (char)data[offset + 1], (char)data[offset + 2], (char)data[offset + 3] });

    private static ushort ReadUInt16(byte[] data, int offset)
        => (ushort)((data[offset] << 8) | data[offset + 1]);

    private static short ReadInt16(byte[] data, int offset)
        => unchecked((short)ReadUInt16(data, offset));

    private static uint ReadUInt32(byte[] data, int offset)
        => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
}
