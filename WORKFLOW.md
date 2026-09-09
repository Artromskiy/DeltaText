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

## Delta consumer boundary

DeltaText, its tests and its bounded FontCheck/UnicodeConformance probes use
`using global::Delta;` with the canonical `Maths.*` facade for mathematical
operations. Keep the provider implementation's platform bridge isolated in
DeltaMaths; do not reintroduce direct
`System.Math`/`System.MathF` calls in this repository's consumer code. Run this
bounded gate before a contract or performance change:

```bash
if rg -n '\b(Math|MathF)\.' src tests probes samples benchmarks -g '*.cs'; then
  echo 'Direct System.Math/System.MathF usage is not allowed in DeltaText consumers.' >&2
  exit 1
fi
```

The provider exception is limited to DeltaMaths's own implementation files
`Maths.cs`, `Maths.half.cs` and `MathCompat.cs`; those files are outside this
repository and implement the scalar primitives consumed here.

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

The compile-time package `SixLabors.Fonts.Delta` supplies font loading,
OpenType shaping, fallback selection and outline callbacks. The current
package is built from the official `SixLabors/Fonts` repository at commit
`c68b27d683c701ca254d5cfc6487c699954f19ff` (merge of PR #557,
`Fix signed fractional CFF coordinates`). The package ID and version are
declared once in `src/DeltaText/DeltaText.csproj`; this workflow does not copy
that version. DeltaText keeps the returned pixels and performs coverage, SDF,
MSDF and color rasterization in managed C#. There is no native font or MSDF DLL
to copy, and no ImageSharp runtime dependency.

The source snapshot is repackaged as `SixLabors.Fonts.Delta` for the build-only
input kept outside Git at
`Furnace/Packages/SixLabors.Fonts-Fork`. `DeltaText.csproj` and the dev-only
tests/probes prepend this local feed by default; another machine or CI job that
builds the source, tests or probes must provide the same package feed through
the `SixLaborsFontsPackageSource` MSBuild property. This makes a missing fork
package fail during restore instead of silently selecting the public NuGet
build. A verified replacement may override `SixLaborsFontsPackageVersion`; the
default remains the value declared in the project file.

For a clean source checkout without the local fork directory, point that
property at an authenticated feed containing the exact fork package. Do not
rely on the repository `NuGet.config` to provide this build-only source: pass it
explicitly through `SixLaborsFontsPackageSource`:

```bash
sixlabors_fonts_source='https://nuget.pkg.github.com/Artromskiy/index.json'
dotnet restore src/DeltaText/DeltaText.csproj \
  -p:SixLaborsFontsPackageSource="$sixlabors_fonts_source"
```

The feed credentials belong in the user's NuGet credential provider or
environment, never in the repository. The source must provide the
`SixLabors.Fonts.Delta` package declared by `DeltaText.csproj`; the public
`SixLabors.Fonts` package is not an equivalent substitute for DeltaText's
pinned outline behavior.

The published DeltaText package has no SixLabors NuGet dependency. At pack time
the verified fork assembly is copied into the package as
`lib/net8.0/SixLabors.Fonts.dll`, with its required notice. The pack target fails
closed when that assembly is missing, and `eng/verify-package.sh` rejects any
SixLabors dependency in the generated nuspec. Consumers therefore restore only
DeltaText (and its public DeltaMaths dependency); they do not need a private
feed or a separate SixLabors package.

The pinned SixLabors.Fonts source is distributed under the Six Labors Split License. The
package's build target requires a local license file. Set the property through
the environment for local and CI builds; do not commit the file or its path:

```bash
SixLaborsLicenseFile=/path/to/sixlabors.lic dotnet build src/DeltaText/DeltaText.csproj -c Release
```

The current local license is kept outside Git at
`Furnace/Licenses/SixLabors.lic`. The managed build is otherwise the same on
Linux, macOS and Windows.

The GitHub Actions test workflow is `.github/workflows/ci.yml`. It restores
`DeltaMaths` and `SixLabors.Fonts.Delta` from the Artromskiy GitHub Packages
feed and requires the repository secret `SIXLABORS_LICENSE` containing the
Six Labors license file contents. The secret is materialized only in the
runner's temporary directory; it is not logged or committed. Fork pull
requests without access to repository secrets cannot run this workflow until
the required package and license inputs are made available by the repository
owner.

## NuGet

NuGet has only the workspace `dev` and `release` modes. Run them from the
Furnace root as documented in
[`docs/NUGET_WORKFLOW.md`](../docs/NUGET_WORKFLOW.md). The private
`SixLabors.Fonts.Delta` package remains a build-time dependency; the release
mode bundles the verified assembly and does not expose it to consumers.

## Code metrics

Run the shared Furnace wrappers from this repository before every commit; see
the [common workflow](../REVIEW_PLAYBOOK.md#shared-local-formatter-and-metrics-wrappers):

```bash
../eng/format.sh "$PWD"
FORMAT_CHECK=1 ../eng/format.sh "$PWD"
SixLaborsLicenseFile=/path/to/sixlabors.lic \
  ../eng/code-metrics.sh "$PWD" -v:q
```

Set `CODE_METRICS_ERROR_LOG` when a different SARIF destination is needed:

```bash
SixLaborsLicenseFile=/path/to/sixlabors.lic \
  CODE_METRICS_ERROR_LOG=/tmp/deltatext-metrics.sarif \
  ../eng/code-metrics.sh "$PWD" -v:q
```

Inspect the SARIF and summary artifacts from the manual workflow. The rules
CA1501/CA1502/CA1505/CA1506 are report-only signals; do not refactor a method
for one isolated warning. Refactor when several metrics remain over their
limits, the issue persists across runs, or profiling identifies a hot path.
