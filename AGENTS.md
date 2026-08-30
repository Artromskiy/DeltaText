# DeltaText agent guide

Scope: renderer-neutral font identity, SixLabors.Fonts shaping/outlines,
positioned glyphs and CPU SDF/MSDF generation. It owns no XAML, Vulkan, SDL or
shaders.

- [README.md](README.md) — short project overview and navigation.
- [PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md) — authoritative v1 data model, ownership,
  JSON shape and standards boundary.
- [TODO.md](TODO.md) — selected text work.
- [IDEAS.md](IDEAS.md) — deferred backend/cache options.
- [WORKFLOW.md](WORKFLOW.md) — managed/native build, tests and fixture export.
- [DECISIONS.md](DECISIONS.md) — backend and ownership decisions.
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) — required legal metadata.
- [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md) — shared editor acceptance.
- [../CONTRACTS.md](../CONTRACTS.md) — text/render ownership; project
  `TODO.md` contains the selected implementation work.

SixLabors.Fonts is the only font-processing dependency. Pixel storage,
coverage/SDF/MSDF/color rasterization and returned image ownership stay in
managed DeltaText code. ImageSharp, FreeType, HarfBuzz native assets and a
native MSDF bridge are not runtime dependencies.

Skills: `performance-speedup` for shaping/cache workloads and
`static-analysis` for ownership review.
