# DeltaText TODO

- Finish platform packaging and CI ABI/lifetime smokes for the HarfBuzz
  draw-contour -> native msdfgen -> managed bitmap route.
- Keep grayscale SDF as the cheap fallback and make glyph outputs cacheable by
  font identity, glyph ID, size, mode and distance-field parameters.
- Expand the deterministic MSDF fixture export to Latin/Cyrillic/Arabic,
  combining marks, ligatures, sharp corners and two DPI scales for DeltaRender.

Cross-project acceptance is in [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md).
