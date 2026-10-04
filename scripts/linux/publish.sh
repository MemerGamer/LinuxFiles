#!/usr/bin/env bash
# Publish Files for Linux (Uno Skia desktop) into artifacts/linux-<rid>/.
# Usage: scripts/linux/publish.sh [--rid linux-x64|linux-arm64] [--framework-dependent] [--tarball]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
rid="linux-x64"
self_contained="true"
tarball="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid) rid="$2"; shift 2 ;;
    --framework-dependent) self_contained="false"; shift ;;
    --tarball) tarball="true"; shift ;;
    -h|--help) sed -n '2,3p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

case "$rid" in
  linux-x64|linux-arm64) ;;
  *) echo "Unsupported RID: $rid" >&2; exit 2 ;;
esac

out="$root/artifacts/$rid"
rm -rf "$out"

# Trimming and AOT are intentionally not enabled.
dotnet publish "$root/src/Files.App/Files.App.csproj" \
  -f net10.0-desktop -c Release -r "$rid" \
  --self-contained "$self_contained" \
  -p:PublishTrimmed=false -p:PublishAot=false \
  -o "$out"

echo "Published to $out ($(du -sh "$out" | cut -f1))"

if [[ "$tarball" == "true" ]]; then
  tar -C "$root/artifacts" -czf "$root/artifacts/files-$rid.tar.gz" "$rid"
  echo "Tarball: $root/artifacts/files-$rid.tar.gz"
fi
