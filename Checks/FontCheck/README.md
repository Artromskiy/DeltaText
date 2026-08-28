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

ImageSharp is not a font renderer. Keeping it in this check project makes the
reference path independent without adding an ImageSharp dependency to the
DeltaText runtime.

The local fixture manifest is [`fonts.json`](Fixtures/fonts.json). Doto and
Luckiest Guy are stored in `Fixtures/Fonts` with their license notices and
verified by SHA-256. Noto fixtures are linked from `Tests/Fixtures` and use the
same OFL notice. These binary fixtures are intentionally local check inputs,
not runtime assets.

Run from the DeltaText repository:

```bash
SixLaborsLicenseFile=/Users/rum/GitProjects/TheFurnace/Furnace/Licenses/SixLabors.lic \
dotnet run --project Checks/FontCheck/FontCheck.csproj -c Release -- \
  --bidi-corpus Checks/FontCheck/Fixtures/BidiCharacterTest.txt \
  --bidi-test Checks/FontCheck/Fixtures/BidiTest.txt \
  --bidi-brackets Checks/FontCheck/Fixtures/BidiBrackets.txt
```

The full UAX #9 corpora are bounded conformance checks, not performance
benchmarks. Line breaking, grapheme segmentation and paragraph layout belong
to the layout consumer; they require their own contracts and are not silently
claimed by `ITextService`. The official corpus text files are local check
inputs and are intentionally not committed; pass their paths explicitly as
shown above.
