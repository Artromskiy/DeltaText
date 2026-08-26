# Delta.Text

Renderer-neutral CPU text producer for Furnace. The public boundary is
`Delta.Text.Contract.ITextService`: it opens immutable font instances, shapes
UTF-16 text and returns unpacked coverage/SDF/MSDF glyph images.

- User-facing API: [USER_API.md](USER_API.md)
- Canonical contract: [PUBLIC_CONTRACT.md](PUBLIC_CONTRACT.md)
- Internal implementation notes: [INTERNAL.md](INTERNAL.md)
- Build and checks: [WORKFLOW.md](WORKFLOW.md)
- Selected work: [TODO.md](TODO.md)
- Legal notices: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

DeltaText owns shaping and CPU image generation only. DeltaRender owns packing,
UVs, staging, batching and GPU resources. DeltaXAML supplies text requests
through its adapter and does not retain DeltaText implementation objects.
