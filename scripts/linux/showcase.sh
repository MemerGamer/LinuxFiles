#!/usr/bin/env bash
# Regenerates the screenshots in docs/linux-port/showcase/ and the captured.txt manifest.
# Runs the app only through headless-run.sh (private Xvfb, sandboxed HOME, no real drives, no real display).
#
# Usage: scripts/linux/showcase.sh [--no-build] [-s startup-seconds]
#   --no-build   reuse the existing build in src/Files.App/bin/Debug/net10.0-desktop
# Env: SHOWCASE_ICON_THEME=<name> renders with that installed icon theme (copied read-only into the sandbox).
# A shot that is missing or identical to the previous frame is recorded as "not yet working".
set -euo pipefail

# Never capture the real home or drives: always run in sandbox mode
unset FILES_REAL_HOME

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

work="${TMPDIR:-/tmp}/files-showcase"
rm -rf "$work"
mkdir -p "$work"
trap 'chmod -R u+w "$work" 2>/dev/null; rm -rf "$work"' EXIT
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
# Small media and archive samples for the preview pane (generated here, nothing is committed)
music="$home/Music"
mkdir -p "$music"
if command -v ffmpeg >/dev/null; then
	ffmpeg -loglevel error -y -f lavfi -i "sine=frequency=440:duration=2" -metadata title="Sample Tone" -metadata artist="Files Test" -metadata album="Showcase" "$music/tone.mp3" || true
	ffmpeg -loglevel error -y -f lavfi -i "sine=frequency=660:duration=2" -metadata title="Sample Flac" -metadata artist="Files Test" -metadata album="Showcase" "$music/tone.flac" || true
fi
if command -v zip >/dev/null; then
	(cd "$d" && zip -q -r "$d/Projects.zip" Projects Reports) || true
fi
# A long folder to exercise scrolling (Videos is otherwise empty)
mkdir -p "$home/Videos/Clips"
for i in $(seq -w 1 300); do : >"$home/Videos/Clips/clip-$i.txt"; done
# Content for the synthetic drives (see showcase-drives.txt)
for m in disk1 disk2 disk3 usb; do mkdir -p "$home/mnt/$m"; done
mkdir -p "$home/mnt/disk1"/{Games,Projects,Backups} "$home/mnt/usb/Photos"
printf 'Level data\n' >"$home/mnt/disk1/Games/save01.dat"
printf 'Holiday album\n' >"$home/mnt/usb/Photos/readme.txt"
# One trashed item (XDG trash spec) in the sandbox trash
t="$home/.local/share/Trash"
mkdir -p "$t/files" "$t/info"
printf 'old draft\n' >"$t/files/old-draft.txt"
printf '[Trash Info]\nPath=%s\nDeletionDate=2026-01-15T09:30:00\n' "$d/old-draft.txt" >"$t/info/old-draft.txt.trashinfo"
# Fixed timestamps keep the Date modified column stable
find "$d" "$pics" -exec touch -d '2026-01-10 12:00:00' {} +
SEED
# Optional: SHOWCASE_ICON_THEME=<name> copies that installed icon theme into the sandbox HOME and selects it
# through the same settings the app reads on a real desktop (kdeglobals, gtk-3.0/gtk-4.0 settings.ini).
if [[ -n "${SHOWCASE_ICON_THEME:-}" ]]; then
	[[ "$SHOWCASE_ICON_THEME" =~ ^[A-Za-z0-9._-]+$ ]] || { echo "invalid SHOWCASE_ICON_THEME" >&2; exit 2; }
	theme_src=""
	for base in "$HOME/.local/share/icons" "$HOME/.icons" /usr/local/share/icons /usr/share/icons; do
		[[ -d "$base/$SHOWCASE_ICON_THEME" ]] && { theme_src="$base/$SHOWCASE_ICON_THEME"; break; }
	done
	[[ -n "$theme_src" ]] || { echo "icon theme not found: $SHOWCASE_ICON_THEME" >&2; exit 2; }
	cat >>"$seed" <<THEME
mkdir -p "\$home/.local/share/icons" "\$home/.config/gtk-3.0" "\$home/.config/gtk-4.0"
cp -rP "$theme_src" "\$home/.local/share/icons/$SHOWCASE_ICON_THEME"
find "\$home/.local/share/icons/$SHOWCASE_ICON_THEME" -type f -exec chmod a-w {} +
printf '[Icons]\nTheme=%s\n' "$SHOWCASE_ICON_THEME" >"\$home/.config/kdeglobals"
printf '[Settings]\ngtk-icon-theme-name=%s\n' "$SHOWCASE_ICON_THEME" | tee "\$home/.config/gtk-3.0/settings.ini" >"\$home/.config/gtk-4.0/settings.ini"
THEME
fi
chmod +x "$seed"

home="$shots/home"
actions="$work/actions.txt"
# Mouse driven (sidebar and cards at 1280x800); the Home page is the startup page.
cat >"$actions" <<A
sleep 6
shot home
# Documents folder, Details then Grid layout
mousemove 113 253 click 1
sleep 5
mousemove 1210 116 click 1
sleep 1
mousemove 892 224 click 1
sleep 1
key Escape
sleep 2
shot folder-details
mousemove 1210 116 click 1
sleep 1
mousemove 1060 224 click 1
sleep 1
key Escape
sleep 3
shot folder-grid
mousemove 1210 116 click 1
sleep 1
mousemove 892 224 click 1
sleep 1
key Escape
sleep 2
# Recycle Bin
mousemove 112 381 click 1
sleep 5
shot recycle-bin
# Settings
mousemove 86 780 click 1
sleep 4
shot settings
# Back in Documents: context menu of a file, then Properties (the wrench in its top row).
# Properties is last: it is a separate window and the bare X server does not repaint what it covered.
mousemove 113 253 click 1
sleep 5
mousemove 400 528 click 3
sleep 2
shot context-menu
mousemove 624 552 click 1
sleep 5
focus
sleep 1
shot properties
A

XVFB_SIZE="${XVFB_SIZE:-1280x800}" FILES_SANDBOX_DRIVES="$repo/scripts/linux/showcase-drives.txt" FILES_SANDBOX_SEED="$seed" "$repo/scripts/linux/headless-run.sh" -s "$seconds" -o "$shots" -a "$actions"

# The runner's home is gone after the run; names below are fixed, so verify each shot
names=(home folder-details folder-grid recycle-bin settings context-menu properties)
# SHOWCASE_SKIP="name name" forces shots to be recorded as "not yet working" when a feature regresses.
broken=" ${SHOWCASE_SKIP:-} "
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
	if [[ "$broken" == *" $n "* ]]; then
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
