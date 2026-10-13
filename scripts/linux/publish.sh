#!/usr/bin/env bash
# Publish Files for Linux (Uno Skia desktop) into artifacts/linux-<rid>/.
# Usage: scripts/linux/publish.sh [--rid linux-x64|linux-arm64] [--framework-dependent] [--tarball] [--x11]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
rid="linux-x64"
self_contained="true"
tarball="false"
use_uno_wayland="true"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid) rid="$2"; shift 2 ;;
    --framework-dependent) self_contained="false"; shift ;;
    --tarball) tarball="true"; shift ;;
    --x11) use_uno_wayland="false"; shift ;;
    -h|--help) sed -n '2,3p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

case "$rid" in
  linux-x64|linux-arm64) ;;
  *) echo "Unsupported RID: $rid" >&2; exit 2 ;;
esac

if [[ "$use_uno_wayland" == "true" ]]; then
  "$root/build/fetch-uno-wayland.sh"
  "$root/build/clear-uno-wayland-cache.sh"
fi

out="$root/artifacts/$rid"
rm -rf "$out"

# Trimming and AOT are intentionally not enabled.
MSBUILDDISABLENODEREUSE=1 nice -n 19 dotnet publish "$root/src/Files.App/Files.App.csproj" \
  -f net10.0-desktop -c Release -r "$rid" \
  --self-contained "$self_contained" \
  -p:UseUnoWayland="$use_uno_wayland" \
  -p:PublishTrimmed=false -p:PublishAot=false -p:PublishReadyToRun=true \
  -nodeReuse:false -m:2 -o "$out"

# This AOT executable is installed separately, root-owned, by native packages only.
MSBUILDDISABLENODEREUSE=1 nice -n 19 dotnet publish \
  "$root/src/Files.Platform.Linux.ElevationHelper/Files.Platform.Linux.ElevationHelper.csproj" \
  -c Release -r "$rid" --self-contained -nodeReuse:false -m:2 \
  -o "$out/elevation-helper"

echo "Published to $out ($(du -sh "$out" | cut -f1))"

if [[ "$tarball" == "true" ]]; then
  tar -C "$root/artifacts" -czf "$root/artifacts/files-$rid.tar.gz" "$rid"
  echo "Tarball: $root/artifacts/files-$rid.tar.gz"
fi
