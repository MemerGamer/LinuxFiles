# Linux packaging (Phase 6 groundwork)

App id (used everywhere): `io.github.memergamer.LinuxFiles`. Binary/launcher: `files`. Assembly: `Files.dll`
(apphost `Files`).

## Layout

| Path | Purpose |
|---|---|
| `packaging/linux/io.github.memergamer.LinuxFiles.desktop` | Desktop entry (`inode/directory`, new-window action). |
| `packaging/linux/io.github.memergamer.LinuxFiles.metainfo.xml` | AppStream metadata. |
| `packaging/linux/icons/hicolor/<N>x<N>/apps/` | Icons 16, 24, 32, 48, 64, 128, 256, 512 (PNG). |
| `packaging/linux/files` | Launcher: finds `Files.dll` (`FILES_LIBDIR`, next to itself, `../share/linuxfiles`, `/usr/lib/linuxfiles`, ...) and execs the apphost if present, else `dotnet Files.dll`. |
| `packaging/linux/flatpak/` | Flatpak manifest. |
| `packaging/linux/aur/` | `PKGBUILD` + `.SRCINFO`. |
| `packaging/linux/appimage/build-appimage.sh` | AppImage builder (needs `appimagetool`). |
| `scripts/linux/publish.sh` | `dotnet publish` to `artifacts/linux-<rid>/`. |
| `scripts/linux/install-local.sh`, `uninstall-local.sh` | User-local install into `~/.local`. |
| `scripts/linux/gen-icons.sh` | Regenerates the icons. |
| `.github/workflows/package-linux.yml` | Manual / `linux-v*` tag: publish linux-x64, upload tarball. |

## Icons

The repo has no SVG logo, so there is no `scalable/` icon. Sizes 16/24/32/48/256 are copied from
`Assets/AppTiles/Release/Square44x44Logo.targetsize-*.png`; 64/128/512 are downscaled from the 600 px
`Square150x150Logo.scale-400.png` (so 512 is a downscale).
A proper SVG should replace these when design provides one.

## Publishing

```
scripts/linux/publish.sh                        # self-contained linux-x64 -> artifacts/linux-x64/
scripts/linux/publish.sh --rid linux-arm64      # arm64 (cross-publish; not tested)
scripts/linux/publish.sh --framework-dependent  # needs a system dotnet 10 runtime
scripts/linux/publish.sh --tarball              # also writes artifacts/files-<rid>.tar.gz
```

Trimming and AOT are explicitly disabled (`PublishTrimmed=false`, `PublishAot=false`). Measured on the
`linux/l-pkg` branch: self-contained linux-x64 is about 155 MB unpacked (276 files), 63 MB as `.tar.gz`. The publish
emits a few ILxxxx analyzer warnings (IL2026 for `Microsoft.Xaml.Interactivity` behaviours and
`Assembly.GetType`, IL2072 for `Activator.CreateInstance`/`PageStackEntry`, IL2122) plus many `Uno0001`
not-implemented warnings; these matter only if trimming/AOT is enabled later.

## Local install

`scripts/linux/install-local.sh [--from DIR] [--prefix ~/.local]` copies the build to `<prefix>/share/linuxfiles`,
the launcher to `<prefix>/bin/files`, and installs the desktop entry (with an absolute `Exec`), metainfo and icons,
then runs `update-desktop-database` and `gtk-update-icon-cache` when present. `uninstall-local.sh` reverses it.

## WM_CLASS / StartupWMClass

Uno's X11 host calls `XSetClassHint` but the class value was not verified at runtime here. The desktop entry assumes
`StartupWMClass=Files` (assembly/process name). Verify on a real X11 session with `xprop WM_CLASS` (click the window)
and adjust the `.desktop` if it differs; otherwise taskbar grouping and icon matching will fail.

## Flatpak

`packaging/linux/flatpak/io.github.memergamer.LinuxFiles.yml`, runtime `org.freedesktop.Platform//25.08` with the
`org.freedesktop.Sdk.Extension.dotnet10` extension (the extension version availability is unverified). It is
authored only; not built. Flathub needs an offline build, so a `nuget-sources.json` from `flatpak-dotnet-generator`
must replace the `--share=network` build arg.

`finish-args` rationale:

- `--share=ipc`, `--socket=x11`, `--device=dri`: Uno Skia desktop is X11 only (XWayland on Wayland) and uses GL.
- `--filesystem=host`, `--filesystem=xdg-run/gvfs`, `xdg-run/gvfs-fuse`: a file manager needs the whole filesystem
  plus mounted network/MTP volumes.
- `--own-name=org.freedesktop.FileManager1`: reserved for the Phase 6 D-Bus service.
- `--talk-name=org.freedesktop.Notifications`: toasts.
- `--talk-name=org.freedesktop.Flatpak`: allows `flatpak-spawn --host` to launch host applications (Open With,
  terminals, `gnome-disks`). Trade-off: it gives the app arbitrary command execution on the host, effectively
  voiding the sandbox. Alternative is the OpenURI/OpenWith portals, which cannot cover all cases; decide in Phase 6.

## AUR

`packaging/linux/aur/PKGBUILD` builds from the `linux-v<ver>` tag tarball with `dotnet-sdk`, installing to
`/usr/lib/linuxfiles` with `/usr/bin/files`. `sha256sums` is `SKIP` until a release exists; regenerate `.SRCINFO` with
`makepkg --printsrcinfo` after edits. Not built here (no Arch tooling).

## AppImage

`packaging/linux/appimage/build-appimage.sh` assembles an AppDir from the publish output (AppRun sets
`FILES_LIBDIR`) and calls `appimagetool`. Because the build is self-contained, no extra bundling is needed beyond
host libraries (fontconfig, X11, GL), which AppImage expects from the host. Authored only.

## Becoming the default file manager (Phase 6, not done yet)

1. `inode/directory`: already in the desktop entry's `MimeType=`. Users (or the installer's post-install hook)
   then run `xdg-mime default io.github.memergamer.LinuxFiles.desktop inode/directory`. Do not do this
   automatically from the package scripts.
2. `org.freedesktop.FileManager1`: implement `ShowFolders`, `ShowItems`, `ShowItemProperties` on the session bus
   (Phase 6 task, in `Files.Platform.Linux`). To make it activatable when the app is not running, install
   `/usr/share/dbus-1/services/org.freedesktop.FileManager1.service` with `Name=org.freedesktop.FileManager1`,
   `Exec=/usr/bin/files --dbus-activate` (flag to be defined). The Flatpak equivalent is installed under
   `/app/share/dbus-1/services/` and covered by the `--own-name` finish-arg already present.
3. Each package recipe above needs the `.service` file added to its install step at that point.

## Open questions

- Real `WM_CLASS` value on X11 (see above).
- Whether an SVG logo can be supplied for `scalable/`.
- Flatpak SDK extension availability for .NET 10 on the chosen runtime.
- arm64 publishing has not been tested.
