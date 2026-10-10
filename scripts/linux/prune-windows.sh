#!/usr/bin/env -S bash
# Copyright (c) Files Community. Licensed under the MIT License.
# Re-enter with normalized input so the checked-in CRLF script runs on Linux.
[[ "${BASH_SOURCE[0]}" == /dev/fd/* ]] || exec bash <(sed 's/\r$//' "${BASH_SOURCE[0]}") "$@" # CRLF bootstrap
set -euo pipefail

apply=false
conflicts_only=false
paths_file=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --apply) apply=true; shift ;;
    --conflicts-only) conflicts_only=true; shift ;;
    --paths-file)
      [[ $# -ge 2 ]] || { echo "--paths-file requires a file" >&2; exit 2; }
      paths_file="$2"; shift 2 ;;
    -h|--help)
      echo "Usage: prune-windows.sh [--apply] [--conflicts-only] [--paths-file FILE]"
      echo "Dry-run by default. Conflict mode keeps only allowlisted deletions made by ours."
      exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

root="$(git rev-parse --show-toplevel)"
paths_file="${paths_file:-$root/scripts/linux/windows-paths.txt}"
# Resolve an explicit list relative to the caller before moving to the repository root.
paths_file="$(realpath "$paths_file")"
cd "$root"

paths=()
while IFS= read -r path || [[ -n "$path" ]]; do
  path="${path%$'\r'}"
  [[ -z "$path" || "$path" == \#* ]] && continue
  if [[ ! "$path" =~ ^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*/?$ ||
        "/$path/" == */../* || "/$path/" == */./* ||
        "$path" == .git || "$path" == .git/* ]]; then
    echo "Unsafe or non-literal path in list: $path" >&2
    exit 2
  fi
  paths+=("$path")
done < "$paths_file"
[[ ${#paths[@]} -gt 0 ]] || { echo "Empty Windows path list" >&2; exit 2; }

allowed() {
  local entry
  for entry in "${paths[@]}"; do
    if [[ "$1" == "$entry" || ( "$entry" == */ && "$1" == "$entry"* ) ]]; then
      return 0
    fi
  done
  return 1
}

selected=()
if [[ "$conflicts_only" == true ]]; then
  git rev-parse -q --verify MERGE_HEAD >/dev/null || { echo "No merge in progress" >&2; exit 2; }
  # DU means deleted by us, modified by them; UD and every other conflict stay unresolved.
  while IFS= read -r -d '' record; do
    status="${record:0:2}"
    path="${record:3}"
    if [[ "$status" == DU ]] && allowed "$path"; then
      selected+=("$path")
    fi
    # Porcelain -z adds a second pathname for renames/copies.
    if [[ "$status" == *R* || "$status" == *C* ]]; then
      IFS= read -r -d '' _ || true
    fi
  done < <(git status --porcelain=v1 -z --untracked-files=no)
else
  git rev-parse -q --verify MERGE_HEAD >/dev/null && { echo "Use --conflicts-only during a merge" >&2; exit 2; }
  for path in "${paths[@]}"; do
    files=()
    mapfile -d '' -t files < <(git --literal-pathspecs ls-files -z -- "$path")
    if [[ ${#files[@]} -gt 0 ]]; then
      selected+=("$path")
      printf '%s: %d tracked file(s)\n' "$path" "${#files[@]}"
    fi
  done
fi

if [[ ${#selected[@]} -eq 0 ]]; then
  echo "No eligible tracked paths."
  exit 0
fi

flags=(-r)
[[ "$conflicts_only" == true ]] && flags+=(-f)
# Preflight the entire operation; normal pruning refuses local/staged modifications.
git --literal-pathspecs rm --dry-run "${flags[@]}" -- "${selected[@]}" >/dev/null
if [[ "$apply" == true ]]; then
  git --literal-pathspecs rm "${flags[@]}" -- "${selected[@]}"
else
  printf 'Would git rm: %s\n' "${selected[@]}"
  echo "Dry run only; pass --apply to stage these deletions."
fi
