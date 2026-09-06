# DeltaText agent router

Scope: renderer-neutral font identity, SixLabors.Fonts shaping/outlines,
positioned glyphs and CPU SDF/MSDF generation. DeltaText owns no XAML, Vulkan,
SDL or shader runtime and publishes renderer-neutral glyph images/data.

## Map — open only as needed

- ../CODE_STYLE.md — technical ownership, allocation and cache rules.
- ../CONTRACTS.md — text/render ownership; open only for a boundary task.
- IDEAS.md — backend/cache research/options only when requested.
- WORKFLOW.md — managed/native build, tests and fixture export.
- PUBLIC_CONTRACT.md — frozen public data model and ownership boundary.
- USER_API.md — user-facing text API; open only for public API/documentation work.
- INTERNAL.md and DECISIONS.md — implementation ownership and durable decisions.
- src/DeltaText — production shaping/rasterization.
- tests, probes, benchmarks — verification, focused checks and measured workloads.

SixLabors.Fonts is build-time only; released DeltaText exposes no SixLabors
package dependency. ImageSharp, FreeType, HarfBuzz native assets and native
MSDF bridges are not runtime dependencies.
