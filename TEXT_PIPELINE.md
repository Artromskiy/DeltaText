# Delta.Text pipeline boundary

Delta.Text exposes three CPU-side stages:

1. `TextShaper.Shape` turns a `TextShapingRequest` and `FontFace` into an immutable
   `ShapedGlyphRun`. HarfBuzz owns script, direction, cluster, ligature and
   positioning decisions.
2. `IGlyphBitmapGenerator.TryGenerateGlyph` turns one glyph into a `GlyphBitmap`.
   Grayscale and MSDF are supported; MTSDF returns `UnsupportedMode` explicitly.
   The bitmap is an unpacked CPU result with metrics and pixels, not a GPU page.
3. `GlyphRenderData` joins positioned glyphs with their bitmaps for the renderer.
   DeltaRender packs these results into its own pages and assigns UVs, lifetime
   and GPU handles. Delta.Text does not own pages, UVs or GPU resources.

`TextShaper` and `GlyphAtlasGenerator` accept `TextCacheBudget`. Each uses a
separate bounded LRU cache. Entries are evicted by monotonically increasing
access order, with a byte and entry limit. Generation is serialized per cache,
so sequential requests have deterministic eviction. The legacy
`IGlyphAtlasGenerator`/`GlyphAtlasResult` path remains available for migration;
new renderer integrations should use the three stages above and avoid retaining
both packed pages and individual bitmaps.

Xamy should retain the shaped run and pass its positioned glyphs forward. Rend
should request/cache or immediately pack the returned `GlyphBitmap` values and
own atlas page layout, UVs and GPU upload policy.
