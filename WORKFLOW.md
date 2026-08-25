# DeltaText workflow

The public API and ownership rules are defined only by
[`PUBLIC_CONTRACT.md`](PUBLIC_CONTRACT.md). The commands below exercise the
current implementation and migration surface; passing them does not change
the contract.

Managed checks:

```bash
dotnet restore Delta.Text.csproj
dotnet build Delta.Text.csproj -c Release --no-restore \
  --disable-build-servers -m:1 /p:UseSharedCompilation=false
dotnet run --project Tests/Delta.Text.Tests.csproj -c Release
```

Native bridge when its source changes:

```bash
cmake -S native -B native/build -DCMAKE_BUILD_TYPE=Release
cmake --build native/build --config Release
```

The `Native text bridge smoke` workflow builds and runs this bridge on
`ubuntu-latest`, `macos-14` and `windows-latest`. It copies the resulting
`libDeltaTextMsdf.so`, `libDeltaTextMsdf.dylib` or `DeltaTextMsdf.dll` beside
the test executable before running the MSDF smoke. The Windows runner is the
checked CI contract; local Windows packaging should ship the DLL beside
`Delta.Text.dll` or the consuming executable.

The workflow sets `DELTATEXT_REQUIRE_NATIVE_SMOKE=1`; missing or unloadable
native output fails the job. A normal managed test run leaves that variable
unset, so Gray8 remains usable without a native MSDF binary.

HarfBuzz package assets remain under the standard output layout
`runtimes/<rid>/native/<library>`. `NativeLibraryResolver` checks files beside
the managed assembly first, then the current runtime identifier, current
platform/architecture and the neutral platform RID (`osx`, `linux` or `win`).
Preserve the `runtimes` directory when copying or publishing Delta.Text; no
consumer-side native-library copy or disk-wide search is required.

| Target | Native output | Required runtime dependency |
|---|---|---|
| Linux x64 | `libDeltaTextMsdf.so` | system C++ runtime plus bundled msdfgen core and packaged HarfBuzz assets |
| macOS arm64 | `libDeltaTextMsdf.dylib` | Apple C++ runtime plus bundled msdfgen core and packaged HarfBuzz assets |
| Windows x64 | `DeltaTextMsdf.dll` | MSVC C++ runtime plus bundled msdfgen core and packaged HarfBuzz assets |

No FreeType, Homebrew, vcpkg or other system font library is required by the
bridge. The CI matrix is the platform evidence; a local macOS run does not
claim Linux or Windows compatibility.

The current test project can export a legacy atlas fixture with:

```bash
dotnet run --project Tests/Delta.Text.Tests.csproj -c Release -- \
  --export-atlas-fixture <output-directory>
```

This export is retained for migration/testing only. Atlas pages, UVs and row
pitch are not DeltaText public outputs; consumers own those concerns.

Verify ownership/disposal on both success and failure paths. Do not infer
cross-platform native success from one macOS run.

## Code metrics

Run the same analyzer/code-metrics build locally and in the manual GitHub
Actions workflow through the repository wrapper:

```bash
./eng/code-metrics.sh -v:q
```

`eng/code-metrics.sh` converts `CODE_METRICS_ERROR_LOG` (default:
`artifacts/code-metrics/diagnostics.sarif`) to an absolute path before
MSBuild starts, so multi-project builds write one repository-level SARIF
instead of resolving a missing directory relative to each project. An
explicit destination is supported:

```bash
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
