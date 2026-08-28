using Delta.Maths;
using Delta.Text.Contract;

namespace Delta.Text;

internal static class CpuDistanceDecoder
{
    internal static float Decode(
        GlyphImageEncoding encoding,
        ReadOnlySpan<byte> source,
        int sourceIndex,
        float distanceRange)
        => encoding switch
        {
            GlyphImageEncoding.CoverageR8 => source[sourceIndex] / 255f,
            GlyphImageEncoding.SdfR8 => DecodeDistance(source[sourceIndex], distanceRange),
            GlyphImageEncoding.MsdfRgb8 => DecodeMsdf(source, sourceIndex, distanceRange),
            _ => throw new InvalidDataException("Glyph image encoding is not supported by the CPU renderer.")
        };

    private static float DecodeDistance(byte encoded, float distanceRange)
    {
        var distance = DecodeSignedDistance(encoded, distanceRange);
        return DeltaMaths.Smoothstep(-0.5f, 0.5f, distance);
    }

    private static float DecodeMsdf(ReadOnlySpan<byte> source, int index, float distanceRange)
    {
        var red = DecodeSignedDistance(source[index], distanceRange);
        var green = DecodeSignedDistance(source[index + 1], distanceRange);
        var blue = DecodeSignedDistance(source[index + 2], distanceRange);
        var median = DeltaMaths.Max(
            DeltaMaths.Min(red, green),
            DeltaMaths.Min(DeltaMaths.Max(red, green), blue));
        return DeltaMaths.Smoothstep(-0.5f, 0.5f, median);
    }

    private static float DecodeSignedDistance(byte encoded, float distanceRange)
        => (encoded / 255f - 0.5f) * (2f * distanceRange);
}
