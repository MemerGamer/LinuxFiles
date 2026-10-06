#!/usr/bin/env bash
# Render the linuxfiles-bin PKGBUILD + .SRCINFO for a release.
# Usage: scripts/linux/gen-aur.sh <version> <dir-with-release-tarballs> [out-dir]
#   <dir> holds files-linux-x64.tar.gz and files-packaging.tar.gz (artifacts/ after publish.sh --tarball
#   plus the packaging bundle). Output: <out-dir>/PKGBUILD and .SRCINFO (default artifacts/aur).
# AUR_BASE_URL overrides the download base (e.g. file:///path) to test the build with makepkg offline.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ver="${1:?version required, e.g. 0.1.0}"
ver="${ver//-/}"  # pkgver may not contain hyphens: 0.1.0-alpha1 -> 0.1.0alpha1
dir="${2:?directory with release tarballs required}"
out="${3:-$root/artifacts/aur}"
tpl="$root/packaging/linux/aur/linuxfiles-bin/PKGBUILD"

sum() { sha256sum "$dir/$1" | cut -d' ' -f1; }
bin_sum="$(sum files-linux-x64.tar.gz)"
pkg_sum="$(sum files-packaging.tar.gz)"

mkdir -p "$out"
python3 - "$tpl" "$out/PKGBUILD" "$ver" "$bin_sum" "$pkg_sum" "${AUR_BASE_URL:-}" <<'PY'
import re, sys
tpl, dst, ver, a, b, base = sys.argv[1:7]
t = open(tpl).read()
t = re.sub(r"^pkgver=.*$", "pkgver=" + ver, t, flags=re.M)
t = re.sub(r"sha256sums=\('SKIP'\n\s+'SKIP'\)", "sha256sums=('%s'\n            '%s')" % (a, b), t)
if base:
    t = re.sub(r'^_base=.*$', '_base="%s"' % base, t, flags=re.M)
open(dst, "w").write(t)
PY
(cd "$out" && makepkg --printsrcinfo > .SRCINFO)
echo "Wrote $out/PKGBUILD and $out/.SRCINFO"
