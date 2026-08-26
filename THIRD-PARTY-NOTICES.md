# Third-party notices

## HarfBuzz native assets

DeltaText uses the native HarfBuzz library distributed by
`HarfBuzzSharp.NativeAssets.*` version `7.3.0.2`. HarfBuzz is available under
the Old MIT license. The package's complete license and third-party notice are
distributed with the package; the upstream sources are at
https://github.com/harfbuzz/harfbuzz.

## SkiaSharp

DeltaText uses `SkiaSharp` version `2.88.8` and the matching platform native
assets for the implementation's CPU glyph raster path. SkiaSharp remains an
internal dependency and is not part of the public `DeltaText.Contract` API.
SkiaSharp is distributed under the MIT license and includes its own
third-party notices.
Upstream sources are at https://github.com/mono/SkiaSharp.

## Noto test fonts

`Tests/Fixtures/NotoSans-Regular.ttf` and
`Tests/Fixtures/NotoSansArabic-Regular.ttf` are Noto fonts distributed under
the SIL Open Font License 1.1. The complete license is
`Tests/Fixtures/OFL.txt`. Source: https://github.com/notofonts/noto-fonts.

## msdfgen core

DeltaText vendors only the `core/` source set from msdfgen v1.13, commit
`1874bcf7d9624ccc85b4bc9a85d78116f690f35b`, from
https://github.com/Chlumsky/msdfgen. Font-import and extension sources are
excluded; DeltaText has no FreeType dependency. msdfgen is licensed under
the MIT license; the upstream license is retained at
`third_party/msdfgen/LICENSE.txt`.
