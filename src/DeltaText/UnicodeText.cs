using Delta.Text.Contract;
using ContractGraphemeCluster = Delta.Text.Contract.GraphemeCluster;

namespace Delta.Text;

/// <summary>
/// Unicode text-boundary operations used by layout consumers.
/// </summary>
/// <remarks>
/// The implementation uses the public Unicode property and grapheme APIs in
/// SixLabors.Fonts 3.1.0 and copies the result into DeltaText-owned snapshots.
/// SixLabors types never cross this API. Input is not normalized.
/// </remarks>
public static class UnicodeText
{
    /// <summary>Segments valid UTF-16 text into extended grapheme clusters.</summary>
    /// <param name="text">The source text in logical order.</param>
    /// <returns>An owned immutable cluster map.</returns>
    /// <exception cref="ArgumentException">The input contains an unpaired surrogate.</exception>
    public static GraphemeClusterMap SegmentGraphemes(ReadOnlyMemory<char> text)
    {
        ValidateUtf16(text.Span);
        var clusters = new List<ContractGraphemeCluster>();
        foreach (var cluster in SixLabors.Fonts.Unicode.MemoryExtensions.EnumerateGraphemes(text.Span))
        {
            clusters.Add(new ContractGraphemeCluster(
                new TextRange(cluster.Utf16Offset, cluster.Utf16Length),
                cluster.CodePointCount));
        }

        return new GraphemeClusterMap(text.Length, clusters.ToArray());
    }

    /// <summary>Finds UAX #14 line-break opportunities in valid UTF-16 text.</summary>
    /// <param name="text">The source text in logical order.</param>
    /// <returns>An owned immutable line-break map.</returns>
    /// <exception cref="ArgumentException">The input contains an unpaired surrogate.</exception>
    public static LineBreakMap GetLineBreaks(ReadOnlyMemory<char> text)
    {
        ValidateUtf16(text.Span);
        var (codePoints, codePointOffsets) = Decode(text.Span);
        var breakTypes = UnicodeLineBreakEngine.GetBreakOpportunities(codePoints);
        var opportunities = new List<LineBreakOpportunity>();
        for (var index = 0; index < breakTypes.Length; index++)
        {
            var kind = breakTypes[index];
            if (kind == UnicodeBreakType.None)
            {
                continue;
            }

            opportunities.Add(new LineBreakOpportunity(
                codePointOffsets[index],
                kind == UnicodeBreakType.Mandatory ? LineBreakKind.Mandatory : LineBreakKind.Optional));
        }

        return new LineBreakMap(text.Length, opportunities.ToArray());
    }

    private static (int[] CodePoints, int[] Offsets) Decode(ReadOnlySpan<char> text)
    {
        var codePoints = new int[text.Length];
        var offsets = new int[text.Length + 1];
        var count = 0;
        for (var index = 0; index < text.Length; count++)
        {
            offsets[count] = index;
            if (index + 1 < text.Length && char.IsSurrogatePair(text[index], text[index + 1]))
            {
                codePoints[count] = char.ConvertToUtf32(text[index], text[index + 1]);
                index += 2;
            }
            else
            {
                codePoints[count] = text[index];
                index++;
            }
        }

        offsets[count] = text.Length;
        return (codePoints.AsSpan(0, count).ToArray(), offsets.AsSpan(0, count + 1).ToArray());
    }

    private static void ValidateUtf16(ReadOnlySpan<char> text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsSurrogate(text[index]))
            {
                continue;
            }

            if (!char.IsHighSurrogate(text[index]) ||
                index + 1 >= text.Length ||
                !char.IsLowSurrogate(text[index + 1]))
            {
                throw new ArgumentException(
                    "Text contains an unpaired UTF-16 surrogate.",
                    nameof(text));
            }

            index++;
        }
    }
}
