#!/usr/bin/env bash
# Regenerate packaging/linux/icons/hicolor from the Files logo tiles (no SVG source exists in the repo),
# with a small penguin badge in the bottom-right corner so LinuxFiles is distinct from the official Files icon.
# Requires ImageMagick (`convert` or `magick`).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
src="$root/src/Files.App/Assets/AppTiles/Release"
dst="$root/packaging/linux/icons/hicolor"
name="io.github.memergamer.LinuxFiles.png"
im="magick"
command -v magick >/dev/null || im="convert"

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

# Badge: simple penguin drawn on a 100x100 canvas, composited bottom-right at ~46% of the icon size.
badge="$(mktemp --suffix=.png)"
trap 'rm -f "$badge"' EXIT
$im -size 100x100 xc:none \
  -fill '#ffffff' -stroke '#1b1b1b' -strokewidth 4 -draw 'circle 50,50 50,3' \
  -stroke none -fill '#1b1b1b' -draw 'ellipse 50,56 24,34 0,360' \
  -fill '#ffffff' -draw 'ellipse 50,62 14,25 0,360' \
  -fill '#ffffff' -draw 'circle 41,34 41,28' -draw 'circle 59,34 59,28' \
  -fill '#1b1b1b' -draw 'circle 42,34 42,31' -draw 'circle 58,34 58,31' \
  -fill '#f5a623' -draw 'polygon 43,42 57,42 50,50' \
  -draw 'ellipse 38,88 11,5 0,360' -draw 'ellipse 62,88 11,5 0,360' \
  "$badge"

for f in "$dst"/*/apps/"$name"; do
  s="$(basename "$(dirname "$(dirname "$f")")")"; s="${s%%x*}"
  b=$(( s * 46 / 100 ))
  $im "$f" \( "$badge" -filter Lanczos -resize "${b}x${b}" \) -gravity southeast -compose over -composite "$f"
done
