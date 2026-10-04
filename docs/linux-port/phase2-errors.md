# Phase 2: Files.App on Uno (net10.0-desktop) build inventory

Branch `linux/p2-head`. Generated from `dotnet build src/Files.App -f net10.0-desktop` (Uno.Sdk 6.7.30, Uno.WinUI 6.7.135).

## How the build is organised

- `Files.App` and `Files.App.Storage` target `net10.0-desktop` only. The Windows TFM is not built (Linux-first); Windows-only properties,
  packages and project references are conditioned on `$(TargetFramework.Contains('-windows'))` so they can come back.
- `Platforms/Desktop/` holds the desktop host (`Program.cs`, X11), the desktop startup path (`App.Desktop.cs`), a `WindowEx` replacement,
  attribute shims (`Shims/`) and the checked-in `ICommandManager` snapshot (`Generated/`, regenerate with `scripts/linux/regen-command-manager.sh`).
- `App.xaml.cs` keeps the WinAppSDK activation/tray/"leave running" code under `#if WINDOWS`; `AppLifecycleHelper` registers `AddLinuxPlatform()`
  on desktop and skips the Windows service implementations (their interfaces are still in `Data/Contracts`).

### Roslyn declaration errors hide everything else

`csc` stops after declaration binding when there are declaration errors, so the default build only ever reports the *declaration-level*
errors (section A). Method-body errors appear only once those are gone. To see the rest anyway there is an opt-in lens:

```
dotnet build src/Files.App.CsWin32 -p:EnableWindowsTargeting=true -p:Platform=x64      # once
dotnet build src/Files.App -f net10.0-desktop -p:FilesWin32Compat=true                  # section B
```

`FilesWin32Compat=true` compiles the Win32/Shell-COM code against the (Windows-built, but Linux-buildable with `Platform=x64`) CsWin32 assembly and
re-includes `Files.App.Storage/Windows/**`, `Helpers/Win32/**`, `Utils/Shell/**`, `Utils/Taskbar/**`, `Utils/Signatures/**`, `Services/Windows/**`.
It is a *measurement tool*: nothing compiled that way can run on Linux. The goal is to delete the Win32 usage (section C), not to ship this mode.

## A. Default build


Total errors: **82** (0 XAML-compiler UXAML*, 0 C# errors inside Uno-generated XAML code-behind, 82 in hand-written C#)

By error code: CS0246 35, CS0234 29, CS0122 17, CS0103 1

| Folder | Errors | Files | Top codes |
|---|---:|---:|---|
| Utils/Storage | 33 | 14 | CS0246 12, CS0234 11, CS0122 9, CS0103 1 |
| Data | 25 | 12 | CS0246 16, CS0122 5, CS0234 4 |
| ViewModels | 8 | 6 | CS0234 6, CS0246 2 |
| Services | 8 | 3 | CS0234 5, CS0122 3 |
| Utils/Serialization | 3 | 1 | CS0234 3 |
| UserControls | 2 | 1 | CS0246 2 |
| Views | 2 | 1 | CS0246 2 |
| Helpers | 1 | 1 | CS0246 1 |

Top missing symbols overall:

`SHOW_WINDOW_CMD` x15, `Windows.Win32.Storage.FileSystem` x13, `WIN32_ERROR` x6, `Win32PInvoke` x4, `Windows.Win32.Security` x4, `IWindowsStorable` x3, `HWND` x3, `Files.App.Helpers.Win32PInvoke` x3, `BOOL` x2, `WIN32_FIND_DATAW` x2, `Windows.Win32.System.WinRT` x2, `ShellItem` x2, `STGMEDIUM` x2, `IStream` x2, `OpenWithMenu` x2, `ContextMenu` x1, `Disposable` x1, `Windows.Win32.UI.Shell.SLR_FLAGS` x1, `STATSTG` x1, `Windows.Win32.NetworkManagement` x1, `Files.App.Helpers.Win32Helper` x1, `IShellItem` x1, `Windows.Win32.UI.Input` x1, `ShellFileOperations2` x1, `Files.App.Storage.TaskbarManager` x1

#### Per folder: top 15 symbols

- **Utils/Storage** (33): `Windows.Win32.Storage.FileSystem` x7, `SHOW_WINDOW_CMD` x4, `Win32PInvoke` x4, `BOOL` x2, `ShellItem` x2, `STGMEDIUM` x2, `IStream` x2, `Disposable` x1, `Windows.Win32.UI.Shell.SLR_FLAGS` x1, `HWND` x1, `WIN32_FIND_DATAW` x1, `STATSTG` x1, `ShellFileOperations2` x1, `Windows.Win32.System.WinRT` x1, `Windows.Win32.Foundation.PROPERTYKEY` x1
- **Data** (25): `SHOW_WINDOW_CMD` x10, `WIN32_ERROR` x3, `IWindowsStorable` x3, `HWND` x2, `ContextMenu` x1, `Windows.Win32.Security` x1, `IShellItem` x1, `Files.App.Helpers.Win32PInvoke` x1, `Windows.Win32.UI.Input` x1, `MENU_ITEM_TYPE` x1, `Windows.Win32.Storage.FileSystem` x1
- **ViewModels** (8): `Windows.Win32.Storage.FileSystem` x3, `WIN32_FIND_DATAW` x1, `Windows.Win32.System.WinRT` x1, `Files.App.Storage.TaskbarManager` x1, `FindCloseSafeHandle` x1, `Files.App.Helpers.Win32PInvoke` x1
- **Services** (8): `WIN32_ERROR` x3, `Windows.Win32.Security` x3, `Windows.Win32.NetworkManagement` x1, `Windows.Win32.Storage.FileSystem` x1
- **Utils/Serialization** (3): `Files.App.Helpers.Win32Helper` x1, `Files.App.Helpers.Win32PInvoke` x1, `Windows.Win32.Storage.FileSystem` x1
- **UserControls** (2): `OpenWithMenu` x2
- **Views** (2): `WindowMessageMonitor` x1, `WindowMessageEventArgs` x1
- **Helpers** (1): `SHOW_WINDOW_CMD` x1


## B. Inventory mode


Total errors: **145** (0 XAML-compiler UXAML*, 32 C# errors inside Uno-generated XAML code-behind, 113 in hand-written C#)

By error code: CS0103 57, CS1729 14, CS1950 12, CS1503 12, CS8604 12, CS0122 8, CS0246 8, CS8600 4, CS1061 3, CS8619 3, CS8602 3, CS0403 3, CS8601 2, CS0234 1, CS0117 1, CS8603 1, CS8625 1

| Folder | Errors | Files | Top codes |
|---|---:|---:|---|
| UserControls | 32 | 10 | CS1950 8, CS1503 8, CS0122 8, CS0103 3 |
| Views | 32 | 9 | CS0103 10, CS8604 7, CS8600 4, CS1950 4 |
| ViewModels | 25 | 9 | CS0103 17, CS0246 7, CS8604 1 |
| Helpers | 18 | 9 | CS1729 6, CS0403 3, CS8602 2, CS0103 2 |
| Utils/Storage | 13 | 5 | CS0103 5, CS8619 3, CS1061 2, CS1729 2 |
| Actions | 9 | 9 | CS0103 6, CS1729 2, CS8604 1 |
| Data | 7 | 5 | CS0103 7 |
| Styles | 4 | 1 | CS0103 4 |
| Services | 4 | 1 | CS0103 3, CS0246 1 |
| (root files) | 1 | 1 | CS1729 1 |

Top missing symbols overall:

`DriveHelpers` x37, `RectInt32` x6, `SizeInt32` x4, `PointInt32` x4, `_ProgressBarTrackSubject` x4, `CanvasPathBuilder.AddLine(Vector2)` x3, `CreateSafeFileHandle` x2, `CardsBrowserTemplate` x2, `GridViewBrowserTemplate` x2, `FontFileHelper` x2, `ShellPreviewViewModel` x2, `CodePreviewViewModel` x2, `RegularItemContainerStyle` x2, `As` x2, `CompactItemContainerStyle` x2, `ShellPreview` x2, `ListViewBrowserTemplate` x2, `CanvasPathBuilder.BeginFigure(Vector2, CanvasFigureFill)` x1, `WinRT.MarshalInterface` x1, `CanvasPathBuilder` x1, `CodePreview` x1, `AppToastNotificationHelper` x1, `MarkdownPreview` x1, `MarkdownPreviewViewModel` x1, `WindowsStorageDeviceWatcher` x1

#### Per folder: top 15 symbols

- **UserControls** (32): `CanvasPathBuilder.AddLine(Vector2)` x3, `DriveHelpers` x2, `CanvasPathBuilder.BeginFigure(Vector2, CanvasFigureFill)` x1, `CanvasPathBuilder` x1, `As` x1, `CanvasPathBuilder.EndFigure(CanvasFigureLoop)` x1, `CanvasFigureLoop` x1, `TabItemContextMenu` x1, `CanvasGeometry` x1; 20 other (nullability, ctor, conversion...)
- **Views** (32): `CardsBrowserTemplate` x2, `GridViewBrowserTemplate` x2, `RegularItemContainerStyle` x2, `CompactItemContainerStyle` x2, `ListViewBrowserTemplate` x2, `RectInt32` x1, `PointInt32` x1, `SizeInt32` x1; 19 other (nullability, ctor, conversion...)
- **ViewModels** (25): `DriveHelpers` x15, `ShellPreviewViewModel` x2, `CodePreviewViewModel` x2, `ShellPreview` x2, `CodePreview` x1, `MarkdownPreview` x1, `MarkdownPreviewViewModel` x1; 1 other (nullability, ctor, conversion...)
- **Helpers** (18): `RectInt32` x5, `WinRT.MarshalInterface` x1, `PointInt32` x1, `As` x1, `AppToastNotificationHelper` x1, `DriveHelpers` x1; 8 other (nullability, ctor, conversion...)
- **Utils/Storage** (13): `DriveHelpers` x3, `CreateSafeFileHandle` x2, `FontFileHelper` x2, `SizeInt32` x1, `PointInt32` x1; 4 other (nullability, ctor, conversion...)
- **Actions** (9): `DriveHelpers` x6, `SizeInt32` x2; 1 other (nullability, ctor, conversion...)
- **Data** (7): `DriveHelpers` x7
- **Styles** (4): `_ProgressBarTrackSubject` x4
- **Services** (4): `DriveHelpers` x3, `WindowsStorageDeviceWatcher` x1
- **(root files)** (1): `PointInt32` x1

#### C# errors inside Uno-generated XAML code (attributed to the .xaml page)

- `UserControls/TabBar/TabBar.xaml`: CS1950 x5, CS1503 x5, CS0103 TabItemContextMenu x1
- `UserControls/Toolbar.xaml`: CS1950 x3, CS1503 x3
- `Styles/StatusCenterStyles.xaml`: CS0103 _ProgressBarTrackSubject x4
- `Views/Settings/SettingsPage.xaml`: CS1950 x2, CS1503 x2
- `Views/HomePage.xaml`: CS1950 x2, CS1503 x2
- `Views/Layouts/GridLayoutPage.xaml`: CS0103 CardsBrowserTemplate x1, CS0103 GridViewBrowserTemplate x1, CS0103 ListViewBrowserTemplate x1


## C. Win32 / WinRT usage that the default build will report once declaration errors are cleared

Counts are `files/call sites` per category, in code that is still compiled on desktop (the excluded folders are not counted). This is the
forecast for the default build: every one of these becomes an error until it is routed through `Files.Platform.*` or removed.
Categories: 1 `Windows.Win32`/`PInvoke.`; 2 `Win32Helper`/`Win32PInvoke`; 3 Shell COM wrappers (`ShellItem`, `ContextMenu`, `OpenWithMenu`, `LaunchHelper`, ...);
4 WinRT `StorageFile`/`StorageFolder`/`BaseStorage*` (mostly compiles on Uno, kept for Phase 4); 5 WinAppSDK-only (`AppInstance`, `AppNotifications`,
`WinRT.Interop`, Win2D); 6 Registry; 7 `IWindows*Service`/`TaskbarManager`/`SystemTrayIcon`.

| Folder | F/S 1 | F/S 2 | F/S 3 | F/S 4 | F/S 5 | F/S 6 | F/S 7 |
|---|---:|---:|---:|---:|---:|---:|---:|
| Utils/Storage | 20/99 | 11/44 | 7/33 | 32/429 | 2/3 | 2/4 | 1/2 |
| ViewModels | 11/66 | 14/65 | 4/5 | 13/36 | 8/13 | 3/11 | 4/12 |
| Views | 5/13 | 3/7 | 1/5 | 2/2 | 5/79 |  |  |
| Helpers | 5/14 | 4/25 | 2/3 | 3/13 | 2/10 | 4/18 | 4/15 |
| Services | 7/68 | 3/5 | 1/1 | 4/9 | 1/1 | 2/4 |  |
| Data | 16/51 | 3/12 | 3/6 | 3/5 | 2/2 |  | 8/10 |
| (root files) | 3/16 | 2/9 | 2/3 | 1/2 | 2/10 |  | 1/11 |
| Actions | 2/6 | 10/21 | 3/3 | 5/16 |  | 1/2 | 1/2 |
| Utils/Cloud |  | 2/6 |  | 6/11 |  | 3/22 |  |
| Utils/FileTags | 1/2 | 1/9 |  | 1/2 |  | 1/17 |  |
| UserControls | 5/13 |  | 2/8 | 1/1 | 1/3 |  |  |
| Utils/Library | 1/3 |  | 1/11 |  |  |  |  |
| Extensions | 1/2 | 1/1 | 1/2 | 2/6 |  |  |  |
| Utils/Global |  |  |  | 1/4 |  | 1/2 |  |
| Utils/Serialization | 1/3 | 1/3 |  |  |  |  |  |

## D. Cross-cutting fixes already applied (do not redo)

See the PR description. Notable ones that explain symptoms agents will meet: `ResourceString.Key` (was `Name`), `ICommandManager` snapshot,
`[assembly: GeneratedWinRTExposedExternalType]`/`GeneratedBindableCustomProperty` shims, `Window.Content` non-null shadow in `WindowEx`,
`DefaultItemExcludes` for XAML pages, `ApplicationDefinition Include="App.xaml"`.

## E. Known Uno XAML limitations still open (all in section B's "Uno-generated XAML" list)

- `x:Load` on `MenuFlyoutItem`/`CommandBar` children (generated `Add(ElementStub)` fails): TabBar, Toolbar, HomePage, SettingsPage. Use `Visibility` or build the items in code.
- `x:Name` on resources (`DataTemplate`, `Style`, `MenuFlyout` inside `Resources`) does not create a field: GridLayoutPage (`*BrowserTemplate`, `*ItemContainerStyle`), TabBar (`TabItemContextMenu`). Use `x:Key` + lookup.
- `x:Name` on a `Rectangle` in a ControlTemplate referenced via `TemplateBinding` from a converter (`ProgressBarTrack` in `Styles/StatusCenterStyles.xaml`).
- `Windows.Graphics.SizeInt32/PointInt32/RectInt32` have no constructors on Uno (use object initializers).
- Nullability: Uno annotates `Window.Content`, `UIElement.XamlRoot`, `DataPackage.SetText` etc. as nullable; the repo treats nullable warnings as errors. Use `!` or a local guard.

## F. Recommended work packages (folder ownership)

Sizes are `.cs`+`.xaml` lines. Packages own disjoint folders so they can run in parallel; cross-package symbols (e.g. `DriveHelpers`) are owned by the
package named in brackets and the others should wait for it or stub against the interface.

| WP | Owns | Size | Main work |
|---|---|---:|---|
| 1 Storage ops | `Utils/Storage/{Operations,Helpers,Enumerators,Search,Collection,History}`, `Services/Storage/*` (except security/network), `Services/SizeProvider` | ~13k | Replace `Win32StorageEnumerator`, `FileOperationsHelpers`/`ShellFilesystemOperations` (IFileOperation, `SHOW_WINDOW_CMD` shortcuts, `WIN32_FIND_DATAW`), `FolderSearch`, `FileSizeCalculator`, `StorageHelpers` with `Files.Platform.*` interfaces |
| 2 Legacy storage items + drives [owns `DriveHelpers`] | `Utils/Storage/{StorageItems,StorageBaseItems}`, `Utils/Global`, `Utils/Cloud`, `Utils/Library`, `Utils/FileTags`, `Utils/Serialization`, `Utils/Git`, `Data/Items/{DriveItem,Widget*,Sidebar*}`, `Data/Models/{RemovableDevice,PinnedFoldersManager}` | ~9k | Recreate `DriveHelpers`/device watcher without `Windows.Devices.Portable`; stream/`IStream` (`StreamWithContentType`), shell items, cloud/registry detectors, `DefaultSettingsSerializer` Win32 file I/O |
| 3 Data layer | `Data/**` (minus WP2 files) | ~19k | `SHOW_WINDOW_CMD`, `WIN32_ERROR`, `AccessControlPrincipal`, `HotKey` (`Windows.Win32.UI.Input`), `ContextMenu`/`ShellLinkItem`/`ListedItem` shortcut fields, `IStorageSecurityService` contract, `WidgetFolderCardItem` (`IWindowsStorable`) |
| 4 Services + helpers | `Services/**` (not Storage/SizeProvider), `Helpers/**`, `Utils/{StatusCenter,CommandLine,Logger,Widgets}` | ~14k | Linux implementations of `IWindows*Service` replacements (wallpaper, recent items, ini, jump list, security, compatibility, start menu, quick access), `StorageSecurityService`/`StorageNetworkService`, toast notifications, `Win32Helper` call sites (`BringToForegroundEx`, `ForceWindowPosition`, process helpers), `Size/Point/RectInt32` ctors in `Helpers/UI` |
| 5 ViewModels | `ViewModels/**` | ~21k | `Properties/*` (`BaseProperties`, signatures, `SecurityAdvanced`), `ShellViewModel` (Win32PInvoke), `StatusCenterViewModel` (taskbar progress), `InfoPaneViewModel` (shell/code/markdown preview excluded), `QuickAccessWidgetViewModel`, `AdvancedViewModel` |
| 6 Views + Styles (XAML) | `Views/**`, `Styles/**`, `Converters/**` | ~25k | Uno XAML limits from section E (`x:Name` resources in `GridLayoutPage`, `x:Load` flyout items in `HomePage`/`SettingsPage`), layouts' `Win32`/drag-drop (`DataPackage.As`), Properties pages, nullable `XamlRoot` fixes |
| 7 UserControls + Dialogs | `UserControls/**`, `Dialogs/**` | ~14k | `TabBar`/`Toolbar` x:Load and `x:Name`, `SpeedGraph` (Win2D `CanvasPathBuilder` to SKPath), `Toolbar` `OpenWithMenu`, drives/network widgets, sidebar, `StatusCenter`, preview controls (Code/Markdown/Shell excluded; basic previews to verify), `DataPackage` interop |
| 8 Shell, window, startup | `MainWindow.xaml.cs`, `Platforms/Desktop/**`, `App.*`, `Actions/**`, `Extensions/**`, `Utils/Shell/**` (replacement design), `Utils/Taskbar/**`, `Utils/Signatures/**` | ~13k | `InitializeApplicationAsync` activation + `WindowHandle` users, single instance (`ISingleInstanceService`), tray, context menu / "Open with" abstraction replacing `Utils/Shell/ContextMenu`+`OpenWithMenu`, `Actions/**` (mostly `DriveHelpers` + nullable + `SizeInt32`), title bar/drag regions, `ShareItemHelpers`, launch (`LaunchHelper`) |

Suggested order inside every package: (1) make the package compile in default mode (section A errors in its folders go to zero),
(2) re-run the default build; new body-level errors appear once all packages clear declaration errors, so expect a second wave.
Package 4 should publish the replacement for `Win32Helper` helpers first; package 2 should publish `DriveHelpers` first.
