#!/usr/bin/env bash
# Ratchet for Phase 4: builds Files.App with FilesWin32Compat=false and compares the number of distinct compiler
# errors with docs/linux-port/compat-off-baseline.txt. Fails if the count went up.
#
# Usage: scripts/linux/compat-off-check.sh [--update]
#   --update  rewrite the baseline with the current errors (do this when the count went down, or with reviewer sign-off).
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
	-nodeReuse:false -m:2 -clp:NoSummary > "$log" 2>&1 || status=$?

# Distinct "path(line,col): error CODE: message" entries with repo-relative paths; drops the [project] suffix
# and the duplicates MSBuild prints for each project/target.
grep -E ': error [A-Z]+[0-9]+:' "$log" \
	| sed -E 's/^[[:space:]]*([0-9]+>)?//; s/ \[[^]]*\]$//' \
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
		echo "# Distinct compiler errors of: dotnet build src/Files.App -f net10.0-desktop -p:FilesWin32Compat=false"
		echo "# Regenerate with scripts/linux/compat-off-check.sh --update. The check compares only the count."
		echo "errors: $count"
		cat "$errors"
	} > "$baseline"
	echo "Baseline updated: $count distinct errors."
	exit 0
fi

[ -f "$baseline" ] || { echo "Missing $baseline; run with --update." >&2; exit 1; }
expected="$(sed -nE 's/^errors: ([0-9]+)$/\1/p' "$baseline")"
[ -n "$expected" ] || { echo "No 'errors:' line in $baseline." >&2; exit 1; }

if [ "$count" -gt "$expected" ]; then
	echo "Compat-off errors went up: $count (baseline $expected). New entries:" >&2
	grep -vxF -f <(grep -vE '^(#|errors: )' "$baseline") "$errors" | head -n 50 >&2 || true
	exit 1
fi

if [ "$count" -lt "$expected" ]; then
	echo "Compat-off errors went down: $count (baseline $expected). Run with --update to ratchet the baseline."
else
	echo "Compat-off errors unchanged: $count."
fi
