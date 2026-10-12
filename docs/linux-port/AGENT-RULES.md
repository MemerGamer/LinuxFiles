# Linux developer rules

Read [AGENTS.md](../../AGENTS.md) and the [independent-fork policy](upstream-policy.md). Porting plans are archived context. LinuxFiles supports Linux only and has no upstream sync.

## Machine and privacy

- Use `scripts/linux/headless-run.sh` for app runs. It supplies a private Xvfb display, throwaway home and cleanup. Never run against the user's display or capture their desktop.
- Do not alter the user's desktop settings, default applications or real home configuration.
- Inspect headless screenshots before sharing them. Never commit private home data.
- Run builds sequentially and clean up processes you start.

## Validation

```sh
dotnet build src/Files.App -f net10.0-desktop
dotnet build tests/Files.Platform.Tests
dotnet test --project tests/Files.Platform.Tests/Files.Platform.Tests.csproj --no-build
```

Use focused reference searches when the desktop toolchain is unavailable, and report that limitation. Before deletion, check source, XAML, project exclusions and tests for Linux consumers.

## Implementation and review

- Use existing services in `Files.Platform.Abstractions` and implementations in `Files.Platform.Linux`.
- Prefer existing native wrappers and `LibraryImport`. Treat filenames, desktop entries and archives as untrusted; avoid shell interpolation.
- Follow surrounding style and localization patterns. Preserve licenses, notices and attribution.
- Keep changes scoped and preserve CRLF where present. Commit, push or open PRs only when requested, targeting this fork.
- Report behavior changes, verification, unresolved issues and dependencies on other workstreams.
