<p align="center">
  <img alt="LinuxFiles on Linux: Home page" src="../docs/linux-port/showcase/home.png" width="820" />
</p>

# LinuxFiles: Files for Linux

**LinuxFiles, Files for Linux, based on Files by the Files Community, is an unofficial, community-maintained Linux port of [Files](https://github.com/files-community/Files) by the Files Community. It is not affiliated with or endorsed by the Files Community.**

Files is created and maintained by the [Files Community](https://github.com/files-community) and its [contributors](https://github.com/files-community/Files/graphs/contributors). This fork builds on their work and keeps their MIT-licensed code, history and credit. The Linux port itself ([Uno Platform](https://platform.uno), Skia, X11, and the Linux platform layer) is the work of the LinuxFiles contributors. See [Credits](#credits) and [NOTICE.md](../NOTICE.md).

> [!WARNING]
> **Status: alpha.** Expect missing features and rough edges, and do not rely on it for data you cannot afford to lose. The official, supported Files for Windows is at [files.community](https://files.community/).

## Linux port: current state

This fork runs Files natively on Linux (Uno Platform, Skia, X11). The shell, Settings, file operations, trash, archives, clipboard and drag and drop, UDisks2 volumes and desktop integration are in place. See the [showcase](../docs/linux-port/SHOWCASE.md) for the full feature checklist and what is not working yet.

## Install

[![Latest release](https://img.shields.io/github/v/release/MemerGamer/LinuxFiles?include_prereleases&filter=linux-v*&label=release)](https://github.com/MemerGamer/LinuxFiles/releases/tag/linux-v0.1.0-alpha3)

> [!NOTE]
> **0.1.0-alpha3 is the third public alpha release.** Expect rough edges and missing features, and keep backups of anything you care about. Please report problems in the [issue tracker](https://github.com/MemerGamer/LinuxFiles/issues).

Grab the assets from the [0.1.0-alpha3 release page](https://github.com/MemerGamer/LinuxFiles/releases/tag/linux-v0.1.0-alpha3). Requirements: x86_64 Linux with an X11 session or XWayland (Wayland desktops work through XWayland); the packages are self-contained, so no .NET install is needed.

### Arch Linux (AUR)

```sh
yay -S linuxfiles-bin    # or: paru -S linuxfiles-bin
```

This installs the polkit helper for optional [root actions](#root-actions); polkit and a session authentication agent are required.

### Nix / NixOS

```sh
nix run github:MemerGamer/LinuxFiles          # try it
nix profile install github:MemerGamer/LinuxFiles
```

The flake exports `overlays.default` (adds `pkgs.linuxfiles`) and `nixosModules.default`. Plain `nix run`/profile installs keep all [root actions](#root-actions) disabled. NixOS administrators can import the module and opt in:

```nix
# Add inputs.linuxfiles.nixosModules.default to your NixOS modules.
programs.linuxfiles = {
  enable = true;
  rootActions = true; # default: false
};
```

This installs a patched helper, a matching polkit action and trusted deployment manifest, and enables the pkexec wrapper. A session authentication agent is required. The flake pins alpha3, which supports this opt-in. A runtime-capability assertion rejects incompatible or unmarked package overrides. See [Nix packaging details](../docs/linux-port/packaging.md#nix-flake).

### AppImage

```sh
curl -LO https://github.com/MemerGamer/LinuxFiles/releases/download/linux-v0.1.0-alpha3/Files-x86_64.AppImage
chmod +x Files-x86_64.AppImage
./Files-x86_64.AppImage
```

Root actions remain disabled in the AppImage. Administrators can separately [provision a matching native host helper](../docs/linux-port/packaging.md#companion-host-helper-opt-in-appimage--flatpak), but this alone does not enable AppImage elevation.

### Flatpak bundle

```sh
flatpak install --user Files-x86_64.flatpak
flatpak run io.github.memergamer.LinuxFiles
```

The bundle needs the `org.freedesktop.Platform//25.08` runtime; Flatpak offers to install it from Flathub. Root actions remain disabled. An optional [companion host-helper installer](../docs/linux-port/packaging.md#companion-host-helper-opt-in-appimage--flatpak) provisions the native host only; this bundle has no host-spawn bridge and keeps its sandbox permissions unchanged.

### Tarball

```sh
tar xzf files-linux-x64.tar.gz
./linux-x64/Files                      # run it in place
```

To install it for your user (menu entry, icons, `files` launcher in `~/.local`), use the script from a clone of this repository, which also supplies the desktop entry and icons:

```sh
git clone https://github.com/MemerGamer/LinuxFiles.git
cd LinuxFiles
scripts/linux/install-local.sh --from /path/to/linux-x64
```

`files-packaging.tar.gz` holds only the desktop entry, metainfo, icons, launcher and licence, for packagers (the AUR package consumes it together with the tarball above). `scripts/linux/uninstall-local.sh` reverses a local install.

### Root actions

Optional. **Open in terminal as root** uses a trusted run0, sudo or pkexec in native packages that allow elevation. Polkit-authenticated Delete/Rename/Paste-as-root requires the installed trusted helper: AUR, an opted-in local tarball install, or the NixOS module with a release containing its runtime support. AppImage, Flatpak and plain `nix run` keep all root actions disabled. In a supported native installation, start a root-mode window with:

```sh
files --root
```

or pick **Open in Root Mode** from the launcher's desktop actions. Each action asks for authentication for that exact operation, and the GUI itself stays unprivileged. See the [elevation threat model](../docs/linux-port/threat-model-elevation.md).

### Verify downloads

Download `SHA256SUMS` next to the files you fetched and check them (missing files are reported, not fatal, with `--ignore-missing`):

```sh
sha256sum -c --ignore-missing SHA256SUMS
```

### Nightly builds

Every merge to `main` publishes a tarball and AppImage to the rolling [nightly pre-release](https://github.com/MemerGamer/LinuxFiles/releases/tag/nightly). It is untested bleeding edge. Run it with:

```sh
curl -LO https://github.com/MemerGamer/LinuxFiles/releases/download/nightly/LinuxFiles-nightly-x86_64.AppImage && chmod +x LinuxFiles-nightly-x86_64.AppImage && ./LinuxFiles-nightly-x86_64.AppImage
```

### More

- [CHANGELOG.md](../CHANGELOG.md): what is in each release
- [Showcase](../docs/linux-port/SHOWCASE.md): screenshots and the feature checklist
- [Packaging details](../docs/linux-port/packaging.md) and the [roadmap](../docs/linux-port/PLAN.md)

## Troubleshooting

**Soft text or icons on niri / xwayland-satellite:** Enable **Detect display scale from the compositor (experimental)** in Settings > Appearance and restart Files to use niri's focused output scale when no Uno, Xft, GDK or Qt scale hint is present and both `NIRI_SOCKET` and `WAYLAND_DISPLAY` are set. Check stderr for `[display-scale]` to see whether IPC or CLI detection succeeded. Detection defaults off and may double-scale on some setups. `FILES_NIRI_SCALE=1` forces it on and `FILES_NIRI_SCALE=0` forces it off, overriding the saved preference. Launch with `UNO_DISPLAY_SCALE_OVERRIDE=1.25 files` to select a scale manually (replace `1.25` with your output scale). The scale is chosen at startup. See [display scale details](../docs/linux-port/appearance-transparency.md#display-scale-on-niri).

## Reporting bugs

- **Linux bugs and Linux feature requests:** open an issue in [this fork](https://github.com/MemerGamer/LinuxFiles/issues).
- **Windows bugs:** report them to [upstream Files](https://github.com/files-community/Files/issues) only if you can reproduce them in the official Windows app. Please do not report LinuxFiles problems upstream.
- **Security issues:** use this fork's private [Security Advisories](https://github.com/MemerGamer/LinuxFiles/security/advisories/new). See [SECURITY.md](./SECURITY.md).

## About upstream Files

The text and images in this section describe the upstream project and are the Files Community's work, not ours.

![Upstream Files screenshot (Windows)](./assets/FilesScreenshot.png)

Files is a modern file manager that helps users organize their files and folders. The upstream mission is to build the best file manager for Windows, built in the open by the community, with robust multitasking, file tags, deep integrations and an intuitive design. See the [upstream repository](https://github.com/files-community/Files) and [website](https://files.community/).

For the official Windows app, use the upstream distribution channels (Microsoft Store and classic installer from [files.community/download](https://files.community/download)); this fork does not distribute Windows builds.

## Contributing

Contributions to the Linux port are welcome. See [CONTRIBUTING.md](./CONTRIBUTING.md). Contributions to the Windows app belong in the [upstream repository](https://github.com/files-community/Files).

## Credits

- **[Files Community](https://github.com/files-community)** and all [upstream contributors](https://github.com/files-community/Files/graphs/contributors): the Files app, its design, code and translations (via [Crowdin](https://crowdin.com/project/files-app)) that this port is built on. Files is MIT licensed, Copyright (c) 2018 - present Files Community.
- **[Uno Platform](https://platform.uno/)**: WinUI on Skia/X11 for Linux, which makes this port possible.
- **[SkiaSharp](https://github.com/mono/SkiaSharp)**, **[Selawik](https://github.com/microsoft/Selawik)** (UI font) and **Uno.Fonts.Fluent** (icons).
- **[Tmds.DBus](https://github.com/tmds/Tmds.DBus)**, **[TagLibSharp](https://github.com/mono/taglib-sharp)**, **[Markdig](https://github.com/xoofx/markdig)**, **[ColorCode](https://github.com/CommunityToolkit/ColorCode-Universal)**, **[LibGit2Sharp](https://github.com/libgit2/libgit2sharp)**, **[FluentFTP](https://github.com/robinrodricks/FluentFTP)**, **[SharpCompress](https://github.com/adamhathcock/sharpcompress)**, **[7-Zip](https://www.7-zip.org/)** and the .NET Community Toolkit.

The full list of third-party components and licenses is in [NOTICE.md](../NOTICE.md) and [.github/NOTICE.md](./NOTICE.md). "Files" and its logo are the Files Community's; they are used here to identify the upstream project and its Linux port, with no endorsement implied.

## License

[MIT](../LICENSE-MIT). Copyright (c) 2018 - present Files Community; Copyright (c) 2026 LinuxFiles contributors (modifications for Linux).
