namespace Delta.Text.Contract;

/// <summary>A half-open range of UTF-16 code units in the original input.</summary>
public readonly record struct TextRange(int StartUtf16, int LengthUtf16)
{
    /// <summary>The exclusive end offset.</summary>
    public int EndUtf16 => checked(StartUtf16 + LengthUtf16);
}

/// <summary>An OpenType shaping feature and its optional source-text range.</summary>
/// <param name="Tag">Four-byte OpenType feature tag.</param>
/// <param name="Value">Feature value; this is not limited to zero and one.</param>
/// <param name="Range">UTF-16 range, or <see langword="null"/> for the complete input.</param>
public readonly record struct OpenTypeFeature(OpenTypeTag Tag, uint Value, TextRange? Range = null);

/// <summary>Requested or resolved shaping direction.</summary>
public enum TextDirection : byte
{
    /// <summary>Infer direction from the input.</summary>
    Auto = 0,
    /// <summary>Horizontal left-to-right text.</summary>
    LeftToRight = 1,
    /// <summary>Horizontal right-to-left text.</summary>
    RightToLeft = 2,
    /// <summary>Vertical top-to-bottom text.</summary>
    TopToBottom = 3,
    /// <summary>Vertical bottom-to-top text.</summary>
    BottomToTop = 4,
}

/// <summary>Safety information produced by the shaping engine for one glyph cluster.</summary>
[Flags]
public enum GlyphSafety : byte
{
    /// <summary>No additional safety information.</summary>
    None = 0,
    /// <summary>Breaking before this cluster requires reshaping both sides.</summary>
    UnsafeToBreak = 1 << 0,
    /// <summary>Changing adjacent text may change this cluster.</summary>
    UnsafeToConcat = 1 << 1,
    /// <summary>Insertion of a tatweel before this cluster is shaping-safe.</summary>
    SafeToInsertTatweel = 1 << 2,
}

/// <summary>Input for shaping one uniformly styled text span.</summary>
/// <param name="Text">UTF-16 source text. The implementation must not normalize it implicitly.</param>
/// <param name="PixelsPerEm">Requested device size.</param>
/// <param name="FontFallback">Ordered fallback chain of exact font instances.</param>
/// <param name="Direction">Requested direction, or automatic inference.</param>
/// <param name="Script">ISO 15924 tag, or zero for inference.</param>
/// <param name="Language">BCP 47 language tag, or <see langword="null"/> for inference.</param>
/// <param name="Features">OpenType feature values and ranges.</param>
public readonly record struct TextShapeRequest(
    ReadOnlyMemory<char> Text,
    float PixelsPerEm,
    ReadOnlyMemory<FontInstanceId> FontFallback,
    TextDirection Direction = TextDirection.Auto,
    OpenTypeTag Script = default,
    string? Language = null,
    ReadOnlyMemory<OpenTypeFeature> Features = default);

/// <summary>One shaped glyph and its baseline-relative positioning data.</summary>
/// <param name="GlyphId">Glyph identifier meaningful only with its run's font instance.</param>
/// <param name="ClusterUtf16">Offset into the original UTF-16 input.</param>
/// <param name="AdvanceX">Horizontal pen advance.</param>
/// <param name="AdvanceY">Vertical pen advance.</param>
/// <param name="OffsetX">Horizontal offset from the current pen position.</param>
/// <param name="OffsetY">Vertical offset from the current pen position.</param>
/// <param name="Safety">Cluster safety information.</param>
public readonly record struct ShapedGlyph(
    uint GlyphId,
    int ClusterUtf16,
    float AdvanceX,
    float AdvanceY,
    float OffsetX,
    float OffsetY,
    GlyphSafety Safety);

/// <summary>A rectangle expressed relative to the baseline origin.</summary>
/// <remarks>The coordinate system points right on X and down on Y.</remarks>
public readonly record struct TextBounds(float Left, float Top, float Right, float Bottom)
{
    /// <summary>Rectangle width.</summary>
    public float Width => Right - Left;

    /// <summary>Rectangle height.</summary>
    public float Height => Bottom - Top;
}

/// <summary>One directional run shaped with one exact font instance.</summary>
/// <param name="SourceRange">Corresponding UTF-16 range in the original input.</param>
/// <param name="Font">Exact font instance that owns the glyph IDs.</param>
/// <param name="Direction">Resolved shaping direction.</param>
/// <param name="BidiLevel">Resolved Unicode bidirectional embedding level.</param>
/// <param name="PixelsPerEm">Device size used during shaping.</param>
/// <param name="AdvanceX">Total horizontal run advance.</param>
/// <param name="AdvanceY">Total vertical run advance.</param>
/// <param name="Bounds">Conservative run bounds relative to its baseline origin.</param>
/// <param name="Glyphs">Ordered shaped glyph sequence.</param>
public readonly record struct ShapedRun(
    TextRange SourceRange,
    FontInstanceId Font,
    TextDirection Direction,
    byte BidiLevel,
    float PixelsPerEm,
    float AdvanceX,
    float AdvanceY,
    TextBounds Bounds,
    ReadOnlyMemory<ShapedGlyph> Glyphs);

/// <summary>Owned immutable result of shaping one input span.</summary>
public sealed class ShapedText
{
    internal ShapedText(int textLengthUtf16, ReadOnlyMemory<ShapedRun> runs)
    {
        TextLengthUtf16 = textLengthUtf16;
        Runs = runs;
    }

    /// <summary>Original input length in UTF-16 code units.</summary>
    public int TextLengthUtf16 { get; }

    /// <summary>Visual runs split by direction and resolved font fallback.</summary>
    public ReadOnlyMemory<ShapedRun> Runs { get; }
}
