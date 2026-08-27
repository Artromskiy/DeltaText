# Third-party notices

## SixLabors.Fonts

DeltaText uses `SixLabors.Fonts` version `3.0.0` for font loading, OpenType
layout and outline callbacks. It is licensed under the Six Labors Split
License, version 1.0, June 2022. The applicable license file is supplied to
the local build through `SixLaborsLicenseFile`; it is intentionally kept
outside Git at `Furnace/Licenses/SixLabors.lic`. The package's complete notice
is part of the NuGet package.

DeltaText does not depend on ImageSharp, SkiaSharp, HarfBuzz native assets or
FreeType. Its pixel rasterization is its own managed implementation.

## Noto test fonts

`Tests/Fixtures/NotoSans-Regular.ttf` and
`Tests/Fixtures/NotoSansArabic-Regular.ttf` are Noto fonts distributed under
the SIL Open Font License 1.1. The complete license is
`Tests/Fixtures/OFL.txt`. Source: https://github.com/notofonts/noto-fonts.
