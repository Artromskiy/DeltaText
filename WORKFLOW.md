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

The compile-time package `SixLabors.Fonts` supplies font loading, OpenType
shaping, fallback selection and outline callbacks. DeltaText uses public NuGet
version 3.1.3 in the library, tests and probes. It keeps the returned pixels
and performs coverage, SDF, MSDF, MTSDF and color rasterization in managed C#.
There is no native font or MSDF DLL to copy, and no ImageSharp runtime
dependency.

The public package is a build-time dependency (`PrivateAssets="all"`). At pack
time its restored `SixLabors.Fonts.dll` is embedded into DeltaText as
`lib/net8.0/SixLabors.Fonts.dll`, with the required notice. The pack target
fails if the assembly is missing, and `eng/verify-package.sh` rejects any
SixLabors dependency in the generated nuspec. Consumers therefore restore
DeltaText (and its public DeltaMaths dependency) without a separate SixLabors
package dependency.

The package's build target requires a local Six Labors license file. Set the
property through the environment for local and CI builds; do not commit the
file or its path:

```bash
SixLaborsLicenseFile=/path/to/sixlabors.lic dotnet build src/DeltaText/DeltaText.csproj -c Release
```

The current local license is kept outside Git at
`Furnace/Licenses/SixLabors.lic`. The managed build is otherwise the same on
Linux, macOS and Windows.

The GitHub Actions test workflow is `.github/workflows/ci.yml`. It restores
`DeltaMaths` from the Artromskiy GitHub Packages feed and `SixLabors.Fonts`
from NuGet.org. It also requires the repository secret
`SIXLABORS_LICENSE` containing the Six Labors license file contents. The
secret is materialized only in the runner's temporary directory; it is not
logged or committed. Fork pull requests without access to repository secrets
cannot run this workflow until the required package and license inputs are
made available by the repository owner.

## NuGet

NuGet has only the workspace `dev` and `release` modes. Run them from the
Furnace root as documented in
[`docs/NUGET_WORKFLOW.md`](../docs/NUGET_WORKFLOW.md). The public
`SixLabors.Fonts` package remains a build-time dependency; release mode bundles
its restored assembly and does not expose it to consumers.

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
