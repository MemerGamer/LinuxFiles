#!/usr/bin/env bash
# Runs Files on a private Xvfb display (never the user's screen), optionally sends xdotool input,
# takes screenshots, and cleans everything up.
#
# Usage: headless-run.sh [-s seconds] [-o outdir] [-a actions-file] [-- app args...]
#   actions-file: one xdotool command per line (e.g. "mousemove 100 200 click 1", "key ctrl+l",
#                 "type /etc", "sleep 2", "shot name", "run <cmd args>" = run a helper with DISPLAY set to the private display), run against the private display.
# Env: FILES_SANDBOX_DRIVES (fixture of synthetic drives, see scripts/linux/showcase-drives.txt),
#      FILES_SANDBOX_SEED (optional script run as "script <sandbox-home>" after the default sample content is created),
#      FILES_BIN (default src/Files.App/bin/Debug/net10.0-desktop), XVFB_SIZE (default 1600x1000).
set -euo pipefail

seconds=25
outdir="${TMPDIR:-/tmp}/files-headless"
actions=""
while getopts "s:o:a:" opt; do
	case "$opt" in
		s) seconds="$OPTARG" ;;
		o) outdir="$OPTARG" ;;
		a) actions="$OPTARG" ;;
		*) exit 2 ;;
	esac
done
shift $((OPTIND - 1))
[[ "${1:-}" == "--" ]] && shift

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
bin="${FILES_BIN:-$repo/src/Files.App/bin/Debug/net10.0-desktop}"
size="${XVFB_SIZE:-1600x1000}"
mkdir -p "$outdir"
outdir="$(cd "$outdir" && pwd)"

# Pick a free display number >= 99.
display=99
while [[ -e "/tmp/.X11-unix/X$display" || -e "/tmp/.X$display-lock" ]]; do display=$((display + 1)); done

Xvfb ":$display" -screen 0 "${size}x24" -nolisten tcp >"$outdir/xvfb.log" 2>&1 &
xvfb_pid=$!
app_pid=""
cleanup() {
	# The app runs in its own process group (setsid), so this also stops dbus-run-session and its daemon.
	[[ -n "$app_pid" ]] && kill -- "-$app_pid" 2>/dev/null || true
	sleep 1
	kill "$xvfb_pid" 2>/dev/null || true
}
trap cleanup EXIT
sleep 1

shot() {
	ffmpeg -loglevel error -y -f x11grab -video_size "$size" -i ":$display" -frames:v 1 "$outdir/$1.png"
	echo "screenshot: $outdir/$1.png"
}

# By default run against a throwaway HOME with sample files so tests never touch the user's real
# files, settings or trash, and screenshots never show personal file names. FILES_REAL_HOME=1 opts out.
sandbox_env=()
if [[ "${FILES_REAL_HOME:-0}" != "1" ]]; then
	home="$outdir/home"
	rm -rf "$home"
	mkdir -p "$home"/{Desktop,Documents/Projects,Downloads,Music,Pictures,Videos,Templates,.config,.local/share,.cache}
	printf 'Hello from Files on Linux\n' >"$home/Documents/readme.txt"
	printf 'a,b\n1,2\n' >"$home/Documents/data.csv"
	printf '# Notes\n' >"$home/Documents/Projects/notes.md"
	printf '#!/bin/sh\necho hi\n' >"$home/Downloads/script.sh"
	head -c 2048 /dev/urandom >"$home/Downloads/archive.bin"
	ln -sf "$home/Documents" "$home/Desktop/Documents link"
	# Optional: FILES_SANDBOX_SETUP=/path/script.sh runs with HOME pointing at the sandbox (e.g. to create a repository)
	[[ -n "${FILES_SANDBOX_SETUP:-}" ]] && HOME="$home" bash "$FILES_SANDBOX_SETUP"
	mkdir -p -m 0700 "$home/.runtime"
	# Synthetic drives instead of the real mounts (HeadlessDriveFixture; honoured only together with FILES_HEADLESS=1 and this HOME).
	# FILES_SANDBOX_DRIVES names a fixture file; without it the drive list is empty.
	mkdir -p "$home/mnt"
	if [[ -n "${FILES_SANDBOX_DRIVES:-}" ]]; then
		cp "$FILES_SANDBOX_DRIVES" "$home/.drives.txt"
		while IFS='|' read -r _ _ _ _ _ dir; do
			[[ "$dir" =~ ^[A-Za-z0-9_-]+$ ]] && mkdir -p "$home/mnt/$dir"
		done < <(grep -v '^#' "$home/.drives.txt")
	else
		: >"$home/.drives.txt"
	fi
	# Never reach the real system bus (UDisks2 could mount/unmount real disks) or the real gvfs/runtime dir.
	sandbox_env=(HOME="$home" XDG_CONFIG_HOME="$home/.config" XDG_DATA_HOME="$home/.local/share" XDG_CACHE_HOME="$home/.cache" \
		XDG_RUNTIME_DIR="$home/.runtime" DBUS_SYSTEM_BUS_ADDRESS="unix:path=/nonexistent" GIO_USE_VFS=local GVFS_DISABLE_FUSE=1 FILES_HEADLESS=1 FILES_HEADLESS_ROOT="$outdir" FILES_HEADLESS_DRIVES="$home/.drives.txt")
	# The seed runs with a scrubbed environment: sandbox HOME/XDG, the private display, no bus or Wayland variables.
	if [[ -n "${FILES_SANDBOX_SEED:-}" ]]; then
		env -i PATH="$PATH" LANG=C.UTF-8 "${sandbox_env[@]}" DISPLAY=":$display" "$FILES_SANDBOX_SEED" "$home"
	fi
fi

# Private D-Bus session: notifications, portals and app launches never reach the real desktop session.
# shellcheck disable=SC2016
setsid bash -c 'cd "$0" && exec "$@"' "$bin" \
	env -u WAYLAND_DISPLAY -u DBUS_SESSION_BUS_ADDRESS -u FILES_GVFS_DIR "${sandbox_env[@]}" DISPLAY=":$display" LIBGL_ALWAYS_SOFTWARE=1 \
	FILES_LAUNCH_DRYRUN="${FILES_LAUNCH_DRYRUN:-1}" nice -n 19 dbus-run-session -- dotnet Files.dll "$@" \
	>"$outdir/app.log" 2>&1 &
app_pid=$!

sleep "$seconds"
# Xvfb has no window manager, so nothing has keyboard focus: give it to the Files window explicitly
# (XSetInputFocus works without a WM). Re-run with the "focus" action after opening new windows.
focus_files() {
	local win
	win="$(DISPLAY=":$display" xdotool search --class Files 2>/dev/null | tail -1 || true)"
	[[ -n "$win" ]] && DISPLAY=":$display" xdotool windowfocus --sync "$win" 2>/dev/null || true
}
focus_files
if [[ -n "$actions" ]]; then
	while IFS= read -r line || [[ -n "$line" ]]; do
		[[ -z "$line" || "$line" == \#* ]] && continue
		case "$line" in
			sleep\ *) sleep "${line#sleep }" ;;
			shot\ *) shot "${line#shot }" ;;
			exec\ *) # Runs ONE program (TAB-separated argv so arguments may contain spaces; no shell, no expansion) in the sandboxed HOME with a scrubbed
				# environment, "exec " then the program and its arguments separated by TABs, e.g. exec touch<TAB>Documents/new report.txt. Never sees the caller's display, D-Bus or XDG variables.
				[[ "${FILES_REAL_HOME:-0}" == "1" ]] && { echo "exec: refused with FILES_REAL_HOME=1" >&2; continue; }
				IFS=$'\t' read -ra exec_argv <<<"${line#exec }"
				[[ ${#exec_argv[@]} -eq 0 ]] && continue
				(cd "$home" && env -i PATH="$PATH" HOME="$home" XDG_CONFIG_HOME="$home/.config" XDG_DATA_HOME="$home/.local/share" \
					XDG_CACHE_HOME="$home/.cache" XDG_RUNTIME_DIR="$home/.runtime" DISPLAY=":$display" \
					DBUS_SESSION_BUS_ADDRESS="unix:path=/nonexistent" DBUS_SYSTEM_BUS_ADDRESS="unix:path=/nonexistent" \
					GIO_USE_VFS=local GVFS_DISABLE_FUSE=1 "${exec_argv[@]}") ;;
			focus) focus_files ;;
			run\ *) # a helper program on the private display (e.g. an X clipboard client); word-split on purpose
				# shellcheck disable=SC2086
				DISPLAY=":$display" ${line#run } ;;
			*) # shellcheck disable=SC2086
				DISPLAY=":$display" xdotool $line ;;
		esac
	done <"$actions"
fi
shot final
kill -0 "$app_pid" 2>/dev/null && echo "app: still running" || echo "app: exited (see $outdir/app.log)"
