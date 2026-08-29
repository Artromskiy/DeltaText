# DeltaText internal implementation

This document is internal and is not a consumer API.

`SixLaborsTextService` owns a synchronized map of generation-safe
`FontInstanceId` values to `FontFace` objects. `FontFace` owns a private copy of
font bytes and the SixLabors.Fonts collection/face objects until `CloseFont` or
service disposal. No package object or native handle crosses
`Delta.Text.Contract`. The implementation entry point is
`SixLaborsTextService`; no font-backend implementation type is part of the
cross-project contract.

The pinned SixLabors.Fonts fork performs OpenType layout, fallback selection and outline
callbacks. Bidi formatting controls are removed from the layout metrics before
they are paired with renderer callbacks, because controls have source mapping
but no rendered glyph. Fallback identity comes from each returned
`GlyphMetrics.Font`, not from the enclosing text run. Shaping output is copied
into contract-owned arrays.

The adapter passes globally enabled Boolean feature tags to
`TextOptions.FeatureTags` and maps `kern=0` to `KerningMode.None`. SixLabors
The DeltaText fork adapter currently does not map script/language selectors,
ranged feature API, arbitrary feature values or color-palette selection, so
those requests are rejected at the boundary rather than being silently
dropped.

Safety metadata is derived conservatively from the source cluster shape:
multi-scalar and combining clusters, plus Arabic joining contexts, receive
`UnsafeToBreak | UnsafeToConcat`. No `SafeToInsertTatweel` claim is made.

`UnicodeText` is the producer-side boundary API for the parts of text layout
that do not need a font. Graphemes use the public SixLabors grapheme
enumerator. Line breaks use `UnicodeLineBreakEngine`, a managed UAX #14 rule
engine over SixLabors public Unicode property lookups; it does not access the
package's internal line-break types. Both APIs decode UTF-16 once and publish
owned snapshots with UTF-16 offsets. Official Unicode 17 corpus checks live in
`probes/UnicodeConformance`; they cover 766 grapheme cases and 19,338 line-break
cases. Width-aware line construction is intentionally not part of this layer.

`OpenFont` owns one defensive copy of the caller's font bytes. Each successful
`Shape` call creates a new run/glyph snapshot, while implementation-owned
scratch arrays, fallback lists and run builders are reused under the service
lock. Shaping collects glyph metadata only; outline capture is deferred until
`GenerateGlyphImage` needs a glyph, so shaping does not retain contour arrays.
SixLabors `Font` objects are cached per face and pixel size, and the service
keeps a deterministic FIFO glyph-image cache per face (up to 256 entries and 8
MiB). Cached images are immutable contract objects and are safe to share between
repeated requests; the cache is cleared when the face closes. No mutable list,
pinned managed array or native pixel allocation is exposed to a consumer.

`CpuTextRenderer.Render(ShapedText, ...)` is the explicit no-reshaping path
for preview or UI loops that retain a shaped result. The request overload is a
convenience operation and still shapes on every call; callers should retain
`ShapedText` when its input and shaping options are unchanged.

Coverage, SDF, MSDF and color rasterization are all managed. MSDF consumes the
SixLabors.Fonts outline callbacks, flattens curves to a bounded pixel tolerance
and generates deterministic RGB8 pixels. Its geometric values use
`Delta.Maths.float2` and `Delta.Maths.DeltaMaths`; the only
`System.Numerics.Vector2` reference is the private callback adapter required by
SixLabors.Fonts. ImageSharp, SkiaSharp, FreeType and native MSDF assets are not
runtime dependencies.

`UnicodeBidiData` is generated from ICU 78.3's pinned Unicode 17.0 bidi
properties. The repository stores the resulting table and has no ICU runtime
dependency. The resolver applies UAX #9 explicit, isolating-run-sequence,
weak, paired-bracket, neutral, implicit and reorder stages over that table;
isolates remain boundaries until formatting controls are removed, and
explicit-level overflow is tracked separately from valid embedding/isolate
stack entries. The Unicode 17 `BidiCharacterTest` corpus passes all 91,707
cases through L2. L3/L4 line-layout rules are outside that corpus and are not
claimed by this evidence.

Color layers exposed by SixLabors.Fonts are flattened by
`ManagedGlyphRasterizer` into the contract's owned RGBA snapshot. A glyph
format that cannot be exposed as outline callbacks uses the foreground-colored
outline fallback. No SixLabors object, mutable contour list or font handle
crosses the contract.

The implementation deliberately owns no atlas pages, UV coordinates, staging
buffers, batching keys or GPU resources. Consumer adapters must not re-expose
these implementation details through a second public text API.

## Incomplete and obsolete-candidate implementation markers

The source uses `INCOMPLETE / OBSOLETE-CANDIDATE` comments for active code that
is still required by the current producer path but is not ready to be called a
final implementation. These are not `[Obsolete]` attributes: adding compiler
warnings to live implementations would falsely suggest that the current
contract has a replacement.

| Source area | Current limitation | Required follow-up |
|---|---|---|
| `BidiResolver` | The managed data-driven resolver passes all 91,707 Unicode 17 `BidiCharacterTest` cases through L2. The corpus does not cover UAX #9 L3/L4 line-layout rules. | Keep the corpus fixture/command in the conformance loop; add separate line-layout evidence before making an L3/L4 claim. |
| `SixLaborsTextService` direction/feature adapter | The DeltaText SixLabors.Fonts fork adapter does not map all contract fields. Vertical direction is currently collapsed to backend `Auto`; unsupported script, language, ranged and valued feature requests are rejected. | Add explicit backend mappings or keep these requests rejected; never silently discard direction or feature semantics. |
| `FontFace.TryCreateOutline` | Direct glyph-ID metrics and rendering depend on the pinned SixLabors fork's glyph-id API. | Keep the fork API covered by the direct-glyph regression fixture when upgrading the package. |
| `ManagedGlyphRasterizer.RenderColor` | Color output is flattened through outline layers and falls back to a foreground-colored outline for unsupported formats. | Add full COLR v1/SVG paint traversal, transforms and palette handling when the managed backend exposes them safely. |
| `MsdfGeometry` / `MsdfRasterizer` | Edge coloring, grid broad phase and distance evaluation are deterministic baseline implementations. | Add measured corner-quality and difficult-contour coverage, then optimize only against a representative workload. |
| `CpuTextRenderer` | The request overload creates one owned bitmap and deliberately does not retain shaping state. | Use the `Render(ShapedText, ...)` overload for unchanged text; atlas/cache ownership remains outside DeltaText. |

Do not mark the frozen `Delta.Text.Contract` or the live
`SixLaborsTextService` producer as obsolete until a compatible replacement is
implemented and consumers have an explicit migration path.
