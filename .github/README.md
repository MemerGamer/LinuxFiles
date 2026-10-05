<p align="center">
  <img alt="LinuxFiles on Linux: Home page" src="../docs/linux-port/showcase/home.png" width="820" />
</p>

# LinuxFiles: Files for Linux

**LinuxFiles, Files for Linux, based on Files by the Files Community, is an unofficial, community-maintained Linux port of [Files](https://github.com/files-community/Files) by the Files Community. It is not affiliated with or endorsed by the Files Community.**

Files is created and maintained by the [Files Community](https://github.com/files-community) and its [contributors](https://github.com/files-community/Files/graphs/contributors). This fork builds on their work and keeps their MIT-licensed code, history and credit. The Linux port itself (Uno Platform, Skia, X11, and the Linux platform layer) is the work of the LinuxFiles contributors. See [Credits](#credits) and [NOTICE.md](../NOTICE.md).

> [!WARNING]
> **Status: alpha.** Expect missing features and rough edges, and do not rely on it for data you cannot afford to lose. The official, supported Files for Windows is at [files.community](https://files.community/).

## Linux port: current state

This fork runs Files natively on Linux (Uno Platform, Skia, X11). The shell, Settings, file operations, trash, archives, clipboard and drag and drop, UDisks2 volumes and desktop integration are in place. See the [showcase](../docs/linux-port/SHOWCASE.md) for the full feature checklist and what is not working yet.

## Installing LinuxFiles

Packages are coming soon; until then, install from a local build:

| Method | Status |
|---|---|
| AppImage | coming soon |
| Flatpak (`io.github.memergamer.LinuxFiles`) | coming soon |
| AUR `linuxfiles-bin` | coming soon |
| `scripts/linux/install-local.sh` | available now |

```sh
git clone https://github.com/MemerGamer/LinuxFiles.git
cd LinuxFiles
scripts/linux/install-local.sh
```

### Nightly builds

Every merge to `main` publishes a tarball and AppImage to the rolling [nightly pre-release](https://github.com/MemerGamer/LinuxFiles/releases/tag/nightly). Run it with:

```sh
curl -LO https://github.com/MemerGamer/LinuxFiles/releases/download/nightly/LinuxFiles-nightly-x86_64.AppImage && chmod +x LinuxFiles-nightly-x86_64.AppImage && ./LinuxFiles-nightly-x86_64.AppImage
```

See [docs/linux-port/packaging.md](../docs/linux-port/packaging.md) for packaging details and [docs/linux-port/PLAN.md](../docs/linux-port/PLAN.md) for the roadmap.

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
