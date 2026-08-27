# DeltaText user API

This document is the user-facing API guide. The normative cross-project
contract is [PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md); this page contains only
the practical entry-point summary.

Use `Delta.Text.Contract.ITextService`:

```csharp
using Delta.Text;
using Delta.Text.Contract;

using ITextService text = new HarfBuzzTextService();
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
returned result; atlas and cache reuse belongs to the renderer.

`GlyphImage.Pixels` is tightly packed, top-to-bottom, and owned by the returned
immutable image. DeltaText does not return atlas pages or UVs. The renderer
copies or consumes the image and owns all atlas/GPU lifetime.

The service supports coverage, grayscale SDF, MSDF and flattened RGBA color
images. Color images flatten COLR/CPAL version 0 layers with the requested
palette. COLR version 1 and SVG-in-OpenType glyphs use Skia's color glyph
renderer with the original glyph ID, preserving native layered paints and
embedded images when supported by the packaged runtime; an unsupported native
paint falls back to the foreground-colored outline. MTSDF remains an
explicitly unsupported representation.
