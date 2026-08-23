# DeltaText workflow

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

| Target | Native output | Required runtime dependency |
|---|---|---|
| Linux x64 | `libDeltaTextMsdf.so` | system C++ runtime plus bundled msdfgen core and packaged HarfBuzz assets |
| macOS arm64 | `libDeltaTextMsdf.dylib` | Apple C++ runtime plus bundled msdfgen core and packaged HarfBuzz assets |
| Windows x64 | `DeltaTextMsdf.dll` | MSVC C++ runtime plus bundled msdfgen core and packaged HarfBuzz assets |

No FreeType, Homebrew, vcpkg or other system font library is required by the
bridge. The CI matrix is the platform evidence; a local macOS run does not
claim Linux or Windows compatibility.

Export the renderer fixture with:

```bash
dotnet run --project Tests/Delta.Text.Tests.csproj -c Release -- \
  --export-atlas-fixture <output-directory>
```

Verify ownership/disposal on both success and failure paths. Do not infer
cross-platform native success from one macOS run.

## Code metrics

Run the manual GitHub Actions `Code metrics` workflow before committing a
substantial change, then inspect its SARIF and summary artifacts. The rules
CA1501/CA1502/CA1505/CA1506 are report-only signals; do not refactor a method
for one isolated warning. Refactor when several metrics remain over their
limits, the issue persists across runs, or profiling identifies a hot path.

For local application run `./eng/format.sh`; for a non-mutating check use
`FORMAT_CHECK=1 ./eng/format.sh`. The script uses `dotnet format whitespace
--folder` to avoid the MSBuild/Roslyn workspace load that can hang on macOS
with .NET 10. It checks/applies whitespace only; analyzer/style diagnostics
remain covered by the build and SARIF metrics workflow.
