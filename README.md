# Delta.Text

Renderer-neutral CPU text layer for Furnace. It owns font identity, OpenType
shaping, glyph metrics/outlines, positioned glyph runs and CPU glyph bitmap
generation. It has no XAML, Vulkan, SDL, DeltaRender or shader dependency.

The backend uses a small owned P/Invoke surface over HarfBuzz for shaping and
glyph outlines. Consumers pass positioned glyphs to the renderer, never the
original string. `GlyphAtlasRequest` and `GlyphAtlasResult` form the CPU atlas
boundary and carry pixels, UVs, bounds, bearings, advances and page metadata.

```csharp
using System.Globalization;
using Delta.Text;

var key = new FontKey("NotoSans-Regular", "regular", "fixture:noto-sans");
using var face = FontFace.LoadFile(key, "NotoSans-Regular.ttf");
var run = face.Shape(new TextShapingRequest(
    "office Привет", 24, CultureInfo.InvariantCulture));
foreach (var glyph in run.PositionedGlyphs.Span)
    SubmitGlyph(face.Key, glyph);
```

Grayscale SDF is the fallback. The native msdfgen bridge accepts contours from
HarfBuzz and remains gated until ABI/lifetime smokes pass on supported targets.
FreeType is not an engine dependency.

See [DECISIONS.md](DECISIONS.md) for backend choices,
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for licenses,
[WORKFLOW.md](WORKFLOW.md) for commands and [TODO.md](TODO.md) for selected work.
