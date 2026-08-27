# DeltaText TODO

- Keep `Delta.Text.Contract.ITextService` as the only public producer entry
  point.
- Verify that the platform packaging matrix has real artifacts and
  success/failure disposal coverage for the HarfBuzz outline -> native msdfgen
  -> managed glyph-image route. Documentation alone is not acceptance.
- Replace the native MSDF implementation with the staged managed backend
  described in [`DECISIONS.md`](DECISIONS.md), retaining native MSDF as a
  temporary parity fallback until deterministic pixel and quality checks pass.
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
