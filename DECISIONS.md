# DeltaText decisions

The durable producer boundary is documented in
[PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md). Implementation details belong in
[INTERNAL.md](INTERNAL.md); this file remains as a navigation point for older
project links.

## Managed MSDF backend

The next MSDF backend will be implemented in managed C# and will consume the
contours already extracted by HarfBuzz. HarfBuzz remains the font and outline
source; it is not treated as an MSDF generator. The managed implementation is
the only MSDF backend and therefore has the same behavior on every supported
.NET platform.

The intended pipeline is:

```text
HarfBuzz draw callbacks
    -> immutable glyph contour model
    -> normalized line/quadratic/cubic edges
    -> deterministic edge coloring
    -> per-channel signed-distance rasterization
    -> tightly packed GlyphImage
```

The implementation is deliberately split into small internal stages rather
than one large renderer:

- `MsdfGeometry` converts contours into distance-queryable edges and assigns
  stable R/G/B channel masks at corners;
- `MsdfRasterizer` evaluates distances and winding without allocating per
  pixel;
- `MsdfEncoder` applies the distance range and writes the final byte payload.

The managed path must not create atlas pages, UVs or GPU resources. It owns the
MSDF generation path: curves are flattened to a bounded pixel tolerance,
corners receive deterministic channel colors, and a compact grid limits
distance candidates in the pixel loop. Parallelism is a later
measurement-driven step; correctness and deterministic output come first.

The managed backend is internal and returns a tightly packed RGB8 `GlyphImage`
representation. It has no C++ or native MSDF runtime dependency.
