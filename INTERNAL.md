# DeltaText internal implementation

This document is internal and is not a consumer API.

`SixLaborsTextService` owns a synchronized map of generation-safe
`FontInstanceId` values to `FontFace` objects. `FontFace` owns a private copy of
font bytes and the SixLabors.Fonts collection/face objects until `CloseFont` or
service disposal. No package object or native handle crosses
`Delta.Text.Contract`. The implementation entry point is
`SixLaborsTextService`; no font-backend implementation type is part of the
cross-project contract.

SixLabors.Fonts performs OpenType layout, fallback selection and outline
callbacks. Bidi formatting controls are removed from the layout metrics before
they are paired with renderer callbacks, because controls have source mapping
but no rendered glyph. Fallback identity comes from each returned
`GlyphMetrics.Font`, not from the enclosing text run. Shaping output is copied
into contract-owned arrays.

The adapter passes globally enabled Boolean feature tags to
`TextOptions.FeatureTags` and maps `kern=0` to `KerningMode.None`. SixLabors
3.0.0 has no public script/language selector, ranged feature API, arbitrary
feature values or color-palette selector, so those requests are rejected at
the boundary rather than being silently dropped.

Safety metadata is derived conservatively from the source cluster shape:
multi-scalar and combining clusters, plus Arabic joining contexts, receive
`UnsafeToBreak | UnsafeToConcat`. No `SafeToInsertTatweel` claim is made.

`OpenFont` owns one defensive copy of the caller's font bytes. Each successful
`Shape` call creates a new run/glyph snapshot, and each successful image call
creates a new pixel snapshot; these are deliberate boundary allocations, not
service-owned reusable buffers. No mutable list, pinned managed array or native
pixel allocation is exposed to a consumer.

Coverage, SDF, MSDF and color rasterization are all managed. MSDF consumes the
SixLabors.Fonts outline callbacks, flattens curves to a bounded pixel tolerance
and generates deterministic RGB8 pixels. Its geometric values use
`Delta.Maths.float2` and `Delta.Maths.DeltaMaths`; the only
`System.Numerics.Vector2` reference is the private callback adapter required by
SixLabors.Fonts. ImageSharp, SkiaSharp, FreeType and native MSDF assets are not
runtime dependencies.

`UnicodeBidiData` is generated from the pinned Unicode 16.0 bidi properties.
The resolver applies UAX #9 explicit, weak, neutral, implicit and reorder
stages over that table; isolates remain boundaries until formatting controls
are removed, and explicit-level overflow is tracked separately from valid
embedding/isolate stack entries.

Color layers exposed by SixLabors.Fonts are flattened by
`ManagedGlyphRasterizer` into the contract's owned RGBA snapshot. A glyph
format that cannot be exposed as outline callbacks uses the foreground-colored
outline fallback. No SixLabors object, mutable contour list or font handle
crosses the contract.

The implementation deliberately owns no atlas pages, UV coordinates, staging
buffers, batching keys or GPU resources. Consumer adapters must not re-expose
these implementation details through a second public text API.
