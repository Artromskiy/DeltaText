# DeltaText TODO

- Verify that the documented platform matrix has real packaging artifacts and
  success/failure disposal coverage for the HarfBuzz draw-contour -> native
  msdfgen -> managed bitmap route; documentation alone is not acceptance.
- Keep grayscale SDF as the cheap fallback and make glyph outputs cacheable by
  font identity, glyph ID, size, mode and distance-field parameters.
- Expand the deterministic MSDF fixture export to Latin/Cyrillic/Arabic,
  combining marks, ligatures, sharp corners and two DPI scales for DeltaRender.
- Keep MTSDF explicitly unsupported. Treat CPU `GlyphAtlasResult` as a
  migration contract while DeltaRender assumes atlas packing/UV/GPU ownership.

Ownership order is in
[../HIGH_PRIORITY_TODO.md](../HIGH_PRIORITY_TODO.md); cross-project acceptance
is in [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md).
