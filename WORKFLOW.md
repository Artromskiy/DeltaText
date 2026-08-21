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

Run the manual GitHub Actions `Code metrics` workflow when maintainability
evidence is needed. It enables CA1501/CA1502/CA1505/CA1506 as report-only
diagnostics and uploads the SARIF, build log and exit summary as artifacts.
