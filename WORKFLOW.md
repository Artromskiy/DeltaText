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
