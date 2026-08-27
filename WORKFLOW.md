# DeltaText workflow

The public API and ownership rules are defined only by
[`PUBLIC_CONTRACT.md`](PUBLIC_CONTRACT.md). The commands below exercise the
current implementation and migration surface; passing them does not change
the contract.

Managed checks:

```bash
dotnet restore DeltaText.csproj
dotnet build DeltaText.csproj -c Release --no-restore \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false
dotnet run --project Tests/DeltaText.Tests.csproj -c Release
```

SixLabors.Fonts supplies font loading, OpenType shaping, fallback selection and
outline callbacks. DeltaText keeps the returned pixels and performs coverage,
SDF, MSDF and color rasterization in managed C#. There is no native font or
MSDF DLL to copy, and no ImageSharp runtime dependency.

SixLabors.Fonts 3.0.0 is distributed under the Six Labors Split License. The
package's build target requires a local license file. Set the property through
the environment for local and CI builds; do not commit the file or its path:

```bash
SixLaborsLicenseFile=/path/to/sixlabors.lic dotnet build DeltaText.csproj -c Release
```

The current local license is kept outside Git at
`Furnace/Licenses/SixLabors.lic`. The managed build is otherwise the same on
Linux, macOS and Windows.

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
