# DeltaText

DeltaText is a renderer-neutral .NET text producer. It shapes UTF-16 text and
returns owned glyph images and metrics for CPU previews or a renderer.

## What it provides

- Immutable font instances and shaped glyph runs.
- Unicode-aware text shaping through the public text service.
- Grayscale coverage and SDF glyph images.
- Managed MSDF and color glyph-image modes where supported by the input font.
- Owned pixel snapshots and glyph metrics that can outlive the shaping call.
- A CPU renderer for producing an owned RGBA8 preview bitmap.

## Quick start

```xml
<PackageReference Include="DeltaText" Version="0.0.7" />
```

```csharp
using Delta.Text;
using Delta.Text.Contract;

using ITextService text = new SixLaborsTextService();
byte[] fontBytes = File.ReadAllBytes("font.ttf");
FontInstanceId font = text.OpenFont(new FontOpenRequest(
    "embedded-font", fontBytes, 0));
ShapedText shaped = text.Shape(new TextShapeRequest(
    "Hello".AsMemory(), 32f, new[] { font }));
CpuTextImage image = new CpuTextRenderer(text).Render(shaped);
```

The application owns the returned shaped text and pixel snapshots and disposes
font instances when they are no longer needed. Renderers decide atlas packing,
UVs, batching and GPU resource lifetime.

## Capabilities and limits

DeltaText targets .NET 8.0. It owns shaping and CPU glyph-image generation,
not GPU resources or renderer batching. The canonical contract defines the
supported image modes and fallback behavior; unsupported font formats use the
documented fallback rather than silently claiming full color support.

## Packages and examples

Install `DeltaText` for the producer API. See the [FontCheck
probe](probes/FontCheck/README.md) for a runnable headless example.

## Further reading

- [User API](USER_API.md)
- [Public contract](PUBLIC_CONTRACT.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)
