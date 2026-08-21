# Delta.Text decisions

## Shaping and outlines

HarfBuzz is the first OpenType backend. A small owned P/Invoke surface performs
GSUB/GPOS shaping, metrics and glyph outline extraction through the HarfBuzz
draw API. The managed HarfBuzzSharp object model is not public runtime API.
`FontFace` owns font bytes and native handles; shaped output is copied once into
stable managed arrays.

## Distance fields

Grayscale SDF remains the cheap fallback. DeltaText vendors only the minimal
msdfgen core and owns a narrow C ABI that accepts HarfBuzz contours. MSDF is not
enabled as production output until native build, allocation/free and contour
correctness smokes pass on every supported platform.

SkiaSharp remains an implementation detail of the existing grayscale fallback.
FreeType is deliberately not an engine dependency. Future CoreText or
DirectWrite adapters must remain behind the same renderer-neutral contracts.

## Fixtures and licenses

Tests bundle Noto Sans and Noto Sans Arabic under SIL OFL 1.1 for
Latin/Cyrillic, Arabic, combining marks, kerning and ligatures. Dependency
versions and notices are recorded in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
