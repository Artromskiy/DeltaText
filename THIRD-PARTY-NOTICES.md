# Third-party notices

## SixLabors.Fonts

DeltaText uses the locally built package `SixLabors.Fonts` version
`3.1.0-fork.cadda774`, built from
`https://github.com/Artromskiy/Fonts` commit
`cadda774b743472e4186e96c8d779a8419276f98` on branch
`fix-cff-igrunok-outline`. It supplies font loading, OpenType layout and
outline callbacks. It is licensed under the Six Labors Split License, version
1.0, June 2022. The applicable license file is supplied to the local build
through `SixLaborsLicenseFile`; it is intentionally kept outside Git at
`Furnace/Licenses/SixLabors.lic`. The package's complete notice is part of the
local package.

DeltaText does not depend on ImageSharp, SkiaSharp, HarfBuzz native assets or
FreeType. Its pixel rasterization is its own managed implementation.

## UniText UAX #14 reference implementation

`UnicodeLineBreakEngine.cs` is an adapted, dependency-free implementation of
the UAX #14 rule flow from UniText 1.0.0 by Light Side LLC. The original
source is available at https://github.com/Light-Side-LLC/UniText and is
licensed under the MIT License. The adaptation retains the MIT copyright and
permission notice below; it uses SixLabors public Unicode property APIs and
does not include UniText runtime code or third-party dependencies.

```text
MIT License

Copyright (c) 2026 Light Side LLC (https://unity.lightside.media)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Noto test fonts

`tests/DeltaText.Tests/Fixtures/NotoSans-Regular.ttf` and
`tests/DeltaText.Tests/Fixtures/NotoSansArabic-Regular.ttf` are Noto fonts distributed under
the SIL Open Font License 1.1. The complete license is
`tests/DeltaText.Tests/Fixtures/OFL.txt`. Source: https://github.com/notofonts/noto-fonts.
