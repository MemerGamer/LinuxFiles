# Linux port showcase

Files running natively on Linux (Uno Platform, Skia, X11). The screenshots are taken headlessly from a sandboxed
home directory with an empty mount table, so they never show the real machine's files, drives or user name.

Last updated: commit `96267634c`, 2026-10-05 (see `showcase/captured.txt` for the exact shot list).

## What works today

Taken from the merged state of `linux/main` and [PLAN.md](PLAN.md).

- [x] App launches on Linux with the full Files shell: Home page, sidebar, tabs, toolbar, Settings
- [x] File operations through `IFileOperationsService` (copy, move, rename with Linux name rules)
- [x] Trash backend (XDG trash spec) and archives (SharpCompress, hardened extraction)
- [x] Hardened launching: no auto-running executables or untrusted `.desktop` files, exact-argv confirmation dialogs
- [x] File clipboard (gnome-copied-files, uri-list, KDE cut) and outbound drag and drop over XDND
- [x] Properties window with POSIX permissions, hashes and details pages
- [x] Single instance, `org.freedesktop.FileManager1`, recent files, xattr tags, D-Bus notifications, pkexec elevation
- [x] UDisks2 volumes and GVfs network locations in Drives, sidebar, widgets, format and eject

## Not working yet

- [ ] Folder listings do not render on `linux/main` (fix is in PR #28), so Details and Grid layouts cannot be shown
- [ ] Recycle Bin view shows "Did you delete this folder?" instead of the trashed items
- [ ] Properties from a file selection (depends on folder listings)
- [ ] Some UI text still says "Windows" (for example "Open in Windows Terminal")
- [ ] No native Wayland (runs through XWayland), no AT-SPI accessibility

## Screenshots

![Home page](showcase/home.png)

Home page with Quick access. The Drives and Network widgets are empty because the sandbox has no mounts.

![Context menu](showcase/context-menu.png)

Right-click menu on a Quick access card.

![Settings](showcase/settings.png)

Settings page.

Not yet captured (marked "not yet working" in `showcase/captured.txt`): folder in Details layout, folder in Grid
layout, Properties, Recycle Bin.

## How to regenerate

Run `scripts/linux/showcase.sh` (add `--no-build` to reuse the existing build). It uses `scripts/linux/headless-run.sh`,
so nothing touches the real display, and rewrites `docs/linux-port/showcase/`. When the missing features land, remove
them from the `broken` list in the script (or run once with `SHOWCASE_TRY_ALL=1`) and add the new images above.
