#!/usr/bin/env bash
# Regenerate packaging/linux/icons/hicolor from the Files logo tiles (no SVG source exists in the repo).
# Requires ImageMagick (`convert` or `magick`).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
src="$root/src/Files.App/Assets/AppTiles/Release"
dst="$root/packaging/linux/icons/hicolor"
name="io.github.memergamer.LinuxFiles.png"
im="convert"
command -v convert >/dev/null || im="magick"

# Native small sizes shipped by the app.
for s in 16 24 32 48 256; do
  mkdir -p "$dst/${s}x${s}/apps"
  cp "$src/Square44x44Logo.targetsize-$s.png" "$dst/${s}x${s}/apps/$name"
done

# Remaining sizes downscaled from the 600x600 tile.
for s in 64 128 512; do
  mkdir -p "$dst/${s}x${s}/apps"
  $im "$src/Square150x150Logo.scale-400.png" -filter Lanczos -resize "${s}x${s}" "$dst/${s}x${s}/apps/$name"
done
