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

[![Latest release](https://img.shields.io/github/v/release/MemerGamer/LinuxFiles?include_prereleases&filter=linux-v*&label=release)](https://github.com/MemerGamer/LinuxFiles/releases/tag/linux-v0.1.0-alpha1)

> [!NOTE]
> **0.1.0-alpha1 is the first public release and an alpha.** Expect rough edges and missing features, and keep backups of anything you care about. Please report problems in the [issue tracker](https://github.com/MemerGamer/LinuxFiles/issues).

Grab the assets from the [0.1.0-alpha1 release page](https://github.com/MemerGamer/LinuxFiles/releases/tag/linux-v0.1.0-alpha1). Requirements: x86_64 Linux with an X11 session or XWayland (Wayland desktops work through XWayland); the packages are self-contained, so no .NET install is needed.

### Arch Linux (AUR)

```sh
yay -S linuxfiles-bin    # or: paru -S linuxfiles-bin
```

This is the only format that also installs the polkit helper for the optional [root actions](#root-actions).

### Nix / NixOS

```sh
nix run github:MemerGamer/LinuxFiles          # try it
nix profile install github:MemerGamer/LinuxFiles
```

The flake also exports `overlays.default` (adds `pkgs.linuxfiles`). It repackages the release tarball for x86_64-linux and does not include the polkit helper, so the [root actions](#root-actions) that need it stay off.

### AppImage

```sh
curl -LO https://github.com/MemerGamer/LinuxFiles/releases/download/linux-v0.1.0-alpha1/Files-x86_64.AppImage
chmod +x Files-x86_64.AppImage
./Files-x86_64.AppImage
```

### Flatpak bundle

```sh
flatpak install --user Files-x86_64.flatpak
flatpak run io.github.memergamer.LinuxFiles
```

The bundle needs the `org.freedesktop.Platform//25.08` runtime; Flatpak offers to install it from Flathub.

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

Optional. **Open in terminal as root** works everywhere (it asks your terminal elevation tool: run0, sudo or pkexec). The polkit-authenticated Delete/Rename/Paste-as-root actions need the root helper, which only the AUR package installs; they are off in the AppImage and Flatpak. Start a root-mode window with:

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
