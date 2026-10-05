#!/usr/bin/env bash
# Ratchet for Phase 4: builds Files.App with FilesWin32Compat=false and compares its compiler errors with
# docs/linux-port/compat-off-baseline.txt. Errors are keyed by "file: error CODE: message" (no line/column, so edits
# that only move code don't count). Fails if any key is not in the baseline.
#
# Usage: scripts/linux/compat-off-check.sh [--update]
#   --update  rewrite the baseline with the current keys (after errors were fixed, or with reviewer sign-off).
#
# Note: csc stops after declaration errors (unresolved usings, member signatures) without binding method bodies,
# so fixing declaration errors can surface body errors that were hidden before.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
baseline="$root/docs/linux-port/compat-off-baseline.txt"
update=false

case "${1:-}" in
	"") ;;
	--update) update=true ;;
	*) echo "usage: $0 [--update]" >&2; exit 2 ;;
esac

log="$(mktemp)"
errors="$(mktemp)"
trap 'rm -f "$log" "$errors"' EXIT

status=0
MSBUILDDISABLENODEREUSE=1 nice -n 19 dotnet build "$root/src/Files.App" -f net10.0-desktop -p:FilesWin32Compat=false \
	-nodeReuse:false -m:2 -p:UseSharedCompilation=false -clp:NoSummary > "$log" 2>&1 || status=$?

# Distinct "path: error CODE: message" keys with repo-relative paths; drops the line/column, the [project] suffix
# and the duplicates MSBuild prints for each project/target.
grep -E ': error [A-Z]+[0-9]+:' "$log" \
	| sed -E 's/^[[:space:]]*([0-9]+>)?//; s/ \[[^]]*\]$//; s/\([0-9]+,[0-9]+(,[0-9]+,[0-9]+)?\): error /: error /' \
	| sed "s|$root/||g" \
	| LC_ALL=C sort -u > "$errors" || true

count="$(wc -l < "$errors" | tr -d ' ')"

if [ "$status" -ne 0 ] && [ "$count" -eq 0 ]; then
	echo "Build failed without compiler errors; see the log below." >&2
	tail -n 40 "$log" >&2
	exit 1
fi

if $update; then
	{
		echo "# Compiler error keys of: dotnet build src/Files.App -f net10.0-desktop -p:FilesWin32Compat=false"
		echo "# One \"file: error CODE: message\" per line. Regenerate with scripts/linux/compat-off-check.sh --update."
		cat "$errors"
	} > "$baseline"
	echo "Baseline updated: $count error keys."
	exit 0
fi

[ -f "$baseline" ] || { echo "Missing $baseline; run with --update." >&2; exit 1; }
known="$(mktemp)"
trap 'rm -f "$log" "$errors" "$known"' EXIT
grep -vE '^(#|$)' "$baseline" | LC_ALL=C sort -u > "$known" || true

added="$(LC_ALL=C comm -13 "$known" "$errors")"
removed="$(LC_ALL=C comm -23 "$known" "$errors" | wc -l | tr -d ' ')"

if [ -n "$added" ]; then
	echo "New compat-off errors (not in the baseline):" >&2
	echo "$added" | head -n 50 >&2
	exit 1
fi

if [ "$removed" -gt 0 ]; then
	echo "Compat-off errors: $count keys, $removed fixed since the baseline. Run with --update to ratchet it."
else
	echo "Compat-off errors unchanged: $count keys."
fi
