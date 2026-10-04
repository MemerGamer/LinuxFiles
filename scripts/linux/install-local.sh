#!/usr/bin/env bash
# Install a published Files build for the current user (no root).
# Usage: scripts/linux/install-local.sh [--from artifacts/linux-x64] [--prefix ~/.local]
# Layout: <prefix>/share/linuxfiles, <prefix>/bin/files, desktop entry, icons, metainfo.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
from="$root/artifacts/linux-x64"
prefix="${HOME}/.local"
app_id="io.github.memergamer.LinuxFiles"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --from) from="$2"; shift 2 ;;
    --prefix) prefix="$2"; shift 2 ;;
    -h|--help) sed -n '2,4p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

[[ -f "$from/Files.dll" ]] || { echo "No published build at $from (run scripts/linux/publish.sh)" >&2; exit 1; }

libdir="$prefix/share/linuxfiles"
share="$prefix/share"

rm -rf "$libdir"
mkdir -p "$libdir"
cp -a "$from/." "$libdir/"

install -Dm755 "$root/packaging/linux/files" "$prefix/bin/files"

# The desktop entry gets an absolute Exec so it works even when ~/.local/bin is not on PATH.
install -d "$share/applications"
sed "s|^Exec=files|Exec=$prefix/bin/files|" \
  "$root/packaging/linux/$app_id.desktop" > "$share/applications/$app_id.desktop"

install -Dm644 "$root/packaging/linux/$app_id.metainfo.xml" "$share/metainfo/$app_id.metainfo.xml"

for icon in "$root"/packaging/linux/icons/hicolor/*/apps/"$app_id.png"; do
  size="$(basename "$(dirname "$(dirname "$icon")")")"
  install -Dm644 "$icon" "$share/icons/hicolor/$size/apps/$app_id.png"
done

if command -v update-desktop-database >/dev/null; then update-desktop-database "$share/applications" || true; fi
if command -v gtk-update-icon-cache >/dev/null; then gtk-update-icon-cache -q -t -f "$share/icons/hicolor" || true; fi

echo "Installed to $libdir; launcher at $prefix/bin/files"
