#!/usr/bin/env bash
# Remove the pinned fork cohort from NuGet's global cache before restoring.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="$(sed -n 's/.*<UnoWaylandVersion[^>]*>\([^<]*\)<.*/\1/p' "$root/Directory.Packages.props")"
pins="$(python3 "$root/build/uno-wayland-pins.py" "$version")"
cache="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
while read -r _ name; do
  id="${name%."$version".nupkg}"
  rm -rf -- "${cache:?}/${id,,}/${version:?}"
done <<< "$pins"
