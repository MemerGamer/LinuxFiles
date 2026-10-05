# Notice

## Fork relationship

LinuxFiles, "Files for Linux" (this repository, `MemerGamer/LinuxFiles`) is an unofficial, community-maintained Linux port of [Files](https://github.com/files-community/Files) by the Files Community. It is **not affiliated with or endorsed by** the Files Community.

The majority of the application (UI, view models, services, storage layer, localization) originates from upstream Files and remains the work of the Files Community and its contributors. The LinuxFiles contributors added the Linux platform layer, packaging and related changes on top of it.

Files is licensed under the MIT License:

> Copyright (c) 2018 - present Files Community

The LinuxFiles modifications are released under the same license, Copyright (c) 2026 LinuxFiles contributors. The upstream copyright notice is retained unchanged in [LICENSE-MIT](./LICENSE-MIT). [LICENSE-MPL](./LICENSE-MPL) is inherited from upstream and applies to files that carry a Mozilla Public License header.

"Files" and the Files logo belong to the Files Community. This port is named LinuxFiles and uses a badged icon variant; the Files name and logo appear only to identify the upstream project this port is based on.

## Third-party components

Upstream's third-party notices (7-Zip, LiteDB, ByteSize, .NET Community Toolkit, FluentFTP, SevenZipSharp, SQLitePCLRaw, libgit2sharp, DiscUtils and others) are in [.github/NOTICE.md](./.github/NOTICE.md) and still apply. The table below lists the major components that ship in the Linux build. Versions are pinned in [Directory.Packages.props](./Directory.Packages.props); licenses were checked against each package's `.nuspec` (SPDX expression or bundled license file); the package itself has the authoritative text.

| Component | Used for | License |
|---|---|---|
| [Uno Platform](https://github.com/unoplatform/uno) (`Uno.WinUI`, Skia X11 runtime) | WinUI on Linux | Apache-2.0 |
| [Uno.Fonts.Fluent](https://github.com/unoplatform/uno.fonts) | Icon font | Apache-2.0 |
| [SkiaSharp](https://github.com/mono/SkiaSharp) (and Skia) | Rendering | MIT (SkiaSharp), BSD-3-Clause (Skia) |
| [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) | SVG rendering | MIT |
| [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus) | D-Bus integration | MIT |
| [TagLibSharp](https://github.com/mono/taglib-sharp) | Media tags | LGPL-2.1 |
| [Markdig](https://github.com/xoofx/markdig) | Markdown preview | BSD-2-Clause |
| [ColorCode](https://github.com/CommunityToolkit/ColorCode-Universal) | Code highlighting | MIT |
| [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) (libgit2) | Git status | MIT (libgit2 native library: GPL-2.0 with linking exception) |
| [FluentFTP](https://github.com/robinrodricks/FluentFTP) | FTP locations | MIT |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | Archives | MIT |
| [SevenZipSharp](https://github.com/squid-box/SevenZipSharp) / [7-Zip](https://www.7-zip.org/) | Archives | LGPL-3.0 (SevenZipSharp, per package copyright; 7-Zip: LGPL with unRAR restriction, BSD-3-Clause parts) |
| [DiscUtils](https://github.com/DiscUtils/DiscUtils) | Disk images | MIT |
| [.NET Community Toolkit](https://github.com/CommunityToolkit) | MVVM and controls | MIT |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | SQLite | Apache-2.0 |
| [Sentry](https://github.com/getsentry/sentry-dotnet) | Crash reporting (if enabled) | MIT |
| [OwlCore.Storage](https://github.com/Arlodotexe/OwlCore.Storage) | Storage abstractions | MIT (bundled `LICENSE.txt`) |
| Microsoft.Extensions.*, System.Text.Json, .NET runtime | Runtime libraries | MIT |

## Bundled fonts and icons

- **Selawik** (Microsoft), UI text font, unmodified: SIL Open Font License 1.1. License file: `src/Files.App/Assets/Fonts/Linux/Selawik-OFL-LICENSE.txt`.
- **Uno.Fonts.Fluent**, icon glyphs: Apache-2.0.
- Segoe UI, Segoe UI Variable and Segoe Fluent Icons are Microsoft-licensed and are **not** redistributed in the Linux build.

Details and known limitations: [docs/linux-port/third-party-fonts.md](./docs/linux-port/third-party-fonts.md).

If a required notice is missing or wrong, please open an issue at <https://github.com/MemerGamer/LinuxFiles/issues>.
