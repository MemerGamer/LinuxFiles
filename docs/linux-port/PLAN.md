# Files on Linux — Porting Plan

Status: draft v1 · 2026-10-04 · fork base `0e3c17ca4` (in sync with `files-community/Files` main)

## 1. Executive summary

Files is a WinUI 3 / Windows App SDK app (~130k LOC C#, 136 XAML files / ~30k XAML lines). Porting it to Linux is two
largely independent problems:

1. **UI framework** — WinUI 3 does not run on Linux. **Recommendation: Uno Platform (Skia desktop, X11 + Wayland).**
   Uno re-implements the `Microsoft.UI.Xaml` API surface, so the XAML (1,688 `x:Bind`, 168 `x:Load`, 1,084
   `ThemeResource`) and ~17k lines of code-behind carry over largely intact. An Avalonia rewrite would cost 5–10× more.
2. **OS integration** — this is the bigger cost and is the same regardless of UI choice:
   - CsWin32 / `Windows.Win32`: 137 files, 609 `PInvoke.` call sites, ~400 APIs in `NativeMethods.txt`
   - WinRT `Windows.Storage`: 128 files, 806 `StorageFile`/`StorageFolder` hits (the legacy `BaseStorageFile` layer)
   - Registry (22 files), `ApplicationData` (20 files), MSIX/packaged-app APIs, Shell COM (context menus, thumbnails,
     IFileOperation, preview handlers)

Strategy: **introduce a platform abstraction layer, move Windows code behind it unchanged, then add a Linux backend
and an Uno head.** The Windows build must keep working at every step so the fork can keep merging upstream.

## 2. Guiding principles

- **Keep upstream mergeable.** Prefer additive files, partial classes, DI registration, and `#if` in a few
  well-known seams over rewriting upstream files. Each upstream sync should be a routine merge.
- **Windows build never breaks.** Every PR must build `net10.0-windows` (CI on Windows) *and* whatever Linux targets
  exist at that point (CI on Linux).
- **Drop, don't port, Windows-only features** (listed in §6). Hide them in the UI on Linux via a capability flag.
- **Managed first, native second.** Prefer `System.IO`, `FileSystemWatcher`, SharpCompress, TagLibSharp,
  Tmds.DBus over P/Invoking GLib. Use GIO only where a managed path is clearly worse (thumbnails, mime/app info).
- **Respect repo rules** (`CLAUDE.md`): CRLF line endings, `.editorconfig`, no new `DllImport`/`ComImport`,
  AOT-safe code, no edits to generated CsWin32 output.

## 3. Current-state findings

### 3.1 Project inventory

| Project | TFM today | Linux verdict |
|---|---|---|
| Files.Shared | net10.0 | Portable (remove `win-*` RIDs; LogPathHelper redaction is Windows-path-only) |
| Files.Core.Storage | net10.0 (OwlCore.Storage) | Portable — the right foundation, but barely used by the app |
| Files.Core.SourceGenerator | netstandard2.0 | Portable |
| Files.App.Storage | windows | Split: `Ftp/`, `Legacy/NativeStorageLegacy` portable; `Windows/` stays Windows |
| Files.App.Controls | windows (WinUI) | Clean of Win32/Composition — ports to Uno with minor changes |
| Files.App | windows (WinUI, MSIX) | The bulk of the work |
| Files.App.CsWin32 | windows | Windows-only |
| Files.App.Server | windows (OOP WinRT server) | Drop on Linux |
| Files.App.BackgroundTasks | windows (MSIX update task) | Drop on Linux |
| Files.App.Launcher / OpenDialog / SaveDialog | C++ vcxproj | Drop on Linux (see §6 for replacements) |
| tests/* | windows (Appium/WinAppDriver, WinUI gallery) | No unit tests exist today |

All csproj files hard-code `RuntimeIdentifiers=win-x86;win-x64;win-arm64`, including the neutral ones.

### 3.2 Coupling heat-map (src/Files.App)

| Folder | .cs | Files w/ Win32/WinRT | Coupling |
|---|---|---|---|
| Utils | 140 | 80 | Very high (Shell/, Storage/StorageItems, Signatures, Taskbar, Cloud) |
| Helpers | 56 | 24 | High (`Helpers/Win32/`, Registry) |
| Services | 58 | 15 | Concentrated in `Services/Windows`, `Services/Storage` |
| ViewModels | 85 | ~35 | Medium-high; 31 also reference XAML types |
| Actions | 172 | 28 | Low — ~62% already platform-neutral |
| Data | 256 | 25 | Low OS coupling, high UI coupling |

> Gotcha: `.gitignore`'s `[Ww]in32/` rule makes `rg` skip `src/Files.App/Helpers/Win32/`. Always search with
> `rg --no-ignore`.

### 3.3 Top hot spots (ranked)

1. **File-operations stack** — `Utils/Storage/Operations/{FileOperationsHelpers,ShellFilesystemOperations,FilesystemOperations}.cs`,
   `Utils/Shell/ShellFileOperations2.cs`, `Files.App.Storage/Windows/WindowsBulkOperations*` (~3.7k LOC, IFileOperation on STA).
2. **`ViewModels/ShellViewModel.cs`** (3,578 LOC) — directory enumeration (`FindFirstFileExFromApp`),
   `ReadDirectoryChangesW` watcher, thumbnails, `DangerousGetFolder*`. The single most important seam.
3. **`Helpers/Win32/Win32Helper.Storage.cs`** (1,211 LOC) + `Win32PInvoke.Methods.cs` — attributes, ADS, icons, PowerShell.
4. **Legacy WinRT storage layer** — `Utils/Storage/StorageItems/*` (`BaseStorageFile/Folder`, `System*`, `Shell*`,
   `Zip*`, `Ftp*`, `Virtual*`), referenced by 61 files.
5. **Shell item / PIDL layer** — `Utils/Shell/ShellItem.cs`, `ShellFolderExtensions.cs`, `WindowsStorableHelpers.Shell.cs`.
6. **Context menus** — `Utils/Shell/{ContextMenu,OpenWithMenu,ShellNewMenuHelper,ContextMenuWorkerPool}.cs`.
7. **Icons & thumbnails** — `WindowsStorableHelpers.Icon.cs`, `FileThumbnailHelper`, `IconCacheService` (29 consumers).
8. **Security/ACL** — `StorageSecurityService.cs`, `WindowsObjectPicker.cs`.
9. **Clipboard/DnD** — `Utils/Shell/ShellDataObject.cs`, 36 files using `DataPackage`/OLE.
10. **Startup** — `Program.cs` (WinAppSDK `AppInstance` single-instance redirection, `LocalSettings`), `App.xaml.cs`.

Directory watching is implemented separately in five places (`ShellViewModel`, `SidebarViewModel`,
`QuickAccessWidgetViewModel`, `QuickAccessManager`, `LibraryManager`). Unify these behind one interface.

### 3.4 UI-layer blockers under Uno

| Area | File(s) | Plan |
|---|---|---|
| DirectComposition / child HWND hosting | `Data/Items/WindowEx.cs`, `ShellPreviewViewModel.cs` | Windows-only; exclude on Linux |
| Mica/Acrylic backdrop | `Helpers/UI/AppSystemBackdrop.cs` | No-op on Linux |
| Custom title bar / drag regions | `MainPage.xaml.cs`, `MainWindow.xaml.cs`, `DragZoneHelper.cs` | Use Uno's extend-into-titlebar support where available; fall back to system decorations |
| InteractionTracker swipe nav | `Helpers/Navigation/NavigationInteractionTracker.cs` | Make optional, disabled on Linux at first |
| Composition animations | `ShellPanesPage.xaml.cs`, `StickyHeaderBehavior.cs` | Check on Uno Skia; degrade gracefully |
| Win2D | `StatusCenter/SpeedGraph.cs`, `FontFileHelper.cs` | Rewrite with SkiaSharp (or `Path` geometry) |
| TabView tear-out | `UserControls/TabBar/TabBar.xaml.cs` | Disable tear-out on Linux at first |
| WebView2 / MediaPlayerElement | `FilePreviews/HtmlPreview`, `MediaPreview` | Uno WebView (WebKitGTK) / Uno MediaPlayer (libVLC), or defer |
| ColorCode.WinUI | code preview | Shim on ColorCode.Core |
| Tray icon | `Utils/Taskbar/SystemTrayIcon*.cs` | StatusNotifierItem over D-Bus (later) |
| `InputKeyboardSource` | 16 files incl. ViewModels | Verify Uno support; otherwise wrap behind `IKeyboardStateService` |

## 4. Target architecture

```
Files.Shared                (net10.0)  — unchanged
Files.Core.Storage          (net10.0)  — unchanged (OwlCore contracts)
Files.Platform.Abstractions (net10.0)  — NEW: interfaces + capability flags, no implementations
Files.Platform.Windows      (net10.0-windows) — NEW: wraps existing Windows code (moved, not rewritten)
Files.Platform.Linux        (net10.0)  — NEW: Linux implementations
Files.App.Storage           — split: portable parts → net10.0; Windows/ → Files.Platform.Windows
Files.App.Controls          — multi-target: net10.0-windows (WinAppSDK) + net10.0-desktop (Uno)
Files.App                   — multi-target: net10.0-windows (WinAppSDK) + net10.0-desktop (Uno Skia)
```

Backend selection in `AppLifecycleHelper` via `services.AddWindowsPlatform()` / `services.AddLinuxPlatform()`, each
defined in its own extension file so workstreams never collide in `AppLifecycleHelper.cs`.

### 4.1 Interfaces to introduce (Files.Platform.Abstractions)

| Interface | Wraps today | Linux implementation |
|---|---|---|
| `IFileSystemEnumerator` | `Win32StorageEnumerator`, `FindFirstFileEx` in ShellViewModel | `FileSystemEnumerable` + `statx` via `File.GetUnixFileMode` / Mono.Unix |
| `IFolderWatcherFactory` | `ReadDirectoryChangesW`, `WindowsFolderChangeWatcher`, `RecycleBinWatcher` | `FileSystemWatcher` (inotify) |
| `IFileOperationsService` | `IFilesystemOperations`, `ShellFilesystemOperations`, `ShellFileOperations2`, `WindowsBulkOperations` | Managed copy/move with progress, conflict resolution, cancellation; `rename(2)` fast path |
| `IFileAttributesService` | attribute/compression/size-on-disk/ADS methods of `Win32Helper.Storage` | `stat`, hidden = dot-prefix, xattrs |
| `ITrashService` | `IStorageTrashBinService` | freedesktop Trash spec (`$XDG_DATA_HOME/Trash`, per-mount `.Trash-$uid`) |
| `IThumbnailService` / `IIconProvider` | `FileThumbnailHelper`, `StorageItemIconHelpers`, `ImagingService`, icon extraction | XDG thumbnail cache + generation (SkiaSharp), freedesktop icon theme + shared-mime-info |
| `IShellContextMenuService` | `ContextMenu`, `OpenWithMenu`, `ShellNewMenuHelper` | Open With from `.desktop`/`mimeapps.list`; New from `~/Templates`; no 3rd-party extensions |
| `ILauncherService` | `Launcher.*`, `LaunchHelper`, `FileAssociationHelpers`, `InvokeWin32ComponentAsync` | `xdg-open`, `.desktop` Exec parsing, `gio launch` |
| `IClipboardService` | `Clipboard`/`DataPackage`, `ShellDataObject` | `text/uri-list` + `x-special/gnome-copied-files` (+ KDE cut marker) |
| `IDriveService` | `IRemovableDrivesService`, `DriveHelpers`, `WindowsDriveManager`, format/label | UDisks2 over D-Bus (Tmds.DBus), `/proc/self/mountinfo` fallback |
| `INetworkService` | `StorageNetworkService` (WNet) | GVfs mounts (`gio mount`), list `/run/user/$uid/gvfs` |
| `ICloudDetector` | `CloudDrivesDetector` (registry/SyncRoot) | Scan known folders (Dropbox, Nextcloud, OneDrive-abraunegg, rclone, GVfs google-drive) |
| `ISettingsStore` / `IAppDataPaths` | `ApplicationData.Current.*`, `RegistryHelpers`, `WindowsIniService` | JSON in `$XDG_CONFIG_HOME/files/`, cache in `$XDG_CACHE_HOME/files/` |
| `IElevationService` | `runas`, `RunPowershellCommandAsync` | `pkexec` helper |
| `IRecentItemsService` | `WindowsRecentItemsService` | `recently-used.xbel` |
| `IQuickAccessService` | `WindowsQuickAccessService`, `PinnedFoldersManager` | GTK bookmarks + xdg-user-dirs |
| `ISecurityService` | `StorageSecurityService`, `WindowsObjectPicker` | POSIX owner/group/mode editor (+ optional POSIX ACLs) |
| `IArchiveService` | `StorageArchiveService` (SevenZipSharp + bundled `7z*.dll`) | SharpCompress (managed), or system `7z` CLI for 7z write |
| `IFileTagsStore` | ADS `:files` stream + registry | `user.xdg.tags` xattr + existing SQLite DB |
| `ISingleInstanceService` | WinAppSDK `AppInstance` in `Program.cs` | Unix domain socket in `$XDG_RUNTIME_DIR` |
| `IShellIntegration` | jump list, start menu, taskbar, tray, wallpaper | No-op, except wallpaper (`gsettings`/portal) and tray (SNI, later) |
| `IPlatformCapabilities` | — | Flags used by UI to hide unsupported features |

## 5. Phased roadmap

Each phase lists its **exit criteria**. Phases 2 and 3 overlap heavily once Phase 1 has landed.

### Phase 0 — Toolchain & spikes (serial, small)
- Install the .NET SDK on the Linux dev box (`sudo pacman -S dotnet-sdk`). Done when `dotnet --list-sdks` shows 10.x.
- Baseline verified on Linux (SDK 10.0.112, 2026-10-04): Files.Shared, Files.Core.Storage and
  Files.Core.SourceGenerator build with 0 errors/warnings, including `-r linux-x64`; `Files.App` restores with
  `-p:EnableWindowsTargeting=true`. The `win-*` RIDs are therefore not a blocker; clean them up only when the
  neutral projects are packaged for Linux.
- **Spike A (Uno):** a throwaway Uno Skia desktop app on net10.0 that hosts `Files.App.Controls` (Omnibar,
  Sidebar, BreadcrumbBar, Toolbar) and one `x:Bind`-heavy page. Confirm on X11 and Wayland. Record API gaps
  (InteractionTracker, InputKeyboardSource, AppWindow title bar, TabView).
- **Spike B (WinAppSDK ↔ Uno coexistence):** confirm `Files.App.Controls` can multi-target
  `net10.0-windows10.0.26100.0` + `net10.0-desktop` with CommunityToolkit 8.x.
- Add a Linux CI job (`ubuntu-latest`): build neutral projects + run new unit tests.
- **Exit:** neutral projects build in Linux CI; spike report written to `docs/linux-port/spikes.md`;
  Uno vs Avalonia decision confirmed.

### Phase 1 — Abstraction skeleton (serial, blocks everything)
- Create `Files.Platform.Abstractions` (interfaces from §4.1 + `IPlatformCapabilities`), `Files.Platform.Windows`,
  `Files.Platform.Linux` (empty), and a `tests/Files.Platform.Tests` (MSTest, net10.0) project. Add them to `Files.slnx`.
- Add `AddWindowsPlatform()` / `AddLinuxPlatform()` DI entry points; call `AddWindowsPlatform()` from `AppLifecycleHelper`.
- **Exit:** Windows build and behaviour unchanged; new projects build on Linux.

### Phase 2 — Move Windows code behind the seams (parallel, W1–W8)
Each workstream: define its interface methods from real call sites, implement them in `Files.Platform.Windows` by
**moving or wrapping** existing code, then update call sites to use the interface. No behaviour change on Windows.
- **Exit per workstream:** zero direct `PInvoke.`/`Windows.Storage`/Registry calls remaining in the workstream's
  owned consumer files (checked with `rg --no-ignore`); Windows CI green.

### Phase 3 — Linux backend (parallel, one agent per interface)
- Implement each interface in `Files.Platform.Linux` with unit tests in `tests/Files.Platform.Tests` (temp-dir based,
  run in Linux CI). Order by dependency: settings/paths → enumerator → watcher → file ops → trash → launcher →
  attributes → thumbnails/icons → drives → clipboard → archives → tags → quick access/recent → network/cloud →
  elevation → security.
- **Exit:** each interface has a Linux implementation with tests passing on Linux CI.

### Phase 4 — Replace the legacy WinRT storage layer (focused, high risk)
- Migrate the 61 consumers of `BaseStorageFile`/`BaseStorageFolder`/`IStorageItem` onto OwlCore storables
  (`Files.Core.Storage`) + the Phase 2 interfaces. `ShellViewModel` first, then properties, search, archives (`Zip*`),
  FTP, virtual folders.
- Kept on a single owner (or a strictly serialized queue), because almost everything touches `ShellViewModel`.
- **Exit:** `Utils/Storage/StorageItems/*` is no longer referenced by the Linux build.

### Phase 5 — Uno head (can start after Phase 0 spikes, in parallel with 2–4 on Controls)
- 5a: Multi-target `Files.App.Controls` (Windows + Uno).
- 5b: Multi-target `Files.App`; exclude Windows-only files on Linux with `Compile Remove` + `*.Windows.cs` /
  `*.Linux.cs` naming convention rather than scattering `#if`.
- 5c: Resources: load `.resw` on Uno (Uno supports `.resw` via its resource generator; verify) — the 1,332 lookups
  are centralised in `ResourceHelpers` / `StringExtensions`, so only those need changing.
- 5d: Startup: new Linux `Program`/host using `ISingleInstanceService` and argv parsing; replace `LocalSettings`
  usage in `Program.cs`, `AdvancedViewModel`, `TabBar`, `WindowEx`, `ActiveSessionTracker`.
- 5e: UI blockers from §3.4: SpeedGraph on SkiaSharp, backdrop/title-bar fallbacks, TabView tear-out off,
  InteractionTracker optional, previews degraded.
- 5f: Hide unsupported features via `IPlatformCapabilities` (Security tab, Libraries, Signatures, Compatibility,
  shortcuts→`.desktop`, etc.).
- **Exit:** `dotnet run -f net10.0-desktop` opens Files on Linux, navigates, lists, copies/moves/deletes/trashes,
  opens files with default apps, renders thumbnails.

### Phase 6 — Desktop integration & packaging
- `org.freedesktop.FileManager1` D-Bus service (ShowFolders/ShowItems/ShowItemProperties): the Linux equivalent of
  `Files.App.Launcher`.
- `.desktop` entry, `inode/directory` MIME association, AppStream metainfo, icons.
- Packages: Flatpak (primary; needs `--filesystem=host`, `org.freedesktop.Flatpak` spawn for launching host apps
  carefully considered), AppImage, AUR `PKGBUILD`.
- Optional: StatusNotifierItem tray, `xdg-desktop-portal` FileChooser backend (replaces Open/SaveDialog — XL, defer).
- **Exit:** installable package; Files can be set as the default file manager.

### Phase 7 — Hardening
- Linux UI smoke tests (Uno runtime tests or a lightweight harness), performance on 100k-entry folders, HiDPI,
  Wayland vs X11, GNOME/KDE themes, localisation, accessibility (AT-SPI via Uno).

## 6. Features to drop or replace on Linux

| Windows feature | Linux decision |
|---|---|
| Shell preview handlers (COM, DComp hosting) | Drop; rely on built-in previews |
| 3rd-party shell context-menu extensions | Drop; Open With + Templates + custom actions only |
| Libraries (`.library-ms`) | Drop |
| Authenticode signatures tab | Drop |
| Compatibility tab | Drop |
| NTFS ACL Security tab / object picker | Replace with POSIX owner/group/permissions editor |
| `.lnk` / `.url` shortcuts | Read-only support optional; create symlinks / `.desktop` Link entries |
| Jump lists, Start-menu pinning, taskbar progress | Drop (taskbar progress could use Unity LauncherEntry D-Bus later) |
| Files.App.Server, BackgroundTasks, MSIX update task | Drop |
| Open/Save dialog replacement (C++ COM) | Defer; portal backend in Phase 6 |
| WSL distro listing, PowerShell helpers | Drop |
| Format drive dialog | Replace with `gnome-disks` launch if present, else hide |
| Store/sideload updater | Drop; updates come from the package manager |

## 7. Parallel-agent execution model

### 7.1 Model roles

| Role | Model | Responsibilities |
|---|---|---|
| Planner / architect / reviewer | **Opus 5.5** | Phase gates, interface design, splitting work packages, reviewing every PR diff, resolving cross-workstream conflicts |
| Implementer | **Sonnet 5.5** | One work package each: code changes in an isolated git worktree, unit tests |
| Runner | **Haiku 4.5** | Builds, test runs, `rg` inventories, git sync/merge, CI log triage. No source edits |

### 7.2 Workflow per wave

1. **Opus** writes work packages (WP) for the wave: goal, owned files/folders, interface contract, acceptance
   checks, explicit "do not touch" list.
2. **Sonnet** agents run one per WP, each in its own **git worktree** on branch `linux/<phase>-<wp>`.
3. **Haiku** runs the build/test matrix for each branch: `dotnet build` on Linux for neutral and Linux projects,
   unit tests, and an `rg --no-ignore` coupling check against the WP's owned files. It reports pass/fail plus a
   capped log.
4. **Opus** reviews the diff (correctness, CRLF, `.editorconfig`, AOT rules, no scope creep), then merges into
   `linux/main` in dependency order.
5. **Haiku** merges `upstream/main` into `linux/main` weekly and reports conflicts. **Opus** resolves non-trivial ones.

Windows builds can't run locally on Linux. Validate them through GitHub Actions on `windows-2025-vs2026`, with a
Haiku agent polling `gh run` results.

### 7.3 Workstream ownership (Phase 2 → 3)

Rule: a file belongs to exactly one workstream per wave. Two files are hot and must be serialized:
- `ShellViewModel.cs`: W1, then W8, then Phase 4.
- `Actions/**`: only W5 edits it.

| WS | Scope | Owns | Depends on |
|---|---|---|---|
| W1 | Enumeration + watchers | `Utils/Storage/Enumerators`, watcher parts of `ShellViewModel`, `SidebarViewModel`, `QuickAccessManager`, `LibraryManager` | P1 |
| W2 | File operations | `Utils/Storage/Operations`, `Utils/Shell/ShellFileOperations2.cs`, `WindowsBulkOperations*` | P1 |
| W3 | Attributes, properties, security | `Helpers/Win32/Win32Helper.Storage.cs`, `ViewModels/Properties/**`, `StorageSecurityService`, `Utils/Signatures` | P1 |
| W4 | Shell UX | `Utils/Shell/**` (except ShellFileOperations2, LaunchHelper), `ShellPreviewViewModel`, `Services/PreviewPopupProviders` | P1 |
| W5 | Launcher, clipboard, elevation | `Helpers/Navigation`, `Utils/Shell/LaunchHelper.cs`, `Win32Helper.Process.cs`, `Actions/**` | P1 |
| W6 | Drives, network, cloud, trash | `Services/Storage/*{Devices,Network,TrashBin}*`, `Utils/Cloud`, `DriveHelpers`, `MtpHelpers` | P1 |
| W7 | Settings store, app data, registry, shell integration | `Services/Settings`, `Services/Windows/*`, `Utils/Taskbar`, `RegistryHelpers`, `ApplicationData` call sites | P1 |
| W8 | Thumbnails, icons, imaging | `FileThumbnailHelper`, `StorageItemIconHelpers`, `ImagingService`, `IconCacheService`, `ListedItem` image parts | W1 |
| W9.x | Linux implementations | New files only, in `Files.Platform.Linux/<Area>/` + tests | matching W1–W8 interface |
| W10 | Legacy storage removal (Phase 4) | `Utils/Storage/StorageItems/**`, `ShellViewModel` | W1, W2, W8 |
| U1 | Uno: Controls | `src/Files.App.Controls/**` | Phase 0 spikes |
| U2 | Uno: App head, startup, resources | `Program.cs`, `App.xaml.cs`, csproj, `ResourceHelpers` | U1, W7 |
| U3 | Uno: UI blockers | files in §3.4 | U2 |

Suggested concurrency: 4–6 Sonnet agents at once. W1, W2, W5, W6, W7 and U1 can run fully in parallel right after
Phase 1.

### 7.4 Work-package template

```
WP-ID:        W2-a
Goal:         Introduce IFileOperationsService; Windows impl wraps ShellFilesystemOperations
Owns:         src/Files.App/Utils/Storage/Operations/**, src/Files.Platform.Windows/FileOperations/**
Must not edit: ShellViewModel.cs, Actions/**, AppLifecycleHelper.cs
Contract:     (interface signature pasted here by Opus)
Acceptance:   - Windows CI green
              - rg --no-ignore 'PInvoke\.|ShellFileOperations2' on owned consumer files → 0 hits
              - CRLF preserved, no new DllImport/ComImport
Runner checks: dotnet build src/Files.Platform.Abstractions; dotnet test tests/Files.Platform.Tests
```

## 8. Risks & mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Uno gaps (InteractionTracker, title bar, TabView tear-out, InputKeyboardSource) | UI features missing | Phase 0 spike; degrade behind capability flags |
| Upstream churn in hot files (`ShellViewModel`, layout pages) | Painful merges | Weekly upstream merges; additive/partial-class style; keep moved code byte-identical where possible |
| Windows build can't be verified locally | Silent Windows breakage | Mandatory Windows CI on every `linux/*` PR |
| File-operation correctness (data loss) | Critical | Extensive temp-dir unit tests; cross-device move, conflict, cancel, permission-denied, symlink cases |
| Performance of managed enumeration / thumbnails | Sluggish large folders | Benchmarks against 100k entries; batch `stat`; async thumbnail queue with XDG cache |
| Flatpak sandbox vs file manager needs | Limited access | `--filesystem=host`; document; AppImage/AUR as alternatives |
| Native AOT / trimming with Uno + new code | Build failures | Keep AOT analyzers on; follow `CLAUDE.md` reflection rules |
| Localisation pipeline (`.resw`/MRT) | Missing strings | Centralised loader swap in Phase 5c |

## 9. Immediate next steps

1. ~~Install the SDK~~ (done; neutral projects build on Linux).
2. Phase 0: Linux CI job (one Sonnet WP, Haiku verifies).
3. Run Spikes A and B (Sonnet in worktrees, Opus reviews). Confirm the Uno decision.
4. Phase 1 skeleton (one Sonnet WP, serial).
5. Launch the Phase 2 wave: W1, W2, W5, W6, W7, U1 in parallel.
