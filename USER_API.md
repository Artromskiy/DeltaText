# DeltaText user API

This document is the user-facing API guide. The normative cross-project
contract is [PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md); this page contains only
the practical entry-point summary.

Use `Delta.Text.Contract.ITextService`:

```csharp
using Delta.Text;
using Delta.Text.Contract;

using ITextService text = new SixLaborsTextService();
var font = text.OpenFont(new FontOpenRequest(sourceId, fontBytes, faceIndex));
var shaped = text.Shape(new TextShapeRequest(
    "office Привет".AsMemory(),
    24,
    new[] { font }));
var glyph = shaped.Runs.Span[0].Glyphs.Span[0];
var image = text.GenerateGlyphImage(new GlyphImageRequest(
    font,
    glyph.GlyphId,
    48,
    GlyphImageMode.Msdf,
    4));
text.CloseFont(font);
```

`OpenFont` copies the supplied font bytes. `Shape` and `GenerateGlyphImage`
return owned snapshots: their arrays and pixel payloads may be retained by the
caller after the method returns, but are not reusable views into the service or
its native buffers. The snapshot boundary intentionally allocates for the
returned result; the service may cache those results internally, while
consumer-facing shaped-result and atlas reuse belongs to the renderer.

`GlyphImage.Pixels` is tightly packed, top-to-bottom, and owned by the returned
immutable image. DeltaText does not return atlas pages or UVs. The renderer
copies or consumes the image and owns all atlas/GPU lifetime.

For a CPU preview or headless export, compose the same shaped output with the
optional `CpuTextRenderer`:

```csharp
var cpu = new CpuTextRenderer(text);
var bitmap = cpu.Render(
    new TextShapeRequest("Delta".AsMemory(), 32, new[] { font }),
    new CpuTextRenderOptions(
        GlyphImageMode.Msdf,
        4,
        new Rgba32(240, 240, 240, 255)));
```

When the text and shaping inputs do not change, retain the `ShapedText` result
and render it without shaping again:

```csharp
var shaped = text.Shape(request);
var bitmap = cpu.Render(shaped, new CpuTextRenderOptions(
    GlyphImageMode.Coverage,
    0,
    new Rgba32(240, 240, 240, 255)));
```

`CpuTextImage.Pixels` is an owned, tightly packed, top-to-bottom premultiplied
RGBA8 snapshot. `CpuTextImage.Bounds` is relative to the text baseline and
describes the returned bitmap; transparent pixels outside glyphs are included
inside that rectangle. The helper borrows `ITextService` and font instances
only for the duration of `Render`; it does not own fonts, caches, atlases or
GPU resources. SDF and MSDF are decoded with a half-pixel CPU antialiasing
transition using the image's distance range.

The service supports coverage, grayscale SDF, MSDF and flattened RGBA color
images. SixLabors.Fonts supplies the font and outline data; DeltaText performs
the rasterization and owns the returned pixels. Color glyph layers exposed by
SixLabors.Fonts are flattened into the same owned RGBA image. If a format does
not expose an outline through the package, the requested color image uses the
documented foreground-outline fallback. MTSDF remains an explicitly
unsupported representation.

Unicode boundaries are available independently of fonts and shaping:

```csharp
var clusters = UnicodeText.SegmentGraphemes("Café 👩🏽‍💻".AsMemory());
var breaks = UnicodeText.GetLineBreaks("Hello world".AsMemory());
```

Both results own their arrays and use offsets into the original UTF-16 input.
The grapheme map reports extended grapheme clusters; the line-break map reports
UAX #14 opportunities. These APIs do not normalize text or perform
width-dependent line layout, so the consumer remains responsible for measuring
and placing lines.

With the bundled SixLabors.Fonts fork build (`3.1.0-fork.cadda774`), leave
script and language at automatic inference. Globally enabled Boolean feature
tags are supported;
ranged features, feature values above one, explicit script/language selectors
and non-default color palettes fail explicitly with `NotSupportedException`.
The contract contains vertical direction values, but this implementation currently
maps them to automatic horizontal shaping; use `Auto`, `LeftToRight` or
`RightToLeft` for supported layout behavior.
