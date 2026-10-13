# Contributing to LinuxFiles

LinuxFiles is an independent Linux-only fork of [Files](https://github.com/files-community/Files), built with C#/.NET 10 and Uno Platform (Skia, Wayland/X11). It has no Windows support or upstream sync. Read [AGENTS.md](AGENTS.md), [CLAUDE.md](CLAUDE.md), [Linux agent rules](docs/linux-port/AGENT-RULES.md) and the [upstream policy](docs/linux-port/upstream-policy.md) before changing code. Historical porting plans are archived context.

## Issues and pull requests

Report Linux bugs and feature requests in [this fork](https://github.com/MemerGamer/LinuxFiles/issues). Discuss larger changes before implementing them. Follow the [Code of Conduct](.github/CODE_OF_CONDUCT.md); report security issues privately as described in [SECURITY.md](.github/SECURITY.md).

Create a focused branch and submit PRs to `main` in **MemerGamer/LinuxFiles**, never upstream. Use the [PR template](.github/PULL_REQUEST_TEMPLATE.md), link related issues, and prefix the title with `Fix:`, `Feature:` or `Code Quality:`. Explain the resulting behavior, checks performed and remaining limitations.

## Development

Follow `.editorconfig`, nearby MVVM/service/command patterns and localization conventions. Preserve CRLF on files that use it. Avoid generated files, unrelated cleanup and dependency changes. Put OS functionality in `Files.Platform.Abstractions` and `Files.Platform.Linux`; prefer existing native wrappers and source-generated `LibraryImport`.

Treat file operations, clipboard, drag/drop, previews, archives, settings persistence and localization as high-risk areas. Treat filenames, desktop entries and archive contents as untrusted. Avoid shell interpolation and use existing fd-relative helpers for recursive destructive operations. Check source, XAML, project exclusions and tests before deleting types or files.

## Build and validation

Install the .NET 10 SDK and [Linux desktop dependencies](.github/README.md#building-from-source). Linux builds default to the pinned Uno Wayland fork; obtain the public release packages before restoring. See [Wayland package setup](docs/linux-port/wayland-packages.md) and its publisher/build handoff. Run affected builds first, sequentially:

```sh
build/fetch-uno-wayland.sh
build/clear-uno-wayland-cache.sh
dotnet build src/Files.App -f net10.0-desktop
dotnet build tests/Files.Platform.Tests
dotnet test --project tests/Files.Platform.Tests/Files.Platform.Tests.csproj --no-build
```

The build opt-out is `-p:UseUnoWayland=false` (also pass it to a separate restore); `scripts/linux/publish.sh --x11` publishes without the fork. Runtime `UNO_SKIA_HOST=x11|wayland|auto` selects a host in fork-enabled builds. Use a fresh package version for fork changes; never replace published NuGet packages.

Use `scripts/linux/headless-run.sh` for UI checks on a private Xvfb display with a throwaway home. It covers X11 fallback; native Wayland additionally needs a private nested/headless compositor and the checks in the Wayland handoff. Never launch checks against the user's display or alter desktop settings. Clean up started processes, inspect screenshots before sharing, and never commit private home data or screenshots. If the desktop toolchain is unavailable, use focused reference searches and state which checks were not run.

## AI assistance and attribution

AI-assisted contributions are welcome. Disclose in the PR description which tools/models assisted and what they did, and describe your review and validation. Contributors remain responsible for correctness, security and licensing.

Include `Co-Authored-By: Name <email>` trailers in commit messages for co-authors, including an AI assistant when its provider supplies an attribution identity. Use the actual identity supplied by the co-author or tool; do not invent an email address. Separate trailers from the commit body with a blank line, for example:

```text
Fix: Preserve saved tags when xattr writes fail

Co-Authored-By: Contributor Name <contributor@example.org>
```

Preserve Files Community credit, inherited contributor attribution, applicable headers, [LICENSE-MIT](LICENSE-MIT), [LICENSE-MPL](LICENSE-MPL) and [NOTICE.md](NOTICE.md). MIT applies to inherited MIT code and Linux modifications; MPL applies to files bearing MPL headers. Selective upstream fixes must retain attribution with `git cherry-pick -x`; do not restore Windows projects or merge upstream branches.
