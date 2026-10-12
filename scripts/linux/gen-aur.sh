#!/usr/bin/env bash
# Render a checksum-pinned binary or source PKGBUILD + .SRCINFO for a release.
# Usage: scripts/linux/gen-aur.sh <version> <dir-with-release-tarballs> [out-dir] [binary|source]
#   <dir> holds files-linux-x64.tar.gz and files-packaging.tar.gz (artifacts/ after publish.sh --tarball
#   plus the packaging bundle). Output: <out-dir>/PKGBUILD and .SRCINFO (default artifacts/aur).
# Source mode requires linuxfiles-<version>.tar.gz, the exact GitHub tag archive.
# AUR_BASE_URL overrides the download base (e.g. file:///path) to test the build with makepkg offline.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
tag="${1:?version required, e.g. 0.1.0 or 0.1.0-alpha1}"
[[ "$tag" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$ ]] || { echo "Invalid version: $tag" >&2; exit 2; }
if [[ -n "${AUR_BASE_URL:-}" && ! "$AUR_BASE_URL" =~ ^[A-Za-z0-9:/._~%@+-]+$ ]]; then
	echo "Invalid AUR_BASE_URL" >&2; exit 2
fi
dir="${2:?directory with release tarballs required}"
out="${3:-$root/artifacts/aur}"
kind="${4:-binary}"
case "$kind" in
  binary) tpl="$root/packaging/linux/aur/linuxfiles-bin/PKGBUILD" ;;
  source) tpl="$root/packaging/linux/aur/linuxfiles/PKGBUILD" ;;
  *) echo "Recipe kind must be binary or source" >&2; exit 2 ;;
esac

sum() { sha256sum < "$dir/$1" | cut -d' ' -f1; }
if [[ "$kind" == source ]]; then
  bin_sum="$(sum "linuxfiles-$tag.tar.gz")"
  pkg_sum="$bin_sum"
else
  bin_sum="$(sum files-linux-x64.tar.gz)"
  pkg_sum="$(sum files-packaging.tar.gz)"
fi

mkdir -p "$out"
python3 - "$tpl" "$out/PKGBUILD" "$tag" "$bin_sum" "$pkg_sum" "${AUR_BASE_URL:-}" "$kind" <<'PY'
import re, sys
tpl, dst, ver, a, b, base, kind = sys.argv[1:8]
if not all(re.fullmatch(r'[0-9a-f]{64}', digest) for digest in (a, b)):
    raise SystemExit('Invalid release checksum')
t = open(tpl).read()
t = re.sub(r"^_tag=.*$", lambda m: "_tag=" + ver, t, flags=re.M)
t = re.sub(r"^pkgver=.*$", lambda m: "pkgver=" + ver.replace("-", ""), t, flags=re.M)
if kind == 'source':
    t = re.sub(r'^pkgver=.*$', lambda m: 'pkgver=' + ver.replace('-', ''), t, flags=re.M)
    t = re.sub(r'^source=.*$', lambda m: 'source=("$pkgname-$pkgver.tar.gz::$url/archive/refs/tags/linux-v' + ver + '.tar.gz")', t, flags=re.M)
    t = t.replace('LinuxFiles-linux-v$pkgver', 'LinuxFiles-linux-v' + ver)
    checksums = [a]
else:
    checksums = [a, b]
t = re.sub(r"sha256sums=\([^)]*\)", lambda m: "sha256sums=(" + ' '.join("'" + x + "'" for x in checksums) + ")", t)
if 'UNGENERATED' in re.search(r'^sha256sums=.*$', t, re.M).group() or re.search(r"['\"]SKIP['\"]", t):
    raise SystemExit('Recipe checksums are not pinned')
if base and kind == 'source':
    t = re.sub(r'^source=.*$', lambda m: 'source=("$pkgname-$pkgver.tar.gz::' + base + '/linuxfiles-' + ver + '.tar.gz")', t, flags=re.M)
if base and kind == 'binary':
    t = re.sub(r'^_base=.*$', lambda m: '_base="%s"' % base, t, flags=re.M)
open(dst, "w").write(t)
PY
# makepkg only exists on Arch; elsewhere (CI runs on Ubuntu) emit the same fields by sourcing the PKGBUILD.
srcinfo() {
	if command -v makepkg >/dev/null 2>&1 && [[ "${AUR_SRCINFO_FALLBACK:-}" != 1 ]]; then
		(cd "$out" && makepkg --printsrcinfo)
		return
	fi
	(
		# shellcheck disable=SC1091
		source "$out/PKGBUILD"
		list() { local k="$1"; shift; local v; for v in "$@"; do [[ -n "$v" ]] || continue; printf '\t%s = %s\n' "$k" "$v"; done; }
		printf 'pkgbase = %s\n' "$pkgname"
		printf '\tpkgdesc = %s\n\tpkgver = %s\n\tpkgrel = %s\n\turl = %s\n' "$pkgdesc" "$pkgver" "$pkgrel" "$url"
		list arch "${arch[@]}"
		list license "${license[@]}"
		list depends "${depends[@]}"
		list makedepends "${makedepends[@]-}"
		list optdepends "${optdepends[@]}"
		list provides "${provides[@]-}"
		list conflicts "${conflicts[@]-}"
		list noextract "${noextract[@]-}"
		list options "${options[@]}"
		list source "${source[@]}"
		list sha256sums "${sha256sums[@]}"
		printf '\npkgname = %s\n' "$pkgname"
	)
}
srcinfo > "$out/.SRCINFO"
echo "Wrote $out/PKGBUILD and $out/.SRCINFO"
