# Delta.Text pipeline boundary

Delta.Text exposes three CPU-side stages:

1. `TextShaper.Shape` turns a `TextShapingRequest` and `FontFace` into an immutable
   `ShapedGlyphRun`. HarfBuzz owns script, direction, cluster, ligature and
   positioning decisions.
2. `IGlyphBitmapGenerator.TryGenerateGlyph` turns one glyph into a `GlyphBitmap`.
   Grayscale and MSDF are supported; MTSDF returns `UnsupportedMode` explicitly.
   The bitmap is an unpacked CPU result with metrics and pixels, not a GPU page.
   Its immutable contract is `GlyphId`, `Width`, `Height`, byte `Stride`,
   `BearingX`, `BearingY`, `AdvanceX`, `Request.Mode` and `Pixels`. External
   producers use `GlyphBitmap.Create(GlyphAtlasRequest, uint, int, int, int,
   float, float, float, ReadOnlyMemory<byte>)`; the factory validates format
   stride/length and finite metrics, and copies the source memory. `Pixels` has
   `Height * Stride` bytes and remains valid while the `GlyphBitmap` is alive;
   grayscale uses one byte per pixel and MSDF uses three bytes per pixel. No
   HarfBuzz, font-face or native handle crosses this boundary.
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

The XAML/UI layer must not retain `ShapedGlyphRun` as its own text model. The
renderer-neutral adapter (for example the Editor.UiHost integration) consumes
the XAML text request, invokes shaping, and passes the resulting
`GlyphRenderData` to Rend. `GlyphRenderData.Run` and each
`PositionedGlyphBitmap` are immutable handoff values; Rend may consume them
immediately or copy the needed metrics/pixels into its own atlas cache.

Rend should request/cache or immediately pack the returned `GlyphBitmap` values
and own atlas page layout, UVs, GPU upload policy and GPU lifetime. Delta.Text
owns only the managed bitmap/run objects and its internal bounded caches; cache
keys and mutable cache storage are not part of the consumer contract.
