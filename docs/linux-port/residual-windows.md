# Residual Windows audit

Audit scope: `src/`, `tests/`, and central package declarations. Reference checks cover all tracked C#, XAML and project files (including project exclusions); discovery-driven tests, source generators, extension methods and command generation are not dead merely because their type names have no direct callers. This is a static audit, not desktop runtime validation.

## Removed after reference checks

- Isolated NTFS/ReFS security graph: `IStorageSecurityService`, `AccessControlList`, `AccessControlEntry`, `AccessControlPrincipal`, `AccessMaskItem`, `SecurityAdvancedAccessControlItemFactory`, and their access-control enums and `FileSecurityResult`. No consumers outside that graph.
- Unreferenced `RegistryHelpers` (including backslash registry-key combining), `PowerShellExecutionOptions`, `WallpaperType`, `DeviceEvent`, `IDataTransferManagerInterop`, and `Win32Extensions.IsHandleInvalid`.
- Unreferenced `HBITMAP_HMENU`, `Win32ContextMenu`, and `ShowWindowCommandExtensions.ToWin32`. Retained `Win32ContextMenuItem`: the Linux Open-with menu uses its command tag. Retained `ToShowWindowCommand`: `FolderSearch` still calls it.
- Unreferenced `PathHelpers.Combine`, including its backslash-to-slash replacement; no qualified calls or static imports in C#/XAML/project files.
- Unused `Microsoft.Win32` import in `RecentFilesWidgetViewModel`.
- `Appium.WebDriver` central version: no package references in repository project/props/targets files. Every other central package has a reference; WinUI/Uno names alone do not indicate Windows-only dependencies.

## Referenced residuals requiring follow-up

| Area | Evidence and remaining decision |
| --- | --- |
| Advanced settings | `AdvancedViewModel` still binds registry-based default Explorer/open-dialog commands and Windows startup tasks from `AdvancedPage.xaml`. Visibility guards hide them on Linux, but removing them requires coordinated XAML/view-model changes. UAC and regedit/regsvr32 code remains. |
| General settings | `MoveShellExtensionsToSubMenu` has menu/layout consumers; `ShowOpenTerminal` has Linux terminal consumers. The contract still describes Windows Terminal. Remove shell-only behavior separately; retain the terminal capability and review localized labels. |
| Registry serialization | `[Generator] RegistrySerializationGenerator` is discovered by Roslyn and `RegistrySerializable`/`RegistryIgnore` annotations remain on tags and column models. Audit emitted methods and settings migration before removing this pipeline; a one-reference type count is insufficient. |
| Lifecycle/about | `AppLifecycleHelper` has registry-based app information initialization; `AboutViewModel` retains registry/default-Explorer and Windows-version paths alongside Linux guards. Startup/telemetry semantics need review. |
| Preview providers | `PreviewPopupService` still calls `QuickLookProvider` and `PowerToysPeekProvider`; Peek probes the Windows uninstall registry. Desktop preview registration and fallback need checking before removing the chain. |
| WSL | `WSLDistroManager` enumerates Lxss registry entries and constructs UNC paths; sidebar, navigation and initialization still reference it and WSL settings. Remove the complete feature, not just its manager. |
| Cloud detection | `CloudDetector` constructs `GoogleDriveCloudDetector`; its registry mount paths and drive-letter normalization remain referenced. Review Linux mount discovery and cloud settings before deleting. |
| Trash naming | `RecycleBinPath`, Recycle Bin strings and `IStorageTrashBinService` are used by Linux trash/navigation/quick-access implementations. They are not dead Windows code. Rename labels/contracts with migration and translation review rather than deleting Linux trash functionality. |
| Drive letters | `GeneralPage.xaml.cs` still invokes `GetDriveLetterToken`/`RemoveDriveLetterToken` when editing labels; `DrivesViewModel` has legacy drive identification comments. Review Linux volume label behavior before removing branches. |
| Paths/shortcuts | `TryGetFullPath` launches `where.exe` and is called by `CreateShortcutDialogViewModel`. `NavigationHelpers` trims both separators; `StorageFileExtensions` accepts `~\` as well as `~/`. These can mishandle legal Linux backslash filenames and need focused behavior tests. `FolderSearch` still reads Windows shortcut show commands. |
| Wallpaper naming | `IsCompatibleToSetAsWindowsWallpaper` remains used by wallpaper and image-rotation actions. Its image-extension predicate has Linux consumers; review the name independently of behavior. |
| Tests/interop | Tests use `LinuxApplicationRegistry` and `Microsoft.Win32.SafeHandles` for real Linux functionality. MSTest discovers test classes by attributes; do not delete them for lacking explicit references. RESW schema `System.Windows.Forms` reader/writer metadata is format metadata, not an app dependency. |

## Windows-only resource candidates

Translation RESW files are checked-in localized content, with no evidence that every translation file is generated. Per the task requirement, **no RESW files were changed, including en-US**. The following base keys match Windows-specific features and have no literal C#/XAML/project references after cleanup. Searches include dotted key prefixes and generated identifier spellings; dynamic resource lookup still needs review before deletion.

- `CompatibilityOnWindowsLogin`
- `ErrorApplyingTagContent`
- `ErrorCreatingDataStreamDescription`

Windows-related keys with remaining references (not deletion candidates):

`OpenStorageSense`, `OpenStorageSenseDescription`, `WSL`, `SettingsOpenInLogin`, `RunWithPowerShell`, `RecentFilesDisabledOnWindowsWarning`, `WindowsVersion`, `OpenTerminal`, `OpenTerminalAsAdmin`, `RunWithPowershellDescription`, `Key.Windows`, `TurnOnBitLocker`, `ManageBitLocker`, `FilesRunningAsAdminContent`, `OpenOnStartupDisabled`, `ShowOpenTerminal`, `PowerShellFiles`.

## Validation

- C#/XAML/project reference checks and central package-reference inventory completed; `git diff --check` passed and existing CRLF endings were preserved.
- Single-node `dotnet build tests/Files.Platform.Tests -m:1 -nr:false -p:BuildInParallel=false -p:NuGetAudit=false` passed with zero warnings/errors. Initial restore failed because vulnerability feeds were inaccessible; audit was disabled only for this invocation, without changing repository settings.
- Direct runner: 1,262 tests, 1,208 passed, 13 failed, 41 skipped. Failures included private X-server socket/display startup and trusted native-directory assertions; the suite did not pass in this sandbox.
- Desktop app build/run was not performed under the supplied sandbox constraint; deleted app types are verified by reference searches, not app compilation. No commit was made.
