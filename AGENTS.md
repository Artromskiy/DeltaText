# DeltaText agent guide

Scope: renderer-neutral font identity, HarfBuzz shaping/outlines, positioned
glyphs and CPU SDF/MSDF generation. It owns no XAML, Vulkan, SDL or shaders.

- [README.md](README.md) — stable public text/atlas contract.
- [TODO.md](TODO.md) — selected text work.
- [IDEAS.md](IDEAS.md) — deferred backend/cache options.
- [WORKFLOW.md](WORKFLOW.md) — managed/native build, tests and fixture export.
- [DECISIONS.md](DECISIONS.md) — backend and ownership decisions.
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) — required legal metadata.
- [../EDITOR_UI_TODO.md](../EDITOR_UI_TODO.md) — shared editor acceptance.

Do not edit vendored `third_party/msdfgen` except for an explicitly reviewed
vendor update. FreeType is not an engine dependency.

Skills: `abi-and-calling-conventions` for the native bridge, `cmake` for its
build, `cpp-templates` only inside reviewed msdfgen integration,
`performance-speedup` for shaping/cache workloads, and `static-analysis` for
native lifetime and ownership review.
