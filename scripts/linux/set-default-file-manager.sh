#!/usr/bin/env bash
# Makes Files the default handler for folders (inode/directory) for the current user.
# This changes your desktop settings; it is never run by tests or by agents.
#
# Usage: set-default-file-manager.sh [--dry-run] [--restore]
#   (no flag)   remember the current default in ~/.local/state/files/previous-file-manager, then set Files
#   --restore   put the remembered default back
#   --dry-run   only print what would be done
#
# Also enable "Use Files as the default file manager" in Files, so it answers org.freedesktop.FileManager1
# ("Show in folder" from browsers, IDEs, chat apps).
set -euo pipefail

desktop_file="${FILES_DESKTOP_FILE:-io.github.memergamer.LinuxFiles.desktop}"
state_dir="${XDG_STATE_HOME:-$HOME/.local/state}/files"
state_file="$state_dir/previous-file-manager"
dry=0
restore=0
for arg in "$@"; do
	case "$arg" in
		--dry-run) dry=1 ;;
		--restore) restore=1 ;;
		*) echo "unknown option: $arg" >&2; exit 2 ;;
	esac
done

run() {
	if [[ "$dry" == 1 ]]; then echo "would run: $*"; else "$@"; fi
}

command -v xdg-mime >/dev/null || { echo "xdg-mime is required (xdg-utils)" >&2; exit 1; }

if [[ "$restore" == 1 ]]; then
	[[ -f "$state_file" ]] || { echo "nothing to restore" >&2; exit 1; }
	previous="$(<"$state_file")"
	run xdg-mime default "$previous" inode/directory
	exit 0
fi

current="$(xdg-mime query default inode/directory || true)"
echo "current default folder handler: ${current:-none}"
if [[ "$dry" != 1 && -n "$current" && "$current" != "$desktop_file" ]]; then
	mkdir -p "$state_dir"
	printf '%s\n' "$current" >"$state_file"
fi
run xdg-mime default "$desktop_file" inode/directory
