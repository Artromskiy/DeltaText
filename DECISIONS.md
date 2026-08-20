# Delta.Text decisions

## Backend choice

The first implementation uses HarfBuzz for OpenType shaping. HarfBuzz handles
GSUB/GPOS, kerning, ligatures, combining marks, script shaping and bidi. The
project binds only the functions it needs through `NativeHarfBuzz.cs`; the
managed HarfBuzzSharp API is intentionally not part of the public surface.
The pinned `7.3.0.2` native-assets package is available offline in this
workspace and keeps the first build reproducible. Upgrade it only together
with the native ABI smoke tests.

The thin native boundary is important: `FontFace` owns the font blob and all
native handles, shaped output is copied once into stable managed arrays, and
future platform implementations can replace the backend without changing
Xamy or Rend contracts.

## Alternatives and boundaries

- SixLabors.Fonts is attractive because it is managed and includes shaping and
  metrics, but its current split license/commercial terms are not appropriate
  as an unreviewed runtime dependency.
- FreeType is a good outline/metrics source and can be used behind a future
  `IGlyphOutlineProvider`. Its FreeType License is permissive but has different
  notice requirements from MIT; it is not needed for this shaping slice.
- `msdfgen` is a good future CPU MSDF implementation behind
  `IGlyphAtlasGenerator`. It is MIT, but atlas generation is intentionally not
  implemented here and no GPU/Vulkan code belongs in Delta.Text.
- CoreText and DirectWrite belong in platform adapters. The core contracts use
  bytes/files and do not perform OS font discovery implicitly.

Primary references: [HarfBuzz](https://github.com/harfbuzz/harfbuzz),
[HarfBuzz license](https://github.com/harfbuzz/harfbuzz/blob/main/COPYING),
[FreeType license](https://github.com/freetype/freetype/blob/master/LICENSE.TXT),
[SixLabors.Fonts](https://github.com/SixLabors/Fonts), and
[msdfgen](https://github.com/Chlumsky/msdfgen).

## Test font

The tests bundle Noto Sans and Noto Sans Arabic under SIL OFL 1.1. They cover
Latin/Cyrillic, Arabic, combining marks, kerning and ligatures without relying
on an installed system font. The license is included beside the fixtures.
