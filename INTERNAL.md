# DeltaText internal implementation

This document is internal and is not a consumer API.

`HarfBuzzTextService` owns a synchronized map of generation-safe
`FontInstanceId` values to `FontFace` objects. `FontFace` owns a private copy of
font bytes and the HarfBuzz blob, face and font handles until `CloseFont` or
service disposal. No native handle crosses `Delta.Text.Contract`.

HarfBuzz is accessed through the small P/Invoke surface in
`NativeHarfBuzz.cs` and `NativeHarfBuzzOutline.cs`. Variable-font coordinates
are passed to HarfBuzz as OpenType axis strings. Fallback is resolved into
contiguous font-specific runs before shaping; shaping output is copied into
contract-owned arrays.

`OpenFont` owns one defensive copy of the caller's font bytes. Each successful
`Shape` call creates a new run/glyph snapshot, and each successful image call
creates a new pixel snapshot; these are deliberate boundary allocations, not
service-owned reusable buffers. No mutable list, pinned managed array or native
pixel allocation is exposed to a consumer.

Coverage and SDF rasterization use SkiaSharp internally. MSDF consumes the
HarfBuzz outline callbacks, flattens curves to a bounded pixel tolerance and
generates deterministic RGB8 pixels in managed code. Its geometric values use
`Delta.Maths.float2` and `Delta.Maths.DeltaMaths`, so DeltaText has no direct
`System.Numerics` dependency. The resolver first uses the default OS loader and
then checks the assembly directory and current RID runtime asset paths for the
HarfBuzz asset only. MSDF has no native runtime dependency.

The implementation deliberately owns no atlas pages, UV coordinates, staging
buffers, batching keys or GPU resources. Consumer adapters must not re-expose
these implementation details through a second public text API.
