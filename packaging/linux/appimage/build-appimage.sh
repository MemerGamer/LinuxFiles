#!/usr/bin/env bash
# Build an AppImage from a self-contained publish (authored, not run in CI yet).
# Requires `appimagetool` on PATH (or APPIMAGETOOL=/path/to/appimagetool).
# Usage: packaging/linux/appimage/build-appimage.sh [artifacts/linux-x64]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
from="${1:-$root/artifacts/linux-x64}"
app_id="io.github.memergamer.LinuxFiles"
appdir="$root/artifacts/Files.AppDir"
tool="${APPIMAGETOOL:-appimagetool}"

[[ -f "$from/Files.dll" ]] || { echo "No published build at $from" >&2; exit 1; }

rm -rf "$appdir"
mkdir -p "$appdir/usr/bin" "$appdir/usr/lib/linuxfiles" "$appdir/usr/share"
cp -a "$from/." "$appdir/usr/lib/linuxfiles/"
install -Dm755 "$root/packaging/linux/files" "$appdir/usr/bin/files"
install -Dm644 "$root/packaging/linux/$app_id.desktop" "$appdir/usr/share/applications/$app_id.desktop"
install -Dm644 "$root/packaging/linux/$app_id.metainfo.xml" "$appdir/usr/share/metainfo/$app_id.metainfo.xml"
cp -a "$root/packaging/linux/icons/hicolor" "$appdir/usr/share/icons"

# AppImage root entries
cp "$root/packaging/linux/$app_id.desktop" "$appdir/$app_id.desktop"
cp "$root/packaging/linux/icons/hicolor/256x256/apps/$app_id.png" "$appdir/$app_id.png"
ln -s "$app_id.png" "$appdir/.DirIcon"

cat > "$appdir/AppRun" <<'RUN'
#!/bin/sh
here="$(dirname "$(readlink -f "$0")")"
export FILES_LIBDIR="$here/usr/lib/linuxfiles"
exec "$here/usr/bin/files" "$@"
RUN
chmod +x "$appdir/AppRun"

arch="${ARCH:-x86_64}"
ARCH="$arch" "$tool" "$appdir" "$root/artifacts/Files-$arch.AppImage"
echo "Built $root/artifacts/Files-$arch.AppImage"
