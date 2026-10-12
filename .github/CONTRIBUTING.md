# Contributing to LinuxFiles

LinuxFiles is an independent Linux-only fork of [Files](https://github.com/files-community/Files), unaffiliated with the Files Community. There is no upstream sync or Windows support. Preserve inherited license headers, notices and contributor credit.

Report issues and submit pull requests in [this repository](https://github.com/MemerGamer/LinuxFiles). Discuss larger changes before implementing them, and follow our [Code of Conduct](CODE_OF_CONDUCT.md). Report security issues privately as described in [SECURITY.md](SECURITY.md).

Read [AGENTS.md](../AGENTS.md), [Linux agent rules](../docs/linux-port/AGENT-RULES.md) and the [fork policy](../docs/linux-port/upstream-policy.md). The porting plans are archived historical context.

## Build and validation

Use the .NET 10 SDK and the Linux desktop dependencies described in [README.md](README.md).

```sh
dotnet build src/Files.App -f net10.0-desktop
dotnet build tests/Files.Platform.Tests
dotnet test --project tests/Files.Platform.Tests/Files.Platform.Tests.csproj --no-build
```

Run builds sequentially. For UI validation, use `scripts/linux/headless-run.sh` with its private Xvfb display and throwaway home; do not alter the user's desktop or settings.

## Pull requests

Keep each change focused, follow `.editorconfig` and surrounding code, and include relevant validation. Use [the PR template](PULL_REQUEST_TEMPLATE.md), link the issue where applicable, and explain the resulting behavior and any limitations. Maintainers review contributions in this fork; upstream approval or labels are not required.
