#!/usr/bin/env bash
set -euo pipefail

package_path="${1:?usage: verify-package.sh path/to/DeltaText.nupkg}"
[[ -f "$package_path" ]] || { echo "Package not found: $package_path" >&2; exit 1; }
nuspec="$(unzip -p "$package_path" 'DeltaText.nuspec')"
grep -Fq '<id>DeltaText</id>' <<<"$nuspec"
grep -Fq 'lib/net8.0/SixLabors.Fonts.dll' <<<"$nuspec"
! grep -Fq 'SixLabors.Fonts.Delta' <<<"$nuspec"
unzip -tq "$package_path" 'lib/net8.0/SixLabors.Fonts.dll'
unzip -tq "$package_path" 'THIRD-PARTY-NOTICES.md'
echo "DeltaText package surface verified."
