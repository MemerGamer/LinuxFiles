# Changelog

LinuxFiles is an unofficial native Linux port of [Files](https://github.com/files-community/Files) by the Files
Community. This changelog covers the fork only.

## 0.1.0-alpha1 - 2026-10-06

First alpha. Expect rough edges; please report issues at https://github.com/MemerGamer/LinuxFiles/issues.

### Platform

- The app now runs natively on Linux (x11 / XWayland) on the Uno Platform Skia desktop head instead of WinUI 3.
  The Windows-only code paths are kept but are not built or tested.
- Linux-specific services replace the Win32/COM layer: XDG paths and settings store, folder watching,
  freedesktop trash, XDG thumbnails and icon themes, MIME types and `.desktop` launchers, drives via UDisks2,
  GVfs network and MTP locations, Secret Service credentials, D-Bus notifications and recent files.

### File management

- Browsing, copy/move/delete/rename and undo-friendly file operations on Linux, with fd-relative, symlink-safe
  recursive operations and special-file handling.
- Trash: move to trash, restore and empty, with containment and ownership checks.
- Archives (zip, tar, 7z, rar and more) via SharpCompress, with extraction hardening.
- Search, tags (extended attributes) and a Linux Properties window with POSIX permissions.
- Clipboard and drag and drop with other Linux applications (X11 file clipboard, XDND).
- Native file and folder pickers.

### Desktop integration

- Context menus: Open with, New from templates, Open in terminal.
- Confirmation before running binaries or untrusted launchers; launch arguments are exact-argv, never shell
  interpolated.
- Single instance, and `org.freedesktop.FileManager1` (ShowFolders, ShowItems, ShowItemProperties) so other
  applications can reveal files in LinuxFiles.
- Can be set as the default file manager for `inode/directory`; nothing changes automatically.
- System theme, fonts, SVG icons, folder icons and native window decorations.

### Packaging

- Self-contained tarball, AppImage, Flatpak bundle (`io.github.memergamer.LinuxFiles`) and an AUR binary package
  (`linuxfiles-bin`). Rolling `nightly` builds are published for every push to `main`.

### Known limitations

- x86_64 only; arm64 is wired but untested.
- Wayland sessions run through XWayland.
- Windows-only features (Windows integrations, cloud drive and Store specifics) are not available.
