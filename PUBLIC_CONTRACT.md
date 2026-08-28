# DeltaText public contract v1.2

## Purpose

DeltaText is a renderer-neutral producer. It accepts immutable font bytes and
Unicode text, performs OpenType shaping and produces independent CPU glyph
images. The contract contains everything a consumer needs to build, rebuild,
convert or upload a bitmap text atlas without exposing DeltaText internals.

The authoritative C# declarations live in
[`src/DeltaText/Contract`](src/DeltaText/Contract) under the
`Delta.Text.Contract` namespace. These declarations are part of the primary
`DeltaText` assembly. DeltaText does not expose atlas or cache types; atlas
packing and cache policy belong to the consumer as described below.

## Ownership boundary

DeltaText owns:

- immutable font bytes and font-backend state between `OpenFont` and
  `CloseFont`;
- OpenType shaping, fallback resolution and scaled font metrics;
- glyph outline interpretation and generation of unpacked coverage, SDF, MSDF
  or flattened color-glyph images;
- owned immutable arrays returned by `ShapedText` and `GlyphImage`.

The consumer owns:

- shaped-run and glyph-image cache policy;
- atlas packing, pages, UV assignment, padding between packed entries and
  eviction;
- pixel conversion required by the selected Vulkan image format;
- staging, dirty uploads, descriptors, pipelines, batching and GPU lifetime;
- draw colors for monochrome coverage/SDF/MSDF text.

Consequently, none of the following belongs in the DeltaText public contract:

```text
atlas page IDs
UV rectangles
pipeline IDs
batch keys or batch ranges
dirty upload rectangles
source upload row pitch
Vulkan formats or resources
renderer callbacks
```

## Public operations

`ITextService` deliberately exposes only five operations:

```text
OpenFont           immutable bytes + face index + variations -> FontInstanceId
CloseFont          releases an opened instance
GetFontMetrics     exact instance + pixels-per-em -> scaled metrics
Shape              UTF-16 text + fallback chain -> shaped runs
GenerateGlyphImage exact instance + glyph ID + image request -> unpacked pixels
```

An implementation may cache internally, but no cache object or cache budget is
part of this boundary. Invalid arguments fail synchronously. An unavailable
requested representation fails with `NotSupportedException`; it is not
represented by a nullable successful result.

## Unicode boundary operations

Text boundary data is available without opening a font through the independent
static `Delta.Text.UnicodeText` API:

```text
SegmentGraphemes(UTF-16) -> GraphemeClusterMap
GetLineBreaks(UTF-16)    -> LineBreakMap
```

`GraphemeClusterMap` contains owned extended grapheme clusters. Each
`GraphemeCluster.SourceRange` is a half-open range in the original UTF-16
input, and `CodePointCount` identifies how many Unicode scalar values it
contains. `LineBreakMap` contains owned `LineBreakOpportunity` values at
UTF-16 boundaries. `LineBreakKind.Optional` is a permitted break and
`LineBreakKind.Mandatory` is required; a boundary absent from the map is
prohibited. The final boundary is always represented as mandatory, including
for empty input.

These methods reject unpaired UTF-16 surrogates, do not normalize input, and
return immutable snapshots whose arrays are owned by the result. They do not
perform width-aware line construction or consume font metrics. Layout
consumers remain responsible for measuring text, choosing a line width and
handling trailing whitespace.

`Shape` applies the same valid-UTF-16 requirement and additionally rejects
Unicode noncharacters before calling the font backend. Noncharacters are not
silently converted to a missing glyph. A consumer that needs to inspect raw
Unicode boundaries can still use `UnicodeText` first; boundary segmentation is
not a promise that every code point is valid input for OpenType shaping.

## Identity and lifetime

`FontSourceId` is the stable identity of immutable source bytes. The Engine
adapter maps its existing asset identity to this value; DeltaText must not
depend on DeltaEngine merely to obtain an ID. A source may contain multiple
faces, so `FaceIndex` is explicit for TTC and OTC collections.

`FontInstanceId` identifies the exact opened instance after applying collection
index and variable-font coordinates. Glyph IDs are meaningful only together
with this instance. Its generation prevents stale handles from silently
resolving after a slot is reused.

The `FontOpenRequest.Data` memory is input to `OpenFont`. The implementation
must retain its own immutable storage for as long as the returned instance is
open; it may copy the bytes or transfer them into another owned immutable
representation.

## Text and coordinate conventions

- Input is UTF-16, matching .NET strings.
- `TextRange` and `ClusterUtf16` are offsets in the original, unnormalised
  UTF-16 input. DeltaText must not silently normalize the text.
- A range is represented by start and length. Its end is derived and exclusive.
- X points right and Y points down.
- Advances move the pen. Offsets move only the glyph relative to the current
  pen position.
- `ShapedRun.Bounds` and `GlyphImage.PlaneBounds` are relative to the baseline
  origin.
- `BidiLevel` is the resolved Unicode bidirectional embedding level.
- A shaping request describes one uniformly styled text span; resolved font
  fallback and direction may still split it into multiple output runs.

The output stores advances and offsets rather than redundant absolute glyph
positions. A consumer obtains the position by accumulating advances and adding
the current glyph offset.

## Shaping requirements

The feature contract mirrors OpenType rather than a Boolean-only UI
abstraction:

- feature and variation axes use extensible four-byte tags;
- feature values are `uint` because many features accept values other than
  zero and one;
- a feature may apply globally or to a UTF-16 source range;
- language uses a BCP 47 tag;
- script uses an ISO 15924-compatible four-byte tag or automatic inference;
- horizontal and vertical advances and offsets are preserved;
- `UnsafeToBreak`, `UnsafeToConcat` and `SafeToInsertTatweel` are preserved for
  correct line layout and bounded incremental reshaping.

The implementation resolves paragraph base direction, explicit embeddings and
isolates, weak and neutral types, paired brackets, implicit levels and visual
run order before shaping through SixLabors.Fonts. The resolver uses an embedded
Unicode 17.0 `Bidi_Class`/`Bidi_Paired_Bracket` table and implements the
corresponding UAX #9 rules, including overflow handling for explicit controls.

Unicode boundary data is also available without opening a font:

```text
UnicodeText.SegmentGraphemes(UTF-16) -> GraphemeClusterMap
UnicodeText.GetLineBreaks(UTF-16)   -> LineBreakMap
```

`GraphemeClusterMap.Clusters` contains owned extended grapheme clusters in
logical order. `LineBreakMap.Opportunities` contains only permitted UAX #14
boundaries; each position is a UTF-16 offset after a Unicode scalar, and each
entry is `Optional` or `Mandatory`. The maps require valid UTF-16 and do not
normalize input. Width-dependent line measurement, trailing-whitespace
consumption and final line placement remain responsibilities of the layout
consumer. The boundary implementation is validated against the Unicode 17
GraphemeBreakTest and LineBreakTest corpora. Updating the Unicode data version
is an internal package/data update, not a change to these value-level shapes.

The contract carries explicit script, language, feature-value and feature-range
fields so another producer can implement them without changing the consumer
API. The bundled SixLabors.Fonts 3.0.0 adapter currently supports automatic
script/language inference, globally enabled Boolean feature tags and disabling
the `kern` feature. It rejects explicit script or language selectors, ranged
features, values greater than one and disabling other default features with
`NotSupportedException`; it never silently ignores those requests.

Safety flags are conservative: clusters spanning multiple source scalars,
combining-mark clusters and Arabic joining contexts are marked
`UnsafeToBreak | UnsafeToConcat`. `SafeToInsertTatweel` is not asserted without
backend evidence.

## Verified conformance profile

The implementation is checked against the Unicode 17 data shipped with the
repository or supplied to `FontCheck`: all 91,707 `BidiCharacterTest` cases
through visual-order rule L2, 770,241 paragraph variants from `BidiTest`, all
128 `BidiBrackets` mappings, 766 `GraphemeBreakTest` cases and 19,338
`LineBreakTest` cases. These are test-suite guarantees for the current embedded
Unicode data, not a claim that the contract performs width-dependent line
layout.

`Checks/FontCheck` additionally exercises real glyph images through the public
producer API. It renders Doto at 64/128 px and Luckiest Guy at 48/96 px, uses a
Noto Sans/Noto Sans Arabic fallback chain for Latin, Cyrillic and Arabic
coverage, and probes Hebrew, Indic, Thai, CJK, combining marks, emoji, mixed
direction text and controls. Valid probes must produce finite shaped metrics,
valid source clusters and tightly packed Coverage/SDF/MSDF/Color images. A
separate supersampled outline callback path with ImageSharp bitmap/PNG output
compares Coverage images; the accepted alignment is at most one pixel and the
alpha thresholds are `32/255` mean and `224/255` at the 95th percentile.

This profile deliberately does not claim that a font contains every probed
script: missing-glyph output is a valid result unless the fixture selects a
font that is required to cover that script. ImageSharp is an oracle surface,
not a DeltaText runtime dependency or font renderer.

## Glyph-image requirements

`GenerateGlyphImage` returns one image that has not been placed into an atlas.
The supported contract encodings are:

| Encoding | Payload | Interpretation |
|---|---:|---|
| `CoverageR8` | 1 byte/pixel | unsigned normalized coverage |
| `SdfR8` | 1 byte/pixel | unsigned normalized signed distance |
| `MsdfRgb8` | 3 bytes/pixel | unsigned normalized multi-channel distance |
| `ColorRgba8PremultipliedSrgb` | 4 bytes/pixel | flattened color-font presentation |

Pixels are row-major from top to bottom and are always tightly packed. Their
length is exactly `Width * Height * bytesPerPixel`. There is no public stride
or row-pitch field. DeltaText returns this tightly packed managed payload
directly; no native image buffer is involved.

`PlaneBounds` maps the complete image, including the distance-field border, to
baseline-relative device coordinates. Whitespace or another glyph with no
visible image is represented by zero width, zero height and empty pixels; it
does not require an atlas entry. Coverage, SDF, MSDF and color pixels are
rasterized by DeltaText from outline data returned by SixLabors.Fonts.

SDF and MSDF requests use `DistanceRange`. Atlas spacing is separate and is
owned by the packer. Color requests include a palette index and foreground
color. Color layers exposed by SixLabors.Fonts are flattened by DeltaText so
the default palette selection is deterministic. The SixLabors.Fonts 3.0.0
adapter supports palette index zero; a non-default palette is rejected because
that package version does not expose palette selection through its public
rendering API. If a newer color format cannot be exposed
as outline callbacks by the installed SixLabors.Fonts version, DeltaText uses
the foreground-colored outline fallback and still returns the valid RGBA
contract rather than exposing package objects or native handles.

This v1 boundary supports arbitrary transformations of bitmap atlases. Direct
access to vector outlines for a custom rasterizer would be a separate optional
`IGlyphOutlineSource` capability and is intentionally not part of the minimal
contract.

## Primitive JSON shape

The following document expands every exchanged value to JSON primitives. It
is an illustrative serialization shape; the C# API uses `ReadOnlyMemory<T>`
for immutable payloads rather than JSON at runtime.

```json
{
  "fontOpenRequest": {
    "sourceId": "71d54147-203a-48df-b3a2-9bba70a68386",
    "fontDataBase64": "AAEAAA...",
    "faceIndex": 0,
    "variations": [
      {
        "axisTag": "wght",
        "value": 450.0
      }
    ]
  },
  "fontInstance": {
    "value": 17,
    "generation": 1
  },
  "fontMetrics": {
    "unitsPerEm": 1000,
    "ascent": 19.2,
    "descent": 4.8,
    "lineGap": 2.0,
    "underlinePosition": 2.1,
    "underlineThickness": 1.0
  },
  "shapeRequest": {
    "text": "office Привет",
    "pixelsPerEm": 24.0,
    "fontFallback": [
      {
        "value": 17,
        "generation": 1
      }
    ],
    "direction": "leftToRight",
    "scriptTag": "auto",
    "language": "ru",
    "features": [
      {
        "tag": "liga",
        "value": 1,
        "range": null
      },
      {
        "tag": "kern",
        "value": 1,
        "range": {
          "startUtf16": 0,
          "lengthUtf16": 13
        }
      }
    ]
  },
  "shapedText": {
    "textLengthUtf16": 13,
    "runs": [
      {
        "sourceRange": {
          "startUtf16": 0,
          "lengthUtf16": 13
        },
        "font": {
          "value": 17,
          "generation": 1
        },
        "direction": "leftToRight",
        "bidiLevel": 0,
        "pixelsPerEm": 24.0,
        "advanceX": 121.4,
        "advanceY": 0.0,
        "bounds": {
          "left": 0.0,
          "top": -18.4,
          "right": 121.4,
          "bottom": 5.1
        },
        "glyphs": [
          {
            "glyphId": 5044,
            "clusterUtf16": 1,
            "advanceX": 18.7,
            "advanceY": 0.0,
            "offsetX": 0.0,
            "offsetY": 0.0,
            "safety": 1
          }
        ]
      }
    ]
  },
  "glyphImageRequest": {
    "font": {
      "value": 17,
      "generation": 1
    },
    "glyphId": 5044,
    "pixelsPerEm": 48.0,
    "mode": "msdf",
    "distanceRange": 4.0,
    "color": null
  },
  "glyphImage": {
    "font": {
      "value": 17,
      "generation": 1
    },
    "glyphId": 5044,
    "pixelsPerEm": 48.0,
    "encoding": "msdfRgb8",
    "distanceRange": 4.0,
    "width": 42,
    "height": 51,
    "planeBounds": {
      "left": -3.0,
      "top": -42.0,
      "right": 39.0,
      "bottom": 9.0
    },
    "pixelsBase64": "AAECAwQF..."
  }
}
```

## Standards and reference models

The contract follows these durable parts of established APIs and standards:

- [Avalonia `GlyphRun`](https://api-docs.avaloniaui.net/docs/T_Avalonia_Media_GlyphRun)
  and [`GlyphInfo`](https://api-docs.avaloniaui.net/docs/T_Avalonia_Media_TextFormatting_GlyphInfo)
  for exact typeface identity, UTF-16 source mapping, bidi level, glyph ID,
  cluster, advance and offset;
- [SixLabors.Fonts](https://github.com/SixLabors/Fonts) for managed font
  loading, OpenType layout and glyph outline callbacks;
- [OpenType 1.9.1](https://learn.microsoft.com/en-us/typography/opentype/spec/otff)
  for collections, variable fonts, vertical metrics and color glyph formats;
- [Unicode 17](https://www.unicode.org/versions/Unicode17.0.0/),
  [UAX #9](https://www.unicode.org/reports/tr9/),
  [UAX #14](https://www.unicode.org/reports/tr14/) and
  [UAX #29](https://www.unicode.org/reports/tr29/) for bidi, line-breaking and
  text-segmentation behavior.

Typography's separation between font reading/layout and rendering supports the
same ownership decision: DeltaText produces glyph data while the renderer owns
the visual backend and atlas.

## Current API boundary

Consumers use `Delta.Text.Contract.ITextService` and its immutable value
types. The former atlas-oriented implementation model is not part of the
current public surface.
