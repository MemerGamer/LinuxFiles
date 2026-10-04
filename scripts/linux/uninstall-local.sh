#!/usr/bin/env bash
# Remove a user-local Files install made by install-local.sh.
# Usage: scripts/linux/uninstall-local.sh [--prefix ~/.local]
set -euo pipefail

prefix="${HOME}/.local"
app_id="io.github.memergamer.LinuxFiles"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix) prefix="$2"; shift 2 ;;
    -h|--help) sed -n '2,3p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

share="$prefix/share"

rm -rf "$share/linuxfiles"
rm -f "$prefix/bin/files"
rm -f "$share/applications/$app_id.desktop" "$share/metainfo/$app_id.metainfo.xml"
rm -f "$share"/icons/hicolor/*/apps/"$app_id.png"

if command -v update-desktop-database >/dev/null; then update-desktop-database "$share/applications" || true; fi
if [[ -d "$share/icons/hicolor" ]] && command -v gtk-update-icon-cache >/dev/null; then
  gtk-update-icon-cache -q -t -f "$share/icons/hicolor" || true
fi

echo "Uninstalled Files from $prefix"
