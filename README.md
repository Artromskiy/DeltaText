# Delta.Text

`Delta.Text` is the renderer-neutral CPU text layer for Furnace. It owns font
identity, OpenType shaping, glyph metrics and positioned glyph runs. It does
not know about XAML, Vulkan, SDL, DeltaRender or shaders.

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

Consumers pass `PositionedGlyphRun` data to a renderer. They do not pass the
original string to a renderer. `GlyphAtlasRequest` is an explicit CPU atlas
boundary; no atlas generator or GPU upload is part of this project yet.

## Build and test

```text
dotnet build Delta.Text.csproj -c Release --no-restore
dotnet run --project Tests/Delta.Text.Tests.csproj -c Release
```

The tests use Noto Sans and Noto Sans Arabic fixtures under the SIL Open Font
License 1.1. See `THIRD-PARTY-NOTICES.md` and `DECISIONS.md`.
