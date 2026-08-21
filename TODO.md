# DeltaText TODO

- Finish the HarfBuzz draw-contour -> native msdfgen -> managed bitmap route and
  enable `GlyphAtlasMode.Msdf` only after ABI/lifetime smokes pass on supported
  targets.
- Keep grayscale SDF as the cheap fallback and make glyph outputs cacheable by
  font identity, glyph ID, size, mode and distance-field parameters.
- Cover Latin/Cyrillic/Arabic, combining marks, ligatures, sharp corners and two
  DPI scales; export a deterministic fixture for DeltaRender.

Cross-project acceptance is in [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md).
