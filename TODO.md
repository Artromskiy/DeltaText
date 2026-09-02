# DeltaText TODO

- Keep `Delta.Text.Contract.ITextService` as the only public producer entry
  point.
- Keep deterministic managed MSDF coverage for SixLabors.Fonts outlines and
  glyph image generation on every supported .NET platform.
- Keep the producer-side glyph-image cache bounded and internal; key it by
  exact font instance, glyph ID, size, image mode, color and distance-field
  parameters. Revisit its limits only with representative measurements.
- Keep MTSDF outside the current v1 capability set. Any future extension must
  update [`PUBLIC_CONTRACT.md`](PUBLIC_CONTRACT.md) first.

Ownership and cross-project acceptance are in [../CONTRACTS.md](../CONTRACTS.md).
The public API is defined only by [`PUBLIC_CONTRACT.md`](PUBLIC_CONTRACT.md).
