#!/usr/bin/env bash
# Builds the plugin and zips it with a meta.json, ready for Jellyfin to install.
# Usage: scripts/package.sh <version>   e.g. scripts/package.sh 1.0.0.0
# Prints the zip file name on the last line.
set -euo pipefail

version="$1"
root="$(cd "$(dirname "$0")/.." && pwd)"
target_abi=$(sed -n 's/^targetAbi: *"\(.*\)"/\1/p' "$root/build.yaml")
out="$root/artifacts"
zip_name="auto-content-ratings_${version}.zip"

rm -rf "$out/build" "$out/package"
mkdir -p "$out/package"

dotnet build "$root/Jellyfin.Plugin.ContentRatings/Jellyfin.Plugin.ContentRatings.csproj" -c Release \
  -o "$out/build" -p:Version="$version" -p:AssemblyVersion="$version" -p:FileVersion="$version" >&2

cp "$out/build/Jellyfin.Plugin.ContentRatings.dll" "$out/package/"
cat > "$out/package/meta.json" <<JSON
{
  "category": "Metadata",
  "changelog": "",
  "description": "Automatically sets content ratings from TMDB or TVDB.",
  "guid": "44c838b7-b4cc-4cdf-bc1d-3f8fe3ce3748",
  "name": "Auto Content Ratings",
  "overview": "Automatically sets content ratings (PG, 12, 15...) from TMDB or TVDB",
  "owner": "scottnicholls36",
  "targetAbi": "${target_abi}",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "version": "${version}",
  "status": "Active",
  "autoUpdate": true,
  "assemblies": ["Jellyfin.Plugin.ContentRatings.dll"]
}
JSON

rm -f "$out/$zip_name"
(cd "$out/package" && zip -q -r "../$zip_name" .)
echo "$out/$zip_name"
