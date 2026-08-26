namespace DeltaText.Contract;

/// <summary>Stable identity of the immutable bytes from which a font was opened.</summary>
public readonly record struct FontSourceId(Guid Value)
{
    /// <summary>Returns whether this source identity is usable.</summary>
    public bool IsValid => Value != Guid.Empty;
}

/// <summary>
/// Opaque identity of an exact opened face, including collection index and variation coordinates.
/// </summary>
public readonly record struct FontInstanceId(ulong Value, uint Generation)
{
    /// <summary>Returns whether this instance identity is usable.</summary>
    public bool IsValid => Value != 0 && Generation != 0;
}

/// <summary>A packed four-byte OpenType or ISO 15924 tag.</summary>
/// <remarks>Value zero means that the implementation should infer the tag.</remarks>
public readonly record struct OpenTypeTag(uint Value)
{
    /// <summary>The tag value requesting automatic inference.</summary>
    public static OpenTypeTag Auto => default;

    /// <summary>Returns whether automatic inference was requested.</summary>
    public bool IsAuto => Value == 0;
}

/// <summary>A value for one OpenType variable-font design axis.</summary>
public readonly record struct FontVariation(OpenTypeTag Axis, float Value);

/// <summary>Immutable bytes and coordinates required to open one exact font instance.</summary>
/// <param name="Source">Stable identity of <paramref name="Data"/>.</param>
/// <param name="Data">Complete immutable font, TTC or OTC bytes.</param>
/// <param name="FaceIndex">Zero-based face index inside a collection.</param>
/// <param name="Variations">Variable-font design coordinates.</param>
public readonly record struct FontOpenRequest(
    FontSourceId Source,
    ReadOnlyMemory<byte> Data,
    uint FaceIndex,
    ReadOnlyMemory<FontVariation> Variations = default);

/// <summary>Font-wide metrics scaled to the requested pixels-per-em size.</summary>
/// <param name="UnitsPerEm">Font design-grid units per em.</param>
/// <param name="Ascent">Positive distance above the baseline.</param>
/// <param name="Descent">Positive distance below the baseline.</param>
/// <param name="LineGap">Recommended additional line spacing.</param>
/// <param name="UnderlinePosition">Underline position relative to the baseline.</param>
/// <param name="UnderlineThickness">Recommended underline thickness.</param>
public readonly record struct FontMetrics(
    uint UnitsPerEm,
    float Ascent,
    float Descent,
    float LineGap,
    float UnderlinePosition,
    float UnderlineThickness);
