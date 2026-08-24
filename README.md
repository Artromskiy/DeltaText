# Delta.Text

DeltaText is Furnace's renderer-neutral text producer. It owns font identity,
OpenType shaping, glyph metrics/outlines and CPU glyph-image generation; it
does not own XAML, Vulkan, SDL, shader state or atlas packing.

The authoritative public API is declared in
[`src/DeltaText/Contract`](src/DeltaText/Contract) under `Delta.Text.Contract`.
[`PUBLIC_CONTRACT.md`](PUBLIC_CONTRACT.md) is the single source of truth for
the data model, ownership, serialization shape and supported representations.
Other documents in this directory are implementation notes or project
workflow and must not define another public API.

The current implementation still contains legacy atlas/cache types while the
contract migration is in progress. They are migration-only and must not gain
new consumers.

See [DECISIONS.md](DECISIONS.md) for implementation decisions,
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for licenses,
[WORKFLOW.md](WORKFLOW.md) for checks and packaging, and
[TODO.md](TODO.md) for selected work.
