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
| `packaging/linux/flatpak/` | Flatpak manifest (repackages the publish tarball). |
| `packaging/linux/aur/linuxfiles-bin/PKGBUILD` | AUR binary package (release tarball); `linuxfiles/PKGBUILD` is the unverified from-source variant. |
| `packaging/linux/appimage/build-appimage.sh` | AppImage builder; downloads pinned `appimagetool` + type2 runtime into `.cache/tools`. |
| `scripts/linux/build-flatpak.sh`, `gen-aur.sh` | Build the Flatpak bundle; render PKGBUILD + `.SRCINFO` with real checksums. |
| `scripts/linux/publish.sh` | `dotnet publish` to `artifacts/linux-<rid>/`. |
| `scripts/linux/install-local.sh`, `uninstall-local.sh` | User-local install into `~/.local`. |
| `scripts/linux/gen-icons.sh` | Regenerates the icons. |
| `.github/workflows/package-linux.yml` | Manual / `linux-v*` tag: publish, AppImage, Flatpak, AUR recipe; tags also create a GitHub release. |

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

## Building the packages

All three consume the output of `scripts/linux/publish.sh --tarball` (`artifacts/files-linux-x64.tar.gz`), and
CI runs the same scripts (job `package-linux-x64`; on a `linux-v*` tag the `release` job attaches
`files-linux-x64.tar.gz`, `files-packaging.tar.gz`, `Files-x86_64.AppImage` and `Files-x86_64.flatpak` to a GitHub
release). Verified locally on Arch: all three build.

## AppImage

`packaging/linux/appimage/build-appimage.sh [--arch x86_64|aarch64] [publish-dir]` assembles an AppDir (AppRun sets
`FILES_LIBDIR`; the runtime is self-contained, host libs such as fontconfig/X11/GL come from the system) and runs
`appimagetool`. The tool (1.9.1) and the type2 runtime (20251108) are downloaded into the git-ignored `.cache/tools`
and verified against SHA-256 sums pinned in the script (they match the digests GitHub publishes for those release
assets). Nothing is installed system-wide; without FUSE the tool runs via `APPIMAGE_EXTRACT_AND_RUN`. Output:
`artifacts/Files-<arch>.AppImage` (about 61 MB). arm64 is wired (pinned sums) but only x86_64 was built.

Smoke run on the private Xvfb display (the Home page renders):

```
FILES_EXEC=artifacts/Files-x86_64.AppImage scripts/linux/headless-run.sh -s 25 -o /tmp/files-appimage
```

`headless-run.sh` honours `FILES_EXEC` (any launcher, including `packaging/linux/files`) instead of `dotnet Files.dll`;
for `*.AppImage` it sets `APPIMAGE_EXTRACT_AND_RUN=1` and `APPIMAGELAUNCHER_DISABLE=1` so a host AppImageLauncher
cannot pop up an integration dialog.

## Flatpak

`packaging/linux/flatpak/io.github.memergamer.LinuxFiles.yml`, runtime `org.freedesktop.Platform//25.08`. The module
unpacks the self-contained publish tarball, so there is no .NET SDK, NuGet restore or network access in the build
(Flathub-friendly: for a release, point the `file` source at the release URL with its sha256).
`scripts/linux/build-flatpak.sh [--bundle]` runs `flatpak-builder` (needs the 25.08 Platform and Sdk installed) into
`artifacts/flatpak-repo` and optionally writes `artifacts/Files-x86_64.flatpak`; without `flatpak-builder` it only
validates the manifest structure. Built locally; not yet installed or run inside the sandbox.

`finish-args` rationale (each line is the minimum for a feature that exists in the code):

- `--share=ipc`, `--socket=x11`, `--device=dri`: Uno Skia desktop is X11 only (XWayland on Wayland) and uses GL.
- `--filesystem=host`: a file manager needs the whole filesystem. `--filesystem=xdg-run/gvfs`, `xdg-run/gvfs-fuse`
  and `--talk-name=org.gtk.vfs.*`: mounted network/MTP locations through GVfs.
- `--own-name=org.freedesktop.FileManager1`: the `ShowFolders`/`ShowItems`/`ShowItemProperties` service
  (`Files.Platform.Linux/DBus/FileManagerService.cs`).
- `--system-talk-name=org.freedesktop.UDisks2`: drive listing, mount, unmount, eject.
- `--talk-name=org.freedesktop.secrets`: Secret Service credential store.
- `--talk-name=org.freedesktop.Notifications`: toasts.
- Not granted: `org.freedesktop.Flatpak` (`flatpak-spawn --host` would void the sandbox). The portal bus names
  (`org.freedesktop.portal.*`, used for the appearance setting) are always reachable. Opening files with other apps
  inside the sandbox therefore depends on the OpenURI/OpenFile portals; verify "Open with" behaviour when running
  the Flatpak for real.

## AUR

`packaging/linux/aur/linuxfiles-bin/PKGBUILD` repackages the release tarballs (`files-linux-x64.tar.gz` plus
`files-packaging.tar.gz`, which carries the desktop entry, metainfo, icons, launcher and `LICENSE-MIT`) into
`/usr/lib/linuxfiles` + `/usr/bin/files`; x86_64 only. The checked-in `sha256sums` are `SKIP`:
`scripts/linux/gen-aur.sh <version> <dir> [out]` renders a PKGBUILD with real sums and `.SRCINFO` (CI does this on
tags and uploads both; push them to the AUR repo by hand). `AUR_BASE_URL=file:///dir` points the sources at local
files, which is how the package was build-tested with `makepkg` here (resulting file list checked). `namcap` is not
installed on the dev box, so the package is not linted. `aur/linuxfiles/PKGBUILD` (from source) is unchanged and
unbuilt.

## Nightly builds

`.github/workflows/nightly-linux.yml` builds the tarball and AppImage on every push to `main` and replaces the
single rolling pre-release at https://github.com/MemerGamer/LinuxFiles/releases/tag/nightly (stable asset names
`LinuxFiles-nightly-linux-x64.tar.gz` and `LinuxFiles-nightly-x86_64.AppImage`; the Flatpak is only built for
`linux-v*` tags). Pull requests targeting `main` get the same files as workflow artifacts with a read-only token.
Try it: `curl -LO https://github.com/MemerGamer/LinuxFiles/releases/download/nightly/LinuxFiles-nightly-x86_64.AppImage && chmod +x LinuxFiles-nightly-x86_64.AppImage && ./LinuxFiles-nightly-x86_64.AppImage`

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
- Runtime behaviour of the Flatpak (sandboxed run, portals) and a `namcap` lint of the AUR package.
- arm64 publishing has not been tested.

## Root actions

Native AUR packages install a root-owned Native AOT helper at `/usr/lib/linuxfiles/files-elevation-helper` and `packaging/linux/io.github.memergamer.LinuxFiles.root-actions.policy` under `/usr/share/polkit-1/actions/`. Install polkit to enable the menu. The helper is never setuid; the action uses `auth_admin` without retaining authorization. AOT publishing requires clang and zlib development files.

For local installation, publish normally and opt in with `scripts/linux/install-local.sh --install-root-helper`. This separately runs `sudo python3 scripts/linux/install-root-helper.py --from artifacts/linux-x64/elevation-helper`; the app stays in the user prefix. To inspect the package layout without privileges, run `python3 scripts/linux/install-root-helper.py --from <published-helper-dir> --destdir <temporary-staging-dir>`. Remove the system helper and policy as administrator when uninstalling; the user-local uninstaller intentionally cannot remove system files.

AppImage and Flatpak omit the helper, install no policy, and disable Root actions. They cannot supply host elevation, even when the native helper is installed separately. See [the elevation threat model](threat-model-elevation.md) for protocol, failure semantics and limitations.
