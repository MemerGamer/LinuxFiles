# Changelog

LinuxFiles is an unofficial native Linux port of [Files](https://github.com/files-community/Files) by the Files
Community. This changelog covers the fork only.

## 0.1.0-alpha2 - 2026-10-08

Second alpha, driven by feedback on the first. Expect rough edges and keep backups. Report issues at
https://github.com/MemerGamer/LinuxFiles/issues.

### Features

- Optional Adwaita-style appearance (Settings > Appearance): libadwaita light and dark palettes, rounded controls
  and more spacing, applied by overriding existing resources. Off by default.
- Window opacity setting (Linux, needs a compositing window manager). The Backdrop material card is hidden on
  Linux because it did nothing.
- Nix flake (`nix run github:MemerGamer/LinuxFiles`) wrapping the release tarball. Root actions are off in the
  Nix package.
- `FILES_RENDERER=software|opengl|gles|vulkan` to override the rendering backend.

### Fixes

- The app background image setting now shows the image (the path was never decoded and the light file area
  covered it). Images are loaded with size and pixel limits, special files are rejected and animations use their
  first frame.
- The glitching icon at the top left of the tab bar: two buttons were drawn on top of each other, and the hidden
  one swallowed the clicks meant for the tab actions menu.
- Systems without GPU acceleration use the software renderer, which roughly halves startup CPU use and
  navigation cost.
- Fractional `Xft.dpi` values such as `144.0` are honoured instead of falling back to 100 percent scale.
- Holding Page Down in a large folder no longer freezes repainting (repeats are coalesced), and the Details column
  header stays visible when the list is scrolled.
- The root helper installer refuses to overwrite files owned by a distro package, and the AppImage and Flatpak
  builds only strip the root-mode desktop action instead of everything after it.

### Known limitations

- Text can look soft at 100 percent scale: Uno's font hinting settings are not configurable. With fractional
  scaling under XWayland, set `Xft.dpi` (KDE: apply scaling themselves) to avoid compositor upscaling.
- Window opacity fades the whole window, and compositors that ignore the property show no change.

## 0.1.0-alpha1 - 2026-10-06

First public alpha. Expect rough edges and do not rely on it for data you cannot afford to lose. Please report
issues at https://github.com/MemerGamer/LinuxFiles/issues. Downloads and install instructions are on the
[release page](https://github.com/MemerGamer/LinuxFiles/releases/tag/linux-v0.1.0-alpha1).

### Highlights

- Files runs natively on Linux (X11 / XWayland) on the Uno Platform Skia desktop head instead of WinUI 3.
- Grouped views are virtualized: a 10,000-file folder grouped in the Grid went from 90 s to 1.7 s, and grouping
  in Downloads works again.
- KDE service menus appear under "Show more options", filtered per action by the current shell.
- Root actions through a polkit-authenticated helper, with the prompt bound to the exact operation, plus a root
  mode (`files --root`) and "Open in terminal as root".
- Native file pickers, opening files inside archives, tabs in a client-side title bar and the Win11 icon theme.
- Packages for Arch (AUR `linuxfiles-bin`), AppImage, Flatpak and a self-contained tarball.

### Features

Platform

- Linux-specific services replace the Win32/COM layer: XDG paths and settings store, folder watching,
  freedesktop trash, XDG thumbnails and icon themes, MIME types and `.desktop` launchers, drives via UDisks2,
  GVfs network and MTP locations, Secret Service credentials, D-Bus notifications and recent files.
- The Windows-only code paths are kept but are not built or tested.

File management

- Browsing, copy/move/delete/rename and undo-friendly file operations, with fd-relative, symlink-safe recursive
  operations and special-file handling.
- Trash: move to trash, restore and empty, with containment and ownership checks.
- Archives (zip, tar, 7z, rar and more) via SharpCompress with hardened extraction. Browse inside archives, open
  files from them (read-only) and use the Extract actions.
- Search, tags (extended attributes), file previews and a Linux Properties window with POSIX permissions.
- Clipboard and drag and drop with other Linux applications (X11 file clipboard, XDND).
- Native file and folder pickers through the desktop portal.

Desktop integration

- KDE service menus under "Show more options", with per-action shell filtering.
- Context menus: Open with, New from templates, Open in terminal.
- Root mode: `files --root`, the "Open in Root Mode" desktop action, or simply running as root (the tab and title
  show "(Root mode)" or "(Running as root)").
- "Open in terminal as root" using run0, sudo or pkexec, starting root's login shell.
- Single instance, and `org.freedesktop.FileManager1` (ShowFolders, ShowItems, ShowItemProperties) so other
  applications can reveal files in LinuxFiles.
- Can be set as the default file manager for `inode/directory`; nothing changes automatically.

Look and feel

- A client-side title bar with tabs, a live window title and a Home-rooted breadcrumb.
- The Win11 icon theme is supported; folder icons always show with Win11-style themes.
- HiDPI scale detection from the desktop settings, labelled Sort and Layout toolbar buttons, accessibility names
  on controls and a translation fallback to English for missing strings.
- System theme, bundled UI font, SVG icons and window decorations.
- Settings changed in one window are merged with the other windows instead of overwriting them.

### Fixes

- Grouped file lists render and scroll quickly in the Grid, List and Cards layouts.
- A slow second click on a file no longer starts a rename by mistake.
- Column widths in Details are persisted correctly.
- Large folders with many PDFs load faster.
- Renames work on NFS/FUSE filesystems without hardlinks, and cross-device trash restores are staged safely.
- Toolbar icons and the toolbar row, Sort/Group/View buttons, status bar sizes and Recycle Bin columns.
- Keyboard shortcuts, Enter in the address bar and typed paths; layout shortcuts and Settings tab reuse.
- Light-theme grid selection, list row height, Grid labels and Details alignment.
- Flatpak: XDG user folders (Desktop, Downloads, ...) are found reliably, also without a `user-dirs.dirs` file.

### Packaging

- Self-contained tarball (`files-linux-x64.tar.gz`), AppImage (`Files-x86_64.AppImage`), Flatpak bundle
  (`Files-x86_64.flatpak`, app id `io.github.memergamer.LinuxFiles`) and the AUR binary package `linuxfiles-bin`.
  `SHA256SUMS`, `PKGBUILD` and `SRCINFO` are attached to the release.
- The launcher command is `files`.
- Tagged-release CI is idempotent (re-running replaces the assets) and the AUR recipe generation was fixed.
- A rolling `nightly` pre-release is published for every push to `main`.

### Security

- Root actions run through a root-owned, non-setuid helper behind a polkit action that requires administrator
  authentication every time; the prompt names the exact operation. The GUI stays unprivileged.
- AppImage and Flatpak never include the helper, so root actions are disabled there.
- Confirmation before running binaries or untrusted launchers; launch arguments are exact-argv, never shell
  interpolated.
- The preview pane never opens device files, even under a path-swap race.
- Archive extraction, trash and recursive file operations are hardened against symlink and path-swap tricks.

### Known issues

- In the light theme, toolbar buttons can fade when they have focus.
- The Grid context menu acrylic can show an olive smear.
- The Properties dialog has no backdrop.
- Long file names wrap in the Details layout.
- The length of the polkit prompt depends on your authentication agent.
- Wayland sessions run through XWayland; there is no native Wayland support or AT-SPI accessibility yet.
- The Windows build has not been restored (planned for phase 8).
- x86_64 only; arm64 is wired but untested.
- Windows-only features (Windows integrations, cloud drive and Store specifics) are not available.
