#!/usr/bin/env bash
# Fetch the repository-pinned Uno fork release into the repo-local NuGet feed.
# Usage: build/fetch-uno-wayland.sh [--version X.Y.Z-wayland.N] [--feed DIR] [--import DIR|--from DIR]
# --import/--from verify local nupkgs offline; SHA256SUMS is an optional cross-check.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="$(sed -n 's/.*<UnoWaylandVersion[^>]*>\([^<]*\)<.*/\1/p' "$root/Directory.Packages.props")"
feed="$root/.cache/uno-wayland-feed"
source_dir=""
while (($#)); do
  case "$1" in
    --version|--feed|--import|--from)
      (($# >= 2)) || { echo "Missing value for $1" >&2; exit 2; }
      case "$1" in
        --version) version="$2" ;;
        --feed) feed="$2" ;;
        --import|--from) source_dir="$2" ;;
      esac
      shift 2 ;;
    -h|--help) sed -n '2,4p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+-wayland\.[0-9]+$ ]] || { echo 'Expected X.Y.Z-wayland.N version' >&2; exit 2; }
# Fail before network access or feed writes when the version has no reviewed pins.
pins="$(python3 "$root/build/uno-wayland-pins.py" "$version")"
declare -A hashes
names=()
while read -r hash name; do
  hashes["$name"]="$hash"
  names+=("$name")
done <<< "$pins"
base="https://github.com/MemerGamer/uno/releases/download/uno-wayland-$version"
mkdir -p "$feed"
exec 9>"$feed/.fetch.lock"
flock 9
stage="$(mktemp -d "$feed/.fetch.XXXXXX")"
trap 'rm -rf -- "$stage"' EXIT
manifest="$feed/.sha256-$version"

fetch() { # asset destination
  if [[ -n "$source_dir" ]]; then
    cp -- "$source_dir/$1" "$2"
  else
    curl --fail --location --silent --show-error --retry 3 \
      --connect-timeout 20 --max-time 600 --proto '=https' --proto-redir '=https' \
      --output "$2" "$base/$1"
  fi
}
cross_check() {
  local hash name extra count=0
  local -A seen=()
  while read -r hash name extra || [[ -n "$hash$name$extra" ]]; do
    if [[ ! "$hash" =~ ^[a-f0-9]{64}$ || -z "$name" || -n "$extra" || -z "${hashes["$name"]:-}" || -n "${seen["$name"]:-}" ]]; then
      echo 'Invalid SHA256SUMS: expected exactly the eight pinned package filenames.' >&2
      return 1
    fi
    [[ "$hash" == "${hashes["$name"]}" ]] || { echo "SHA256SUMS disagrees with committed pin: $name" >&2; return 1; }
    seen["$name"]=1
    ((++count))
  done < "$1"
  [[ $count -eq ${#names[@]} ]] || { echo 'Incomplete SHA256SUMS' >&2; return 1; }
}
matches() { [[ -f "$1" && "$(sha256sum < "$1")" == "$2  -" ]]; }
if [[ -n "$source_dir" ]]; then
  if [[ -f "$source_dir/SHA256SUMS" ]]; then
    fetch SHA256SUMS "$stage/SHA256SUMS"
    cross_check "$stage/SHA256SUMS"
  fi
elif [[ -f "$manifest" ]]; then
  cross_check "$manifest"
fi
for name in "${names[@]}"; do
  hash="${hashes["$name"]}"
  # Explicit imports always verify the supplied bytes, even with a valid feed cache.
  if [[ -z "$source_dir" ]] && matches "$feed/$name" "$hash"; then
    continue
  fi
  if [[ -z "$source_dir" && ! -f "$stage/SHA256SUMS" ]]; then
    fetch SHA256SUMS "$stage/SHA256SUMS"
    cross_check "$stage/SHA256SUMS"
  fi
  fetch "$name" "$stage/$name"
  matches "$stage/$name" "$hash" || { echo "Checksum mismatch against committed pin: $name" >&2; exit 1; }
done
# Expose imports/downloads only after the complete set passes pin verification.
for name in "${names[@]}"; do
  [[ ! -f "$stage/$name" ]] || mv -f -- "$stage/$name" "$feed/$name"
done
[[ ! -f "$stage/SHA256SUMS" ]] || mv -f -- "$stage/SHA256SUMS" "$manifest"
echo "Verified Uno $version packages against repository pins in $feed"
