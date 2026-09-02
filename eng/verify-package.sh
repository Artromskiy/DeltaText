#!/usr/bin/env bash
set -euo pipefail

package_path="${1:?usage: verify-package.sh path/to/DeltaText.nupkg}"
[[ -f "$package_path" ]] || { echo "Package not found: $package_path" >&2; exit 1; }
nuspec="$(unzip -p "$package_path" 'DeltaText.nuspec')"
grep -Fq '<id>DeltaText</id>' <<<"$nuspec"
if grep -Eqi '<dependency[^>]+id="SixLabors\.' <<<"$nuspec"; then
    echo "DeltaText package must not expose a SixLabors NuGet dependency." >&2
    exit 1
fi
unzip -l "$package_path" | grep -Eq '[[:space:]]lib/net8\.0/SixLabors\.Fonts\.dll$'
unzip -tq "$package_path" 'lib/net8.0/SixLabors.Fonts.dll'
unzip -tq "$package_path" 'THIRD-PARTY-NOTICES.md'
echo "DeltaText package surface verified."
