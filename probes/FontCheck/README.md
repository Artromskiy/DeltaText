# FontCheck

`FontCheck` is a headless, reproducible check for the visible DeltaText
pipeline. It is intentionally separate from the library and from the test
runner: its output is a fixture report and inspectable bitmaps at multiple
sizes.

The check covers four different questions:

1. `BidiCharacterTest.txt` and `BidiTest.txt` exercise the Unicode 17 UAX #9
   paragraph levels and visual order. `BidiBrackets.txt` checks the normative
   paired-bracket table used by rule N0.
2. Shaping checks source ranges, clusters, glyph counts and deterministic SDF
   output for Doto (64/128 px) and Luckiest Guy (48/96 px).
3. A Noto Sans/Noto Sans Arabic fallback chain checks Unicode render probes for Latin, Cyrillic, Arabic,
   Hebrew, Indic, Thai, CJK, combining marks, emoji, mixed-direction text,
   controls and unsupported/noncharacter inputs. Valid probes validate grapheme
   and line-break coverage, shaped clusters/metrics, and all four image modes;
   noncharacters are checked against the producer's documented rejection
   contract.
4. The CPU renderer checks that the same shaped output composes into a stable
   owned RGBA bitmap, including whitespace glyphs.
5. An independent oracle receives the same font outline callbacks, rasterizes
   coverage with its own fixed supersampling path, and uses ImageSharp as the
   bitmap surface/PNG encoder. Both sizes of both fonts are compared. The
   comparison allows `32/255` mean alpha error and `224/255` 95th-percentile
   edge error, with no more than one pixel of alignment offset. A larger
   translation or missing glyph fails the geometry/coverage comparison. An
   alignment preview is emitted alongside the actual and reference PNGs.

6. On macOS, a large native conformance corpus also sends the glyph IDs and
   baseline positions produced by DeltaText to the system CoreText/CoreGraphics
   rasterizer. It compares the resulting premultiplied RGBA8 bytes exactly and
   compares alpha coverage against both the DeltaText result and the ImageSharp
   callback reference. The native path intentionally does not shape text a
   second time: this isolates rasterization, placement and pixel-format
   differences. CoreText's antialiasing is not expected to be byte-identical to
   the managed sampler; the report records exact mismatch count, MAE, P95 and
   first-mismatch PNGs rather than hiding that difference. The default corpus
   contains 2048 deterministic strings at 24/32/48/64 px.

ImageSharp is not a font renderer. Keeping it in this check project makes the
reference path independent without adding an ImageSharp dependency to the
DeltaText runtime.

The local fixture manifest is [`fonts.json`](Fixtures/fonts.json). Doto and
Luckiest Guy are stored in `Fixtures/Fonts` with their license notices and
verified by SHA-256. Noto fixtures are linked from `tests/DeltaText.Tests/Fixtures` and use the
same OFL notice. These binary fixtures are intentionally local check inputs,
not runtime assets.

For a focused shaping regression with the locally downloaded Igrunok font,
run the shaping-only mode. It requires one LTR glyph for `R`, then compares the
complete run/glyph snapshot across 100 repeated calls and a fresh service
instance. The JSON report contains the font hash, glyph ID, UTF-16 cluster,
advance and offset values. Add `--render-png` to also save a CoreText image and
an independent ImageSharp reference image from the same shaped result.

```bash
SixLaborsLicenseFile=/Users/rum/GitProjects/TheFurnace/Furnace/Licenses/SixLabors.lic \
dotnet run --project probes/FontCheck/FontCheck.csproj -c Release -- \
  --shape-only \
  --render-png \
  --font /Users/rum/Downloads/Igrunok-SP/IgrunokSPDemo-Black.otf \
  --pixels-per-em 70 \
  --output /tmp/delta-text-igrunok-shape
```

The Igrunok font remains a caller-provided local input; it is not copied into
the repository by this check.

Run from the DeltaText repository:

```bash
SixLaborsLicenseFile=/Users/rum/GitProjects/TheFurnace/Furnace/Licenses/SixLabors.lic \
dotnet run --project probes/FontCheck/FontCheck.csproj -c Release -- \
  --bidi-corpus probes/FontCheck/Fixtures/BidiCharacterTest.txt \
  --bidi-test probes/FontCheck/Fixtures/BidiTest.txt \
  --bidi-brackets probes/FontCheck/Fixtures/BidiBrackets.txt \
  --native-corpus-count 2048
```

The native corpus is macOS-only because it uses the system CoreText and
CoreGraphics frameworks. On another platform it is reported as skipped; use
`--skip-native` to make that choice explicit. Its output is written to
`artifacts/native-conformance`, including the first DeltaText/CoreText/
ImageSharp mismatch and its metadata. The native comparison consumes the
already-shaped glyph sequence, so it is not a second UAX #9 or OpenType
shaping implementation.

The full UAX #9 corpora are bounded conformance checks, not performance
benchmarks. Line breaking, grapheme segmentation and paragraph layout belong
to the layout consumer; they require their own contracts and are not silently
claimed by `ITextService`. The official corpus text files are local check
inputs and are intentionally not committed; pass their paths explicitly as
shown above.
