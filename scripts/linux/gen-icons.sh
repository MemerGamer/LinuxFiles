#!/usr/bin/env bash
# Regenerate packaging/linux/icons/hicolor from the Files logo tile (no SVG source of the base icon exists in the repo),
# with a small penguin badge in the bottom-right corner so LinuxFiles is distinct from the official Files icon.
# Requires ImageMagick (`convert` or `magick`) and rsvg-convert.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
src="$root/src/Files.App/Assets/AppTiles/Release"
dst="$root/packaging/linux/icons/hicolor"
name="io.github.memergamer.LinuxFiles.png"
im="magick"
command -v magick >/dev/null || im="convert"

# Every size is downscaled from the 600x600 tile with a high-quality filter (the shipped small tiles are paletted and look pixelated).
# The tile has wide transparent padding: trim it, then re-pad to a square with a small margin so the glyph fills the icon.
base="$(mktemp --suffix=.png)"
trap 'rm -f "$base"' EXIT
$im "$src/Square150x150Logo.scale-400.png" -trim +repage -background none -gravity center -extent "%[fx:max(w,h)]x%[fx:max(w,h)]" \
  -bordercolor none -border 4% "$base"
for s in 16 24 32 48 64 128 256 512; do
  mkdir -p "$dst/${s}x${s}/apps"
  $im "$base" -filter Lanczos -define filter:blur=0.9 -resize "${s}x${s}" "$dst/${s}x${s}/apps/$name"
done

# Badge: vector penguin (packaging/linux/icons/penguin-badge.svg) rasterised directly at its final pixel size, so it stays crisp.
# Icons up to 24px use a simplified badge; small icons get a proportionally larger badge to keep it readable.
for f in "$dst"/*/apps/"$name"; do
  s="$(basename "$(dirname "$(dirname "$f")")")"; s="${s%%x*}"
  if (( s <= 32 )); then b=$(( s * 56 / 100 )); else b=$(( s * 46 / 100 )); fi
  badge="$(mktemp --suffix=.png)"
  svg=penguin-badge.svg; (( s <= 24 )) && svg=penguin-badge-small.svg
  rsvg-convert -w "$b" -h "$b" "$root/packaging/linux/icons/$svg" -o "$badge"
  $im "$f" "$badge" -gravity southeast -compose over -composite "PNG32:$f"
  rm -f "$badge"
done
