#!/usr/bin/env bash
# Regenerates the screenshots in docs/linux-port/showcase/ and the captured.txt manifest.
# Runs the app only through headless-run.sh (private Xvfb, sandboxed HOME, no real drives, no real display).
#
# Usage: scripts/linux/showcase.sh [--no-build] [-s startup-seconds]
#   --no-build   reuse the existing build in src/Files.App/bin/Debug/net10.0-desktop
# Shots known broken on this build are recorded as "not yet working" (SHOWCASE_TRY_ALL=1 tries them anyway); a shot identical to the previous frame is skipped too.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$repo/docs/linux-port/showcase"
build=1
seconds=30
while [[ $# -gt 0 ]]; do
	case "$1" in
		--no-build) build=0; shift ;;
		-s) seconds="$2"; shift 2 ;;
		*) echo "unknown argument: $1" >&2; exit 2 ;;
	esac
done

if [[ "$build" == 1 ]]; then
	(cd "$repo" && MSBUILDDISABLENODEREUSE=1 nice -n 19 dotnet build src/Files.App -f net10.0-desktop -nodeReuse:false -m:2 -v:quiet -clp:ErrorsOnly)
fi

work="$(mktemp -d "${TMPDIR:-/tmp}/files-showcase.XXXXXX")"
trap 'rm -rf "$work"' EXIT
shots="$work/shots"
mkdir -p "$shots"

# Deterministic sample content (runs inside the sandbox HOME; nothing from the real machine).
seed="$work/seed.sh"
cat >"$seed" <<'SEED'
#!/usr/bin/env bash
set -euo pipefail
home="$1"
d="$home/Documents"
mkdir -p "$d"/{Projects/Website,Reports,Invoices,Notes,Recipes}
printf 'Quarterly summary\n=================\n\nRevenue is up.\n' >"$d/summary.txt"
printf 'name,qty,price\nApples,3,1.20\nPears,5,0.90\nPlums,12,2.40\n' >"$d/inventory.csv"
printf '# Project notes\n\n- ship the Linux port\n- write the docs\n' >"$d/notes.md"
printf 'Meeting at 10:00\n' >"$d/todo.txt"
printf '<h1>Hello</h1>\n' >"$d/Projects/Website/index.html"
printf 'Q3,Q4\n10,12\n' >"$d/Reports/q3.csv"
printf 'Invoice 0001\n' >"$d/Invoices/invoice-0001.txt"
printf 'Pancakes: flour, milk, eggs\n' >"$d/Recipes/pancakes.txt"
head -c 150000 /dev/zero | tr '\0' 'x' >"$d/big-log.txt"
pics="$home/Pictures"
mkdir -p "$pics"
if command -v ffmpeg >/dev/null; then
	ffmpeg -loglevel error -y -f lavfi -i "gradients=s=640x420:c0=#ff7e5f:c1=#2575fc:seed=1:duration=1:speed=0" -frames:v 1 "$pics/sunset.png" || true
	ffmpeg -loglevel error -y -f lavfi -i "gradients=s=640x420:c0=#11998e:c1=#38ef7d:seed=2:duration=1:speed=0" -frames:v 1 "$pics/forest.png" || true
	ffmpeg -loglevel error -y -f lavfi -i "gradients=s=640x420:c0=#f7971e:c1=#7f00ff:seed=3:duration=1:speed=0" -frames:v 1 "$pics/dusk.jpg" || true
fi
# One trashed item (XDG trash spec) in the sandbox trash
t="$home/.local/share/Trash"
mkdir -p "$t/files" "$t/info"
printf 'old draft\n' >"$t/files/old-draft.txt"
printf '[Trash Info]\nPath=%s\nDeletionDate=2026-01-15T09:30:00\n' "$d/old-draft.txt" >"$t/info/old-draft.txt.trashinfo"
# Fixed timestamps keep the Date modified column stable
find "$d" "$pics" -exec touch -d '2026-01-10 12:00:00' {} +
SEED
chmod +x "$seed"

home="$shots/home"
actions="$work/actions.txt"
# Mouse driven (sidebar and cards at 1280x800); the Home page is the startup page.
cat >"$actions" <<A
sleep 6
shot home
# Context menu of the Documents quick access card
mousemove 900 420 click 1
sleep 1
mousemove 683 180 click 3
sleep 2
shot context-menu
key Escape
sleep 1
# Settings
mousemove 86 780 click 1
sleep 4
shot settings
mousemove 60 114 click 1
sleep 3
A
if [[ "${SHOWCASE_TRY_ALL:-0}" == 1 ]]; then
	# Shots known broken on current builds; the app may crash here, so they run last
	cat >>"$actions" <<A
mousemove 113 253 click 1
sleep 4
key ctrl+shift+1
sleep 2
shot folder-details
key ctrl+shift+4
sleep 3
shot folder-grid
key ctrl+shift+1
sleep 1
mousemove 400 190 click 1
key alt+Return
sleep 3
shot properties
key Escape
sleep 1
mousemove 112 381 click 1
sleep 4
shot recycle-bin
A
fi

XVFB_SIZE="${XVFB_SIZE:-1280x800}" FILES_SANDBOX_SEED="$seed" "$repo/scripts/linux/headless-run.sh" -s "$seconds" -o "$shots" -a "$actions"

# The runner's home is gone after the run; names below are fixed, so verify each shot
names=(home context-menu folder-details folder-grid properties recycle-bin settings)
# Known broken on current linux/main (folder listings and trash view are empty until the file listing work lands).
# SHOWCASE_TRY_ALL=1 captures them anyway; use it once those features are merged and drop them from this list.
broken=" folder-details folder-grid properties recycle-bin "
mkdir -p "$out"
commit="$(git -C "$repo" rev-parse --short HEAD)"
{
	echo "commit: $commit"
	echo "date: $(date -u +%Y-%m-%d)"
} >"$work/manifest.txt"
prev=""
for n in "${names[@]}"; do
	f="$shots/$n.png"
	ok=0
	if [[ "$broken" == *" $n "* && "${SHOWCASE_TRY_ALL:-0}" != 1 ]]; then
		rm -f "$out/$n.png"
		echo "not yet working: $n" >>"$work/manifest.txt"
		prev="$f"
		continue
	fi
	# Skipped when missing, or identical to the previous frame (the action had no visible effect)
	if [[ -s "$f" ]]; then
		ok=1
		if [[ -n "$prev" && -s "$prev" ]] && cmp -s "$f" "$prev"; then ok=0; fi
	fi
	if [[ "$ok" == 1 ]]; then
		cp "$f" "$out/$n.png"
		echo "captured: $n" >>"$work/manifest.txt"
	else
		rm -f "$out/$n.png"
		echo "not yet working: $n" >>"$work/manifest.txt"
	fi
	prev="$f"
done
cp "$work/manifest.txt" "$out/captured.txt"
# Debugging aid: SHOWCASE_KEEP_RAW=<dir> keeps every raw frame, including skipped shots
if [[ -n "${SHOWCASE_KEEP_RAW:-}" ]]; then mkdir -p "$SHOWCASE_KEEP_RAW"; cp "$shots"/*.png "$SHOWCASE_KEEP_RAW"/; fi
cat "$out/captured.txt"
echo "Review every PNG in $out before committing (no real names, paths or drives)."
