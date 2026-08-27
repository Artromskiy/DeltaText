# DeltaText agent guide

Scope: renderer-neutral font identity, HarfBuzz shaping/outlines, positioned
glyphs and CPU SDF/MSDF generation. It owns no XAML, Vulkan, SDL or shaders.

- [README.md](README.md) — short project overview and navigation.
- [PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md) — authoritative v1 data model, ownership,
  JSON shape and standards boundary.
- [TODO.md](TODO.md) — selected text work.
- [IDEAS.md](IDEAS.md) — deferred backend/cache options.
- [WORKFLOW.md](WORKFLOW.md) — managed/native build, tests and fixture export.
- [DECISIONS.md](DECISIONS.md) — backend and ownership decisions.
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) — required legal metadata.
- [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md) — shared editor acceptance.
- [../HIGH_PRIORITY_TODO.md](../HIGH_PRIORITY_TODO.md) — text/render ownership
  and migration order.

MSDF is implemented in managed C#; the project has no native MSDF bridge or
vendored font rasterizer. FreeType is not an engine dependency.

Skills: `abi-and-calling-conventions` for the HarfBuzz native boundary,
`performance-speedup` for shaping/cache workloads, and `static-analysis` for
native lifetime and ownership review.
