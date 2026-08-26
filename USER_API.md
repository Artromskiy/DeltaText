# Delta.Text user API

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

`GlyphImage.Pixels` is tightly packed, top-to-bottom, and owned by the returned
immutable image. DeltaText does not return atlas pages or UVs. The renderer
copies or consumes the image and owns all atlas/GPU lifetime.

The service currently supports coverage, grayscale SDF and MSDF images.
Color glyph images and MTSDF are explicit unsupported capabilities in this
implementation.
