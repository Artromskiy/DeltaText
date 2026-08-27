using System.Buffers.Binary;
using Delta.Text.Contract;

namespace Delta.Text;

/// <summary>Reads the compact COLR/CPAL v0 color-font representation.</summary>
internal static class ColorFont
{
    private const ushort ForegroundPaletteIndex = ushort.MaxValue;
    private static readonly Rgba32 DefaultForeground = new(255, 255, 255, 255);

    internal static ColorGlyphLayer[] GetLayers(ReadOnlySpan<byte> fontData, uint glyphId, ColorGlyphOptions? options)
    {
        if (glyphId > ushort.MaxValue || !TryFindTable(fontData, "COLR", out var colr))
        {
            return Array.Empty<ColorGlyphLayer>();
        }

        var data = fontData.Slice(colr.Offset, colr.Length);
        if (!TryReadUInt16(data, 0, out var version) || version != 0
            || !TryReadUInt16(data, 2, out var baseGlyphCount)
            || !TryReadUInt32(data, 4, out var baseGlyphOffset)
            || !TryReadUInt32(data, 8, out var layerOffset)
            || !TryReadUInt16(data, 12, out var layerCount)
            || !TryGetRange(data, baseGlyphOffset, checked((uint)baseGlyphCount * 6), out var baseRecords)
            || !TryGetRange(data, layerOffset, checked((uint)layerCount * 4), out var layerRecords))
        {
            return Array.Empty<ColorGlyphLayer>();
        }

        var glyph = checked((ushort)glyphId);
        for (var i = 0; i < baseGlyphCount; i++)
        {
            var record = baseRecords.Slice(i * 6, 6);
            if (!TryReadUInt16(record, 0, out var recordGlyph) || recordGlyph != glyph
                || !TryReadUInt16(record, 2, out var firstLayer)
                || !TryReadUInt16(record, 4, out var layerCountForGlyph)
                || (uint)firstLayer + layerCountForGlyph > layerCount)
            {
                continue;
            }

            var result = new ColorGlyphLayer[layerCountForGlyph];
            var colorOptions = options ?? new ColorGlyphOptions(0, DefaultForeground);
            for (var layer = 0; layer < layerCountForGlyph; layer++)
            {
                var layerRecord = layerRecords.Slice((firstLayer + layer) * 4, 4);
                if (!TryReadUInt16(layerRecord, 0, out var layerGlyph)
                    || !TryReadUInt16(layerRecord, 2, out var paletteIndex))
                {
                    return Array.Empty<ColorGlyphLayer>();
                }

                result[layer] = new ColorGlyphLayer(
                    layerGlyph,
                    paletteIndex == ForegroundPaletteIndex
                        ? colorOptions.Foreground
                        : ReadPaletteColor(fontData, paletteIndex, colorOptions));
            }

            return result;
        }

        return Array.Empty<ColorGlyphLayer>();
    }

    internal static bool HasModernColorTables(ReadOnlySpan<byte> fontData)
    {
        if (TryFindTable(fontData, "SVG ", out _))
        {
            return true;
        }

        if (!TryFindTable(fontData, "COLR", out var colr))
        {
            return false;
        }

        var data = fontData.Slice(colr.Offset, colr.Length);
        return TryReadUInt16(data, 0, out var version) && version >= 1;
    }

    private static Rgba32 ReadPaletteColor(ReadOnlySpan<byte> fontData, ushort paletteIndex, ColorGlyphOptions options)
    {
        if (!TryFindTable(fontData, "CPAL", out var cpal))
        {
            return options.Foreground;
        }

        var data = fontData.Slice(cpal.Offset, cpal.Length);
        if (!TryReadUInt16(data, 0, out var version) || version != 0
            || !TryReadUInt16(data, 2, out var entriesPerPalette)
            || !TryReadUInt16(data, 4, out var paletteCount)
            || !TryReadUInt16(data, 6, out var colorRecordCount)
            || !TryReadUInt32(data, 8, out var colorRecordsOffset)
            || options.PaletteIndex >= paletteCount
            || !TryGetRange(data, 12, checked((uint)paletteCount * 2), out var paletteIndices)
            || !TryReadUInt16(paletteIndices, options.PaletteIndex * 2, out var firstColorRecord)
            || paletteIndex >= entriesPerPalette
            || (uint)firstColorRecord + entriesPerPalette > colorRecordCount
            || !TryGetRange(data, colorRecordsOffset, checked((uint)colorRecordCount * 4), out var colors))
        {
            return options.Foreground;
        }

        var index = checked((uint)firstColorRecord + paletteIndex);
        if (!TryGetRange(colors, checked(index * 4), 4, out var color))
        {
            return options.Foreground;
        }

        // CPAL stores color records as BGRA; the public image contract is RGBA.
        return new Rgba32(color[2], color[1], color[0], color[3]);
    }

    private static bool TryFindTable(ReadOnlySpan<byte> fontData, string tag, out Table table)
    {
        table = default;
        var data = fontData;
        if (data.Length < 12 || tag.Length != 4 || !TryReadUInt16(data, 4, out var count))
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var recordOffset = 12 + i * 16;
            if (recordOffset > data.Length - 16 || !HasTag(data, recordOffset, tag)
                || !TryReadUInt32(data, recordOffset + 8, out var offset)
                || !TryReadUInt32(data, recordOffset + 12, out var length)
                || !TryGetRange(data, offset, length, out _))
            {
                continue;
            }

            table = new Table(checked((int)offset), checked((int)length));
            return true;
        }

        return false;
    }

    private static bool HasTag(ReadOnlySpan<byte> data, int offset, string tag)
        => data[offset] == tag[0] && data[offset + 1] == tag[1]
            && data[offset + 2] == tag[2] && data[offset + 3] == tag[3];

    private static bool TryGetRange(ReadOnlySpan<byte> data, uint offset, uint length, out ReadOnlySpan<byte> range)
    {
        if (offset > int.MaxValue || length > int.MaxValue || (ulong)offset + length > (ulong)data.Length)
        {
            range = default;
            return false;
        }

        range = data.Slice(checked((int)offset), checked((int)length));
        return true;
    }

    private static bool TryReadUInt16(ReadOnlySpan<byte> data, int offset, out ushort value)
    {
        if (offset < 0 || offset > data.Length - 2)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        return true;
    }

    private static bool TryReadUInt32(ReadOnlySpan<byte> data, int offset, out uint value)
    {
        if (offset < 0 || offset > data.Length - 4)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
        return true;
    }

    private readonly record struct Table(int Offset, int Length);
}

internal readonly record struct ColorGlyphLayer(ushort GlyphId, Rgba32 Color);
