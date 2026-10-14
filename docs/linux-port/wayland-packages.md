# Uno Wayland package distribution

The Linux desktop target defaults to `UseUnoWayland=true` (effective switch: `FilesUseUnoWayland`) and pins **6.7.135-wayland.5** in `Directory.Packages.props`. The fork is [MemerGamer/uno, branch wayland/host](https://github.com/MemerGamer/uno/tree/wayland/host); LinuxFiles does not build Uno during restore. Each revision is built once from a reviewed `wayland/host` commit, pinned in the repository pin file and published byte-for-byte as a public release; revision 5 is current.

## Release contract

Repository: **MemerGamer/uno** (public). Tag and GitHub Release: **uno-wayland-6.7.135-wayland.5**. For subsequent revisions, replace the version consistently with `X.Y.Z-wayland.N`; never use `latest`, a rolling tag, or replace assets for an existing version.

The release must be published (a public prerelease is fine), with these nine assets, preserving filename case:

```text
Uno.WinUI.6.7.135-wayland.5.nupkg
Uno.WinRT.6.7.135-wayland.5.nupkg
Uno.Foundation.6.7.135-wayland.5.nupkg
Uno.Foundation.Logging.6.7.135-wayland.5.nupkg
Uno.UI.Adapter.Microsoft.Extensions.Logging.6.7.135-wayland.5.nupkg
Uno.WinUI.Runtime.Skia.6.7.135-wayland.5.nupkg
Uno.WinUI.Runtime.Skia.X11.6.7.135-wayland.5.nupkg
Uno.WinUI.Runtime.Skia.Wayland.6.7.135-wayland.5.nupkg
SHA256SUMS
```

Each nupkg must identify the matching package ID/version in its nuspec, with fork dependencies pointing to the same version. Build desktop assets for **net10.0**, matching LinuxFiles; net9.0-only aggregates are insufficient. The fork's `build/wayland/build-local-packages.sh --strict --version VERSION --tfm net10.0 --feed DIR` builds the required projects, packs the five ordinary packages and prepares the three aggregate packages. Preserve the packages' upstream licenses, notices and contributor attribution.

`SHA256SUMS` contains exactly eight LF-terminated lines in `sha256sum` format: 64 lowercase hexadecimal characters, two spaces, and the corresponding bare nupkg filename. No directories, extra entries or duplicate names. Downloads use `https://github.com/MemerGamer/uno/releases/download/uno-wayland-VERSION/ASSET`.

## Obtaining packages

Install Bash, Python 3, curl, GNU coreutils and util-linux (`flock`), then run:

```sh
build/fetch-uno-wayland.sh
build/clear-uno-wayland-cache.sh
dotnet build src/Files.App -f net10.0-desktop
```

The script reads the version from `Directory.Packages.props`, loads the committed eight-package pin set, cross-checks the release manifest, downloads and verifies packages before exposing them, and rechecks cached hashes on every run. It serializes concurrent fetches. `nuget.wayland.config` maps the eight exact package IDs to `.cache/uno-wayland-feed`, relative to the repository; other dependencies keep their existing sources. `.cache/` is gitignored. A complete verified cache needs no package-download network access on subsequent runs; ordinary NuGet dependencies still need to be restored or cached.

The trust anchor is [build/uno-wayland-packages.sha256](../../build/uno-wayland-packages.sha256), with exactly eight entries per version in `VERSION SHA256 BARE_PACKAGE_FILENAME` format. Fetches, local imports and cached feed packages are checked against those repository pins. Unknown versions and incomplete, duplicate or malformed pin sets fail before network access or feed writes. Downloaded/cached `SHA256SUMS` must agree with all eight pins; it is a release cross-check and cannot authorize different bytes. Replacing both packages and the release manifest still fails.

For an offline mirror or locally built fork, use `build/fetch-uno-wayland.sh --import /path/to/nupkgs` (`--from` is an alias used by AUR). Only the eight nupkgs are required; if `SHA256SUMS` exists it is cross-checked. Explicit imports verify every supplied package even if the feed already holds a valid copy. The local build must match committed pins: an unreviewed build requires a fresh version and new reviewed pins. Do not repack an existing version or bypass verification with a custom feed.

`--version VERSION` requires a complete committed pin set for that version; use `-p:UnoWaylandVersion=VERSION` consistently for restore/build. `--feed DIR` changes the output location; supply a matching `-p:RestoreConfigFile=/path/to/custom.config` if it differs from the default feed. The SDK version-check exception is restricted to the reviewed `6.7.135-wayland.5` cohort and SDK base `6.7.135`; review that exception when updating versions.

NuGet's global cache can bypass the verified feed. Run `build/clear-uno-wayland-cache.sh` before a separate restore/build (or use a fresh `NUGET_PACKAGES` directory). The CI workflows do this after restoring their caches; publishing and source AUR do it before publishing. The helper removes only the eight package ID/version directories for the configured cohort, preserving unrelated packages and upstream versions.

## Updating the committed pins

Always follow **build packages -> compute sha256 -> commit pins -> publish release**:

1. Review the Uno host source commit and build the eight net10.0 desktop packages with a fresh version using the fork builder or the artifact-only workflow in [uno-fork-packages](uno-fork-packages/README.md). Verify package identities, dependency versions, desktop assemblies and licenses. Download the resulting artifact once; keep these exact bytes for publishing.
2. In that artifact directory, run `sha256sum *.nupkg > SHA256SUMS`. Confirm there are exactly the eight filenames in the release contract. Independently review the source commit and package metadata; do not copy hashes from a mutable release as the trust anchor.
3. Add eight real `VERSION SHA256 FILENAME` rows to `build/uno-wayland-packages.sha256`, for the new version. Update `UnoWaylandVersion` and the narrowly scoped SDK exception together for future cohorts. Run `--import` against the build directory and offline checks, then **commit the reviewed pins in LinuxFiles before publishing**. Source AUR generation reads the same pin file and fails until the set is complete. Generate its recipe with `scripts/linux/gen-aur.sh VERSION ARCHIVE_DIR OUT_DIR source`.
4. Publish the exact already-hashed artifact, plus `SHA256SUMS`, at `uno-wayland-VERSION` pointing to the reviewed source commit. Do not rebuild between pinning and upload: differing build bytes need new pins before publication. Never replace published assets or reuse a NuGet version. Verify an anonymous fetch against the committed pins and a clean restore with fresh `NUGET_PACKAGES`.

Placeholder hashes and `SKIP` are forbidden in generated recipes.

## Build and runtime opt-outs

Build with the upstream 6.7.135 X11 packages without fetching the fork:

```sh
dotnet build src/Files.App -f net10.0-desktop -p:UseUnoWayland=false
scripts/linux/publish.sh --x11 --tarball
```

Pass `UseUnoWayland=false` to restore as well if restoring separately; switching the property requires another restore/build. The effective switch selects only Uno Linux desktop targets, including outer builds and the virtualization/Publicizer, controls and storage projects. Other TFMs, non-Linux RIDs and platform-neutral projects receive upstream packages and no fork restore config, independent of the build OS. Linux-only Uno target defaults are declared early in `Directory.Build.props`, before central packages are evaluated. The Uno SDK stays at 6.7.30. Published builds remain untrimmed and non-AOT; the elevation helper's existing Native AOT configuration is separate.

In a fork-enabled build, `Program.cs` registers `UseWayland()` before `UseX11()`. The fork implements:

| `UNO_SKIA_HOST` | Behavior |
| --- | --- |
| unset or `auto` | Prefer native Wayland when `WAYLAND_DISPLAY` is nonempty and the compositor probe succeeds; otherwise try X11 using `DISPLAY`. |
| `x11` | Skip Wayland and use X11/XWayland (requires `DISPLAY`). |
| `wayland` | Require native Wayland; missing display, required compositor globals or connection libraries cause failure instead of fallback. |

Values are lowercase; invalid values (including an empty override) fail. Auto fallback applies to host probing, not recovery after a running Wayland connection fails. With both display variables absent there is no graphical host. Override examples: `UNO_SKIA_HOST=x11 files`, `UNO_SKIA_HOST=wayland ./Files-x86_64.AppImage`, or `flatpak run --env=UNO_SKIA_HOST=x11 io.github.memergamer.LinuxFiles`. An upstream-only build has no Wayland host to force. Restart the app when changing the override; a normal second launch forwards to the existing instance.

## CI and packaging

- Linux CI, package and nightly workflows call the fetch script before any build. Public release downloads require no contributor token, PAT, NuGet credential or cross-repository secret. Cache keys include the committed checksum pin file and restore configuration; the eight fork package ID/version directories are removed after cache restoration so NuGet reads the verified feed.
- `scripts/linux/publish.sh` fetches by default, so direct contributor/package publishing uses the same contract. Source AUR declares all eight Uno nupkgs in `source` with repository-pinned `sha256sums` and `noextract`, then imports them with `--from "$srcdir"`. The import performs no downloads; other NuGet dependencies still need a populated cache or network for restore.
- AppImage, Flatpak, `linuxfiles-bin` and Nix repackage the LinuxFiles publish/release tarball, which contains both Uno hosts. End users never fetch Uno packages. AUR recipes add `wayland`, `libxkbcommon` and EGL (`libglvnd`). Nix adds Wayland and xkbcommon to ELF runpaths alongside the existing GL/EGL provider; it keeps the previous release pin until the next application release is published and `scripts/linux/update-nix-flake.sh` updates its version/hashes.
- AppImage uses host `libwayland-client.so.0`, `libwayland-cursor.so.0`, `libwayland-egl.so.1`, `libxkbcommon.so.0`, and `libEGL.so.1` alongside existing X11/font/GL dependencies. The launcher preserves compositor and host-selection variables. Verify those libraries on the minimum supported distro; AppImage does not bundle a compositor or these libraries.
- Flatpak uses the Freedesktop 25.08 runtime libraries and grants `--socket=wayland` plus the existing `--socket=x11`. Keep the unconditional X11 socket so `UNO_SKIA_HOST=x11` works in Wayland sessions; `fallback-x11` would hide it there. Verify both paths inside the sandbox. Root-action gates and portal behavior are unchanged.

## Why GitHub Releases

GitHub Releases provide public, version-addressed assets with anonymous downloads and an ordinary local NuGet folder feed. The artifact build uses a read-only workflow token; publication uses the maintainer's usual GitHub credentials after the pins are committed. The eight desktop-only packages remain outside LinuxFiles history.

[GitHub's NuGet registry documentation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry) requires authentication even to install public packages. GitHub Packages would add token setup for contributors and cross-repository access configuration for CI and distro source builds. Vendoring nupkgs removes the fetch step but adds large binary files, repeated version churn and redistribution/license maintenance to LinuxFiles. Release assets keep ownership and source attribution with the Uno fork while retaining reproducible version selection and hash verification.

## Maintainer and build handoff

Copy [the fork workflow](uno-fork-packages/wayland-packages.yml) to **MemerGamer/uno/.github/workflows/wayland-packages.yml**, replacing its old GitHub Packages workflow. See [publisher instructions](uno-fork-packages/README.md). Build the new revision first, commit reviewed pins, then publish the exact artifact. The build workflow requires no custom secrets; release publication uses a maintainer's normal GitHub credentials. No changes to the Uno repository or releases have been made by this LinuxFiles change.

Local distribution checks: `python3 build/tests/test-fetch-uno-wayland.py` exercises offline import, pinned download URLs, cache verification/repair, malformed manifests and failed/checksum-mismatched downloads without network. Shell syntax, XML and YAML checks do not replace a real restore/build.

Before shipping, the build agent must:

1. Complete the pin update procedure from the intended `wayland/host` commit, check package artifact/metadata validation and verify the nine public release assets. Test a clean anonymous fetch, corrupt cache repair, and a checksum mismatch failure.
2. With an empty feed and fresh `NUGET_PACKAGES`, run the default desktop build, then build `tests/Files.Platform.Tests` and run `dotnet test --project tests/Files.Platform.Tests/Files.Platform.Tests.csproj --no-build`, sequentially. Repeat the app build with `-p:UseUnoWayland=false` and no fork feed. Check the restore graph uses the pinned revision throughout the default Uno graph and upstream 6.7.135 in the opt-out.
3. Publish/package the app, AppImage and Flatpak. Regenerate/check the AUR source and binary recipes and its native runtime dependencies. After the next application release, bump Nix with real tarball hashes and verify `nix flake check`/`nix build` and Wayland library resolution in the patched binaries. Existing alpha4 packages remain X11-only.
4. Run UI checks on private displays with throwaway homes: Xvfb with `WAYLAND_DISPLAY` unset, a private nested/headless compositor with `DISPLAY` unset, and both displays with each `UNO_SKIA_HOST` value. The current `scripts/linux/headless-run.sh` covers X11 and unsets Wayland; extend its isolated-display approach for Wayland tests. Never launch against the user's display or change desktop settings. Check fractional/mixed-DPI scaling, chrome/move/resize, multiple windows, keyboard/IME, file clipboard, inbound/outbound DnD, software/GPU rendering, AppImage and Flatpak. Clean up processes and private data. Distribution changes alone do not prove host readiness.
