# Delta.Text

`Delta.Text` is the renderer-neutral CPU text layer for Furnace. It owns font
identity, OpenType shaping, glyph metrics, positioned glyph runs and the CPU
atlas contract that turns shaped glyphs into pixels. It does not know about
XAML, Vulkan, SDL, DeltaRender or shaders.

The first backend is a deliberately small P/Invoke surface over the native
HarfBuzz ABI. The managed HarfBuzzSharp object model is not exposed and is not
a runtime dependency. HarfBuzz native assets are selected by target platform in
`Delta.Text.csproj`.

```csharp
using Delta.Text;

using var face = FontFace.LoadFile(
    new FontKey("NotoSans-Regular", "regular", "fixture:noto-sans"),
    "NotoSans-Regular.ttf");

var request = new TextShapingRequest("office Привет", 24, CultureInfo.InvariantCulture);
var run = face.Shape(request);

foreach (var glyph in run.PositionedGlyphs.Span)
    SubmitGlyph(face.Key, glyph.GlyphId, glyph.Position, glyph.Advance);
```

Consumers pass positioned glyph runs to a renderer. They do not pass the
original string to a renderer. `GlyphAtlasRequest` is an explicit CPU atlas
boundary and `GlyphAtlasResult` carries page pixels plus glyph UVs, bounds,
bearing, advance and page index metadata.

The current atlas backend is a deterministic CPU grayscale signed-distance
fallback. `GlyphAtlasMode.Msdf` and `GlyphAtlasMode.Mtsdf` are declared in the
contract, but they are intentionally blocked until a native `msdfgen` backend
is verified on all supported platforms.

Export a Rend smoke fixture with:

```text
dotnet run --project Tests/Delta.Text.Tests.csproj -c Release -- --export-atlas-fixture <output-directory>
```

## Build and test

```text
dotnet build Delta.Text.csproj -c Release --no-restore
dotnet run --project Tests/Delta.Text.Tests.csproj -c Release
```

The tests use Noto Sans and Noto Sans Arabic fixtures under the SIL Open Font
License 1.1. See `THIRD-PARTY-NOTICES.md` and `DECISIONS.md`.
