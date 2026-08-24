# DeltaText TODO

- Complete the implementation migration to `Delta.Text.Contract.ITextService`;
  the legacy atlas/cache types remain migration-only.
- Verify that the platform packaging matrix has real artifacts and
  success/failure disposal coverage for the HarfBuzz outline -> native msdfgen
  -> managed glyph-image route. Documentation alone is not acceptance.
- Keep internal glyph caching bounded and keyed by the exact font instance,
  glyph ID, size, image mode and distance-field parameters. Cache policy is not
  part of the public contract.
- Expand deterministic glyph-image fixture coverage to Latin/Cyrillic/Arabic,
  combining marks, ligatures, sharp corners and two DPI scales for DeltaRender.
- Keep MTSDF outside the current v1 capability set. Any future extension must
  update [`PUBLIC_CONTRACT.md`](PUBLIC_CONTRACT.md) first.

Ownership order is in
[../HIGH_PRIORITY_TODO.md](../HIGH_PRIORITY_TODO.md); cross-project acceptance
is in [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md). The public API is defined
only by [`PUBLIC_CONTRACT.md`](PUBLIC_CONTRACT.md).
