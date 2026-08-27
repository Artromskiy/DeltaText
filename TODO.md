# DeltaText TODO

- Keep `Delta.Text.Contract.ITextService` as the only public producer entry
  point.
- Keep deterministic managed MSDF coverage for SixLabors.Fonts outlines and
  glyph image generation on every supported .NET platform.
- If a producer-side cache is introduced later, keep its policy internal and
  key it by exact font instance, glyph ID, size, image mode and distance-field
  parameters.
- Expand deterministic glyph-image fixture coverage to Latin/Cyrillic/Arabic,
  combining marks, ligatures, sharp corners and two DPI scales for DeltaRender.
- Keep MTSDF outside the current v1 capability set. Any future extension must
  update [`PUBLIC_CONTRACT.md`](PUBLIC_CONTRACT.md) first.

Ownership order is in
[../HIGH_PRIORITY_TODO.md](../HIGH_PRIORITY_TODO.md); cross-project acceptance
is in [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md). The public API is defined
only by [`PUBLIC_CONTRACT.md`](PUBLIC_CONTRACT.md).
