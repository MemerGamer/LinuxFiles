# Rules for agents working on the Linux port

Read this first, then `PLAN.md` §0, then `CLAUDE.md`.

## Machine etiquette (the owner uses this machine, often for games, while agents work)
- Never run the app on the real display. Don't use `DISPLAY=:0` or `WAYLAND_DISPLAY`, and don't run xdotool, screenshots or input against it. Run the app only through `scripts/linux/headless-run.sh`, which uses a private Xvfb display, a sandboxed throwaway HOME and cleans up after itself. Its `-a` actions file sends xdotool input and takes screenshots on the private display.
- Never change desktop or system settings (gsettings, kwriteconfig, portal writes, xdg-mime defaults, ~/.config of the real user).
- Build at low priority without leftover MSBuild nodes:
  `MSBUILDDISABLENODEREUSE=1 nice -n 19 dotnet build src/Files.App -f net10.0-desktop -nodeReuse:false -m:2`
- Tests: `MSBUILDDISABLENODEREUSE=1 nice -n 19 dotnet build tests/Files.Platform.Tests -nodeReuse:false -m:2 -warnaserror` then `nice -n 19 dotnet test --project tests/Files.Platform.Tests/Files.Platform.Tests.csproj --no-build`.
- Kill every process you start. Leave nothing running.
- At most one app build or test run at a time per agent (`-m:2` caps CPU). Avoid needless full rebuilds.

## Privacy
- Only commit screenshots made with the sandboxed HOME. headless-run.sh gives the app synthetic drives (`FILES_SANDBOX_DRIVES`, honoured only in sandboxed headless mode), so real drive labels no longer appear; still view every PNG before committing. `scripts/linux/showcase.sh` regenerates the showcase shots.
- Never commit anything from the real home directory.

## Git / GitHub
- Branch from `origin/main` (the single integration branch; releases are tags: `nightly` rolling, `linux-v*` stable): `git fetch origin && git switch -c linux/<wp> origin/main`.
- Commit trailer: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Push: `git -c credential.helper='!gh auth git-credential' push -u origin linux/<wp>`.
- PR: `gh pr create -R MemerGamer/LinuxFiles --base main --head linux/<wp> ...`. The body ends with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
- Never target, push to, or comment on upstream `files-community/Files`.

## Code
- Linux-first: the Windows build need not stay green, but keep Windows code paths intact (`#if WINDOWS`, `*.Linux.cs` partials, desktop-only Compile items).
- Route OS functionality through `Files.Platform.Abstractions` services implemented in `Files.Platform.Linux` (unit-tested in `tests/Files.Platform.Tests`). Don't call Win32 on Linux.
- Native interop only via `[LibraryImport]` in `src/Files.Platform.Linux/Native/`. No `DllImport`/`ComImport`.
- Mark stubs `// LINUX-TODO(<area>): ...`.
- Shared files (`Strings/en-US/Resources.resw`, `Directory.Packages.props`, `Files.App.csproj`, `PlatformServiceCollectionExtensions.cs`, `AppLifecycleHelper.cs`): make minimal, append-only edits so merges stay trivial.
- Security: treat file names, `.desktop` files, archives and anything from disk as untrusted. No shell interpolation. Use fd-relative ops for recursive destructive actions (`Native/PosixNative`).
- Match the surrounding style (license header, tabs, block namespaces).

## Report back
PR URL, what works (with headless screenshot paths), what's stubbed (LINUX-TODOs), remaining issues, and any hooks you need from other workstreams.
