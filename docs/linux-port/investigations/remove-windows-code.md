**Recommend option 1: retain the upstream tree, tighten Linux publish exclusions, then investigate trimming.** Deleting sources already excluded from compilation offers essentially **zero binary-size or startup gain**.

Read-only investigation completed; no edits, builds, app launches, commits or external comments. Counts use `git ls-files`, `wc` and targeted `rg`. MSBuild item queries timed out, so compilation counts below are **static estimates**, excluding generated code.

| Source area | Files / physical LOC | Default Linux treatment |
|---|---:|---|
| Files.App C# | 1,031 / 135,434 | Approximately 903 / 108,174 included; 128 / 27,260 excluded |
| `*.Windows.cs`, across src | 70 / 15,828 | Excluded; overlaps other rows |
| App `Helpers/Win32` | 8 / 2,165 | Excluded |
| App Shell / Taskbar / Signatures / Services.Windows | 33 / 6,813 | Excluded except `ShellHelpers.cs` |
| Files.App.Storage C# | 50 / 4,439 | Approximately 23 / 1,815 included; 27 / 2,624 excluded |
| CsWin32 / BackgroundTasks / Server C# | 12 / 550; 1 / 63; 3 / 152 | Projects excluded |
| Files.Platform.Windows C# | 2 / 77 | Project excluded |
| Files.Core.SourceGenerator C# | 16 / 2,172 | Built as an analyzer, not shipped |
| App XAML | 99 / 21,056 | 9 / 1,677 excluded; 90 / 19,379 retained |
| Localisation | 49 files / 232,436 LOC; 9.11 MiB | Shared Linux resources |
| Assets | 913 tracked files; 8.52 MiB | Broad inclusion still ships Windows material |

Build evidence: compat defaults to false in [Directory.Build.props:13](Directory.Build.props:13); App exclusions and project conditions are in [Files.App.csproj:199](src/Files.App/Files.App.csproj:199), [269](src/Files.App/Files.App.csproj:269), [282](src/Files.App/Files.App.csproj:282); storage exclusions are in [Files.App.Storage.csproj:26](src/Files.App.Storage/Files.App.Storage.csproj:26).

The runtime project graph retains Controls, Storage, UnoVirtualization, Core.Storage, Platform.Abstractions, Platform.Linux and Shared. Windows native Launcher/OpenDialog/SaveDialog projects are also outside it: 48 tracked files, approximately 5,779 text LOC combined.

Approximately **3,031 additional Windows-inactive lines** remain inside included App files. Removing these changes source readability, not emitted IL. Eight legacy `StorageItems` files / 1,750 LOC still compile; they cannot safely be classified as unused Windows code.

Concrete findings and root causes:

- **Published assets leak through SDK defaults.** Installed `Uno.DefaultItems.targets:15` includes `Assets/**`; Windows-conditioned `Content Update` entries do not exclude those defaults. The tarball contains `.reg`, `.winmd`, package archives, WSL icons and Windows tile variants.
- **SevenZip remains a Linux compile dependency.** [IStorageArchiveService.cs:99](src/Files.App/Data/Contracts/IStorageArchiveService.cs:99) exposes `SevenZipExtractor`; [StorageArchiveService.Desktop.cs:249](src/Files.App/Services/Storage/StorageArchiveService.Desktop.cs:249) returns a null stub; password handling still tests SevenZip exceptions in [IPasswordProtectedItem.cs:17](src/Files.App/Utils/Storage/StorageBaseItems/IPasswordProtectedItem.cs:17).
- **System.Drawing.Common is a removal candidate, not proven unnecessary.** Retained source references mostly use portable `Point`/`Rectangle` types, e.g. [UIHelpers.cs:262](src/Files.App/Helpers/UI/UIHelpers.cs:262). A focused build must confirm package removal.
- **Registry generation still emits Windows code.** The 401-line [RegistrySerializationGenerator.cs:18](src/Files.Core.SourceGenerator/Generators/RegistrySerializationGenerator.cs:18) processes attributes on shared models; the published DLL contains `TaggedFileRegistry` and `LayoutPreferencesDatabaseItemRegistry`. Gating generation also requires guarding [FileTagsDatabase.cs:11](src/Files.App/Utils/FileTags/FileTagsDatabase.cs:11).
- **WinUI names do not establish Windows-only usage.** Linux uses Uno-compatible toolkit packages and shared dictionaries, explicitly merged in [App.xaml:110](src/Files.App/App.xaml:110). Localisation generates 49 resource payloads, about 3.64 MiB; cultures are not Windows-only. Dynamic resource lookup prevents confidently deleting keys from simple searches.
- **Central package versions are not package references.** Removing unused entries from `Directory.Packages.props` alone does not reduce deployment. WindowsAppSDK, Win2D, CsWinRT and CsWin32 are absent from the observed Linux runtime graph.

Measured [existing tarball](artifacts/files-linux-x64.tar.gz), dated **2026-10-06**, therefore not a current-HEAD publish: **84.66 MiB compressed, 203.82 MiB unpacked, 1,176 files**. It contains 158.56 MiB managed assemblies and 32.19 MiB native libraries; none of the excluded Windows project assemblies or source-generator assembly ship.

Avoidable candidates: `Assets/Libraries` **3.58 MiB**; 648 tile variants **2.32 MiB**; SevenZip/System.Drawing-related assemblies **3.52 MiB**. Keep package archives in the repository: [nuget.config:6](nuget.config:6) uses them for restore. Keep packaging icon originals and required runtime logos/fonts.

Publishing is self-contained, explicitly **untrimmed and non-AOT** in [publish.sh:30](scripts/linux/publish.sh:30); Release uses ReadyToRun ([Files.App.csproj:67](src/Files.App/Files.App.csproj:67)). The separate elevation helper uses AOT. Shipped assemblies do not prove assemblies loaded at startup; the LibGit2 module initializer does explicitly touch its assembly ([LibGit2NativeResolver.cs:35](src/Files.App/Services/Git/LibGit2NativeResolver.cs:35)).

Trimming can remove unreachable managed members/framework assemblies; it does not automatically classify asset files as unused. Exact savings are **unknown without a publish analysis**. Existing blockers include reflective Uno geometry activation in [SpeedGraph.cs:250](src/Files.App/UserControls/StatusCenter/SpeedGraph.cs:250), reflection-based JSON calls and documented IL warnings. [Microsoft trimming guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained) supports analysing these before enabling trimming.

| Option | Effort | Assessment |
|---|---|---|
| 1. Keep tree; exclude and trim publish | S cleanup; M/L trimming | Recommended; preserves upstream paths and Windows restoration |
| 2. Soft split through compile globs | S exclusions; M moving code | Already largely implemented; retain original paths where possible |
| 3. Hard delete with sync automation | L ongoing | Feasible only with explicit path policy and conflict-aware processing |
| 4. Permanent hard fork | L ongoing | Simplifies the visible tree; assumes ownership of upstream fixes and abandons routine reintegration |

Daily sync **attempts a merge and opens a PR**; it aborts on conflicts ([sync-upstream.yml:14](.github/workflows/sync-upstream.yml:14), [71](.github/workflows/sync-upstream.yml:71)). Deletion causes modify/delete conflicts when upstream changes deleted paths—not necessarily every merge. New files can reappear.

`-X ours` favours conflicting content hunks; it is not a deletion policy. A `merge=ours` custom driver needs Git configuration and handles file contents, not structural deletion conflicts. [Git strategy documentation](https://git-scm.com/docs/merge-strategies), [attribute documentation](https://git-scm.com/docs/gitattributes). A viable deletion script must resolve only allowlisted conflicts, re-delete matching additions, reject other conflicts and retain merge ancestry; a post-success script alone is insufficient. Broad `ours` handling risks silently losing useful upstream changes.

Concrete recommended steps:

1. **S:** Add desktop asset exclusions in `Files.App.csproj`, accounting for late SDK globs; retain repository files and packaging inputs.
2. **S:** Verify/remove the Linux System.Drawing.Common reference; condition package references rather than deleting central versions.
3. **M:** Move SevenZip-specific contracts/hooks behind Windows conditions and finish migration to `IArchiveService`, then condition SevenZipSharp.
4. **S:** Gate registry generation on Linux; retain command, string and Uno dependency-property generators.
5. **M/L:** Add an opt-in trim/AOT analysis profile; resolve warnings, compare publish manifests, and retain ReadyToRun as the baseline.

Expected gain from these measured candidates: approximately **7–9.4 MiB unpacked**, subject to dependency/resource verification; compressed savings unknown. Startup improvement is likely small and remains unmeasured. Build gains mainly concern restore, asset processing and unnecessary generation; excluded-source deletion saves little compilation work. Trimming/R2R increase publish work.

Only app execution can establish loaded assemblies, startup/first-frame timings, RSS and runtime regressions. Validate exclusively through [headless-run.sh](scripts/linux/headless-run.sh) on private Xvfb: themes/fonts/localisation, navigation/settings/tags, previews, encrypted archives, FTP and file operations. Xvfb timings do not establish hardware-rendered performance.

Hard deletion conflicts with [AGENT-RULES.md:26](docs/linux-port/AGENT-RULES.md:26) and the optional Windows restoration phase ([PLAN.md:52](docs/linux-port/PLAN.md:52)). Git history preserves recovery, but deletion makes that restoration substantially more expensive.
