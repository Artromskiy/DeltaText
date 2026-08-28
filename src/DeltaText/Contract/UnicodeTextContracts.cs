namespace Delta.Text.Contract;

/// <summary>
/// One extended grapheme cluster in the original UTF-16 input.
/// </summary>
/// <param name="SourceRange">The half-open UTF-16 range of the cluster.</param>
/// <param name="CodePointCount">The number of Unicode scalar values in the cluster.</param>
public readonly record struct GraphemeCluster(TextRange SourceRange, int CodePointCount);

/// <summary>
/// Owned immutable extended grapheme segmentation result.
/// </summary>
public sealed class GraphemeClusterMap
{
    internal GraphemeClusterMap(int textLengthUtf16, ReadOnlyMemory<GraphemeCluster> clusters)
    {
        TextLengthUtf16 = textLengthUtf16;
        Clusters = clusters;
    }

    /// <summary>The original input length in UTF-16 code units.</summary>
    public int TextLengthUtf16 { get; }

    /// <summary>Clusters in logical source order.</summary>
    public ReadOnlyMemory<GraphemeCluster> Clusters { get; }
}

/// <summary>The kind of line break exposed by the Unicode line-break producer.</summary>
public enum LineBreakKind
{
    /// <summary>No break opportunity.</summary>
    None = 0,

    /// <summary>The line break is optional.</summary>
    Optional = 1,

    /// <summary>The line break is required by the input.</summary>
    Mandatory = 2,
}

/// <summary>
/// One UAX #14 line-break opportunity in UTF-16 coordinates.
/// </summary>
/// <param name="PositionUtf16">The boundary after the preceding Unicode scalar value.</param>
/// <param name="Kind">Whether the opportunity is optional or mandatory.</param>
public readonly record struct LineBreakOpportunity(
    int PositionUtf16,
    LineBreakKind Kind);

/// <summary>
/// Owned immutable UAX #14 line-break result.
/// </summary>
/// <remarks>
/// Only actual opportunities are stored. A boundary not present in
/// <see cref="Opportunities"/> is prohibited. The final boundary is always
/// represented as mandatory for non-empty and empty input alike. Width-aware
/// line measurement and the policy for consuming trailing whitespace remain
/// responsibilities of the layout consumer.
/// </remarks>
public sealed class LineBreakMap
{
    internal LineBreakMap(int textLengthUtf16, ReadOnlyMemory<LineBreakOpportunity> opportunities)
    {
        TextLengthUtf16 = textLengthUtf16;
        Opportunities = opportunities;
    }

    /// <summary>The original input length in UTF-16 code units.</summary>
    public int TextLengthUtf16 { get; }

    /// <summary>Break opportunities in logical source order.</summary>
    public ReadOnlyMemory<LineBreakOpportunity> Opportunities { get; }
}
