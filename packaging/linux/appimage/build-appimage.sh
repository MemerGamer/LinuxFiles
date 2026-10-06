#!/usr/bin/env bash
# Build an AppImage from a self-contained publish.
# Usage: packaging/linux/appimage/build-appimage.sh [--arch x86_64|aarch64] [publish-dir]
# appimagetool and the type2 runtime are downloaded into <repo>/.cache/tools and verified against pinned
# SHA-256 sums. Override with APPIMAGETOOL=/path (then the runtime is still fetched and pinned).
# The tool is itself an AppImage; if FUSE is missing it is run with --appimage-extract-and-run.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
app_id="io.github.memergamer.LinuxFiles"
arch="${ARCH:-x86_64}"
from=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --arch) arch="$2"; shift 2 ;;
    -h|--help) sed -n '2,6p' "$0"; exit 0 ;;
    *) from="$1"; shift ;;
  esac
done

case "$arch" in
  x86_64) rid=linux-x64
    tool_sha=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
    rt_sha=2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d ;;
  aarch64) rid=linux-arm64
    tool_sha=f0837e7448a0c1e4e650a93bb3e85802546e60654ef287576f46c71c126a9158
    rt_sha=00cbdfcf917cc6c0ff6d3347d59e0ca1f7f45a6df1a428a0d6d8a78664d87444 ;;
  *) echo "Unsupported arch: $arch" >&2; exit 2 ;;
esac
from="${from:-$root/artifacts/$rid}"
appdir="$root/artifacts/Files-$arch.AppDir"
out="$root/artifacts/Files-$arch.AppImage"
cache="$root/.cache/tools"

[[ -f "$from/Files.dll" ]] || { echo "No published build at $from (run scripts/linux/publish.sh --rid $rid)" >&2; exit 1; }

fetch() { # url dest sha256
  mkdir -p "$(dirname "$2")"
  if [[ ! -f "$2" ]] || ! echo "$3  $2" | sha256sum -c --status; then
    curl -fsSL -o "$2" "$1"
    echo "$3  $2" | sha256sum -c --status || { rm -f "$2"; echo "Checksum mismatch for $1" >&2; exit 1; }
  fi
}

tool="${APPIMAGETOOL:-}"
if [[ -z "$tool" ]]; then
  tool="$cache/appimagetool-$arch.AppImage"
  fetch "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-$arch.AppImage" "$tool" "$tool_sha"
  chmod +x "$tool"
fi
runtime="$cache/runtime-$arch"
fetch "https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-$arch" "$runtime" "$rt_sha"

rm -rf "$appdir"
mkdir -p "$appdir/usr/bin" "$appdir/usr/lib/linuxfiles" "$appdir/usr/share"
cp -a "$from/." "$appdir/usr/lib/linuxfiles/"
# Portable images cannot install the host polkit policy or privileged helper.
rm -rf "$appdir/usr/lib/linuxfiles/elevation-helper"
rm -f "$appdir/usr/lib/linuxfiles/files-elevation-helper"
touch "$appdir/usr/lib/linuxfiles/.root-actions-disabled"
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
export FILES_DISABLE_ROOT_ACTIONS=1
exec "$here/usr/bin/files" "$@"
RUN
chmod +x "$appdir/AppRun"

rm -f "$out"
run_tool() { # AppImages need FUSE; fall back to extract-and-run where it is unavailable (containers, CI)
  if [[ -e /dev/fuse ]] && command -v fusermount3 >/dev/null 2>&1 || command -v fusermount >/dev/null 2>&1; then
    "$tool" "$@"
  else
    APPIMAGE_EXTRACT_AND_RUN=1 "$tool" "$@"
  fi
}
ARCH="$arch" run_tool --runtime-file "$runtime" "$appdir" "$out"
echo "Built $out ($(du -sh "$out" | cut -f1))"
