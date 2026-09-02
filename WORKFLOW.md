# DeltaText workflow

## Benchmark parameter policy

BenchmarkDotNet attributes may describe benchmark methods, categories and
lifecycle hooks, but they must not define workload or run parameters. Do not add
`[Params]`, `[ParamsSource]`, `[Arguments]`, `[ArgumentsSource]` or equivalent
parameter attributes. Parse every workload/configuration value from application
command-line arguments (or the invoking script) before BenchmarkDotNet starts,
and pass the resulting values into the benchmark runner. Keep BDN runner
switches such as `--filter` and `--job` separate from workload input. Existing
parameter attributes are migration debt: do not add new uses and replace them
when that benchmark is next modified.


## Repository layout gate

The repository must follow the shared first-party layout documented in the
Furnace project standard. Before restore/build or a structural handoff, run:

```bash
./eng/check-layout.sh
```

The gate checks the mandatory top-level directories, rejects unexpected
tracked top-level folders, requires src/DeltaText/ as the primary source
project, and requires source siblings to use the src/DeltaText.<Area>/ form.
samples/ contains runnable examples; probes/ contains bounded
headless/compiler/contract checks. Empty mandatory domains stay tracked with
.gitkeep.

The public API and ownership rules are defined only by
[`PUBLIC_CONTRACT.md`](PUBLIC_CONTRACT.md). The commands below exercise the
current implementation and migration surface; passing them does not change
the contract.

Managed checks:

```bash
dotnet restore src/DeltaText/DeltaText.csproj
dotnet build src/DeltaText/DeltaText.csproj -c Release --no-restore \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false
dotnet run --project tests/DeltaText.Tests/DeltaText.Tests.csproj -c Release
```

Headless Unicode/shaping/render check (bounded; writes fixture PNGs and JSON):

```bash
SixLaborsLicenseFile=/path/to/sixlabors.lic \
dotnet run --project probes/FontCheck/FontCheck.csproj -c Release -- \
  --bidi-corpus probes/FontCheck/Fixtures/BidiCharacterTest.txt \
  --bidi-test probes/FontCheck/Fixtures/BidiTest.txt \
  --bidi-brackets probes/FontCheck/Fixtures/BidiBrackets.txt
```

`FontCheck` validates Unicode 17 UAX #9 levels/order and paired brackets,
shapes Doto and Luckiest Guy fixtures at two pixel sizes, and compares both
sizes of each coverage result against an independent callback rasterizer
through ImageSharp. On macOS it additionally runs a deterministic 2048-case
CoreText/CoreGraphics rasterization corpus at four sizes; the exact RGBA8
comparison and alpha error metrics are written under
`artifacts/native-conformance`. CoreText consumes DeltaText's already-shaped
glyph IDs and positions, so this is a native rasterization/placement baseline,
not a second shaping implementation. It is a correctness fixture, not a
BenchmarkDotNet run. Use `--skip-native` on platforms without CoreText.

Unicode boundary conformance (requires the locally downloaded Unicode 17
corpora; the checker does not download them):

```bash
dotnet build probes/UnicodeConformance/UnicodeConformance.csproj -c Release --no-restore --disable-build-servers -m:1 /p:UseSharedCompilation=false
dotnet run --project probes/UnicodeConformance/UnicodeConformance.csproj -c Release --no-build --no-restore -- /path/to/GraphemeBreakTest-17.0.0.txt /path/to/LineBreakTest-17.0.0.txt
```

The current Unicode 17 inputs were verified with SHA-256
`e2d134d2c52919bace503ebb6a551c1855fe1a1faec18478c78fff254a1793ec` and
`e69884e0dde6a8724873f885d68c52dc14518abf9ae4ca9e2283b8773db3b752`,
respectively. Width-dependent multi-line layout remains a consumer/layout
responsibility.

The pinned package `SixLabors.Fonts.Delta` version `3.1.0` supplies font
loading, OpenType shaping, fallback selection and outline callbacks. It is
built from `Artromskiy/Fonts` commit
`cadda774b743472e4186e96c8d779a8419276f98` (branch
`fix-cff-igrunok-outline`). DeltaText keeps the returned pixels and performs
coverage, SDF, MSDF and color rasterization in managed C#. There is no native
font or MSDF DLL to copy, and no ImageSharp runtime dependency.

The fork package is kept outside Git at
`Furnace/Packages/SixLabors.Fonts-Fork`. `DeltaText.csproj` prepends this local
feed by default; another machine or CI job must provide the same package feed
through the `SixLaborsFontsPackageSource` MSBuild property. This makes a missing
fork package fail during restore instead of silently selecting the public
NuGet build. The package version is selected with
`SixLaborsFontsPackageVersion` when a verified replacement is intentionally
tested.

For a clean checkout without the local fork directory, point that property at
the authenticated feed containing the exact fork package. For example, with a
GitHub Packages source configured in `NuGet.config`:

```bash
dotnet restore src/DeltaText/DeltaText.csproj \
  -p:SixLaborsFontsPackageSource=https://nuget.pkg.github.com/Artromskiy/index.json \
  -p:SixLaborsFontsPackageVersion=3.1.0
```

The feed credentials belong in the user's NuGet credential provider or
environment, never in the repository. The source must provide
`SixLabors.Fonts.Delta` `3.1.0`; the public `SixLabors.Fonts` `3.1.0` package is not an
equivalent substitute for DeltaText's pinned outline behavior.

`SixLabors.Fonts.Delta` is a repack-only package identity: its assembly and CLR
namespace remain `SixLabors.Fonts`, while its package ID cannot collide with the
public package. The repacked package must be published to the configured feed
before a clean external restore can succeed.

The fork's SixLabors.Fonts 3.1.0 code is distributed under the Six Labors Split License. The
package's build target requires a local license file. Set the property through
the environment for local and CI builds; do not commit the file or its path:

```bash
SixLaborsLicenseFile=/path/to/sixlabors.lic dotnet build src/DeltaText/DeltaText.csproj -c Release
```

The current local license is kept outside Git at
`Furnace/Licenses/SixLabors.lic`. The managed build is otherwise the same on
Linux, macOS and Windows.

## NuGet package release

`DeltaText` is published as version `0.0.6` and corresponds to tag `v0.0.6`.
Before packing, make sure the configured feed contains the exact
`SixLabors.Fonts.Delta` `3.1.0` package and that `SixLaborsLicenseFile` points
to a local license file. Pack from a clean checkout into a disposable
directory:

```bash
package_dir="$(mktemp -d "${TMPDIR:-/tmp}/deltatext-pack.XXXXXX")"
SixLaborsLicenseFile=/path/to/sixlabors.lic \
dotnet restore src/DeltaText/DeltaText.csproj \
  -p:SixLaborsFontsPackageSource=https://nuget.pkg.github.com/Artromskiy/index.json \
  -p:SixLaborsFontsPackageVersion=3.1.0
SixLaborsLicenseFile=/path/to/sixlabors.lic \
dotnet pack src/DeltaText/DeltaText.csproj -c Release --no-restore -o "$package_dir"
```

Inspect the nuspec and package contents, then publish only the exact package
version to NuGet.org. Supply the key through a credential provider or an
already-exported environment variable; never put it in this repository or in
shell history:

```bash
: "${NUGET_API_KEY:?Set NUGET_API_KEY through your local credential setup}"
dotnet nuget push "$package_dir/DeltaText.0.0.6.nupkg" \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY" \
  --skip-duplicate \
  --no-symbols
```

The private `SixLabors.Fonts.Delta` dependency must be available to consumers
through their configured authenticated feed; publishing `DeltaText` does not
replace that dependency.

## Code metrics

Run the same analyzer/code-metrics build locally and in the manual GitHub
Actions workflow through the repository wrapper:

```bash
SixLaborsLicenseFile=/path/to/sixlabors.lic ./eng/code-metrics.sh -v:q
```

`eng/code-metrics.sh` converts `CODE_METRICS_ERROR_LOG` (default:
`artifacts/code-metrics/diagnostics.sarif`) to an absolute path before
MSBuild starts, so multi-project builds write one repository-level SARIF
instead of resolving a missing directory relative to each project. An
explicit destination is supported:

```bash
SixLaborsLicenseFile=/path/to/sixlabors.lic \
  CODE_METRICS_ERROR_LOG=/tmp/code-metrics.sarif ./eng/code-metrics.sh -v:q
```

Inspect the SARIF and summary artifacts from the manual workflow. The rules
CA1501/CA1502/CA1505/CA1506 are report-only signals; do not refactor a method
for one isolated warning. Refactor when several metrics remain over their
limits, the issue persists across runs, or profiling identifies a hot path.

For local application run `./eng/format.sh`; for a non-mutating check use
`FORMAT_CHECK=1 ./eng/format.sh`. The script uses `dotnet format whitespace
--folder` to avoid the MSBuild/Roslyn workspace load that can hang on macOS
with .NET 10. It checks/applies whitespace only; analyzer/style diagnostics
remain covered by the build and SARIF metrics workflow.
