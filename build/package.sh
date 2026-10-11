#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="${1:-Release}"
output_dir="$project_root/artifacts/package"
package_name="frontiermapbetterprospecting"
dll_path="$project_root/src/bin/$configuration/net10.0/FrontierMapBetterProspecting.dll"

dotnet build "$project_root/src/FrontierMapBetterProspecting.csproj" \
  --configuration "$configuration"

rm -rf "$output_dir/stage"
mkdir -p "$output_dir/stage"
cp "$project_root/modinfo.json" "$output_dir/stage/modinfo.json"
cp "$dll_path" "$output_dir/stage/FrontierMapBetterProspecting.dll"
rm -f "$output_dir/$package_name.zip"

(
  cd "$output_dir/stage"
  if command -v zip >/dev/null 2>&1; then
    zip -q "$output_dir/$package_name.zip" modinfo.json FrontierMapBetterProspecting.dll
  else
    7z a -tzip -bd -y "$output_dir/$package_name.zip" \
      modinfo.json FrontierMapBetterProspecting.dll >/dev/null
  fi
)

rm -rf "$output_dir/stage"
printf 'Created %s\n' "$output_dir/$package_name.zip"
