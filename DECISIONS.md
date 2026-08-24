# Delta.Text decisions

## Public producer boundary

The authoritative v1 API is `Delta.Text.Contract.ITextService`. DeltaText produces
immutable shaped runs and independent, tightly packed glyph images. It does
not produce an atlas. Exact font instances are opaque generation-safe handles;
their identity includes source bytes, collection face index and variable-font
coordinates.

The contract intentionally preserves UTF-16 source ranges, clusters, resolved
font fallback, bidirectional level, horizontal and vertical advances, offsets
and HarfBuzz glyph safety flags. OpenType feature values are unsigned integers
with optional source ranges rather than Boolean toggles.

Atlas pages, UV rectangles, pipelines, batches, cache policy, eviction and GPU
upload are consumer concerns. Bitmap rows returned by DeltaText are tightly
packed, so renderer-specific row pitch is not part of the public API. See
[PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md) for the normative contract.

## Shaping and outlines

HarfBuzz is the first OpenType backend. A small owned P/Invoke surface performs
GSUB/GPOS shaping, metrics and glyph outline extraction through the HarfBuzz
draw API. The managed HarfBuzzSharp object model is not public runtime API.
`FontFace` owns font bytes and native handles; shaped output is copied once into
stable managed arrays.

## Distance fields

Grayscale SDF remains the cheap fallback. DeltaText vendors only the minimal
msdfgen core and owns a narrow C ABI that accepts HarfBuzz contours. MSDF is
enabled when the target native bridge is present; it emits deterministic RGB8
pixels and copies then frees the native allocation immediately. MTSDF remains
outside the current contract.

SkiaSharp remains an implementation detail of the existing grayscale fallback.
FreeType is deliberately not an engine dependency. Future CoreText or
DirectWrite adapters must remain behind the same renderer-neutral contracts.

## Fixtures and licenses

Tests bundle Noto Sans and Noto Sans Arabic under SIL OFL 1.1 for
Latin/Cyrillic, Arabic, combining marks, kerning and ligatures. Dependency
versions and notices are recorded in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
