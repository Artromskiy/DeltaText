# DeltaText decisions

The durable producer boundary is documented in
[PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md). Implementation details belong in
[INTERNAL.md](INTERNAL.md); this file remains as a navigation point for older
project links.

## Managed MSDF backend

The next MSDF backend will be implemented in managed C# and will consume the
contours already extracted by HarfBuzz. HarfBuzz remains the font and outline
source; it is not treated as an MSDF generator. The current native `msdfgen`
path remains in place until the managed implementation passes the same
geometry, pixel and determinism checks.

The intended pipeline is:

```text
HarfBuzz draw callbacks
    -> immutable glyph contour model
    -> normalized line/quadratic/cubic edges
    -> deterministic edge coloring
    -> per-channel signed-distance rasterization
    -> median/error correction
    -> tightly packed GlyphImage
```

The implementation is deliberately split into small internal stages rather
than one large renderer:

- `MsdfGeometry` converts contours into distance-queryable edges;
- `MsdfEdgeColoring` assigns stable R/G/B channel masks at corners;
- `MsdfRasterizer` evaluates distances and winding without allocating per
  pixel;
- `MsdfEncoder` applies the distance range and writes the final byte payload.

The managed path must not create atlas pages, UVs or GPU resources. To keep it
readable and fast, the hot loop will use flat reusable work buffers, reject
pixels outside the glyph bounds early, avoid LINQ/iterator allocations and
use a bounded spatial index for edge candidates. Parallelism is a later
measurement-driven step; correctness and deterministic output come first.

This is an implementation decision and a selected direction, not a claim that
the managed backend is already available. `NativeMsdf` is still the active
MSDF implementation until that work is completed.
