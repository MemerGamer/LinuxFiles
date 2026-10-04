# Phase 0 Spikes A + B: Uno Platform host for Files.App.Controls

Date: 2026-10-04 · Branch `linux/p0-uno-spike` (based on `linux/main` 09e42fcb4) · Host: Garuda Linux, KDE Plasma
Wayland session (XWayland on `DISPLAY=:0`), .NET SDK 10.0.112.

## Verdict

**GO for Uno Platform (Skia desktop).** `Files.App.Controls` (Omnibar, SidebarView, BreadcrumbBar, Toolbar, ThemedIcon,
Storage controls, GridSplitter, BladeView, AdaptiveGridView) builds for `net10.0-desktop` with 0 errors / 1 warning and
renders and behaves correctly in a real Uno Skia host on Linux. The cost was five small, mechanical source edits plus one
build-system finding that matters for the rest of the port (source-generator chaining, see "Key finding").

## Versions

| Item | Version |
|---|---|
| Uno.Sdk (msbuild-sdk, `global.json`) | 6.7.30 (latest stable; 7.0.0-dev.* exists but was not used) |
| Uno.WinUI | 6.7.135 (latest stable) |
| Uno.WinUI.Runtime.Skia.X11 (implicit via Uno.Sdk) | 6.7.135 |
| CommunityToolkit.WinUI.Extensions | 8.2.251219 (unchanged; its `net9.0` lib is the Uno-compatible one, same package ID) |
| CommunityToolkit.Labs.WinUI.DependencyPropertyGenerator | 0.1.250206-build.2040, Windows TFM only now |
| Windows App SDK (Windows TFM) | 2.5.1 (unchanged) |

## Spike B: csproj approach

`src/Files.App.Controls/Files.App.Controls.csproj` now uses `<Project Sdk="Uno.Sdk">` and
`<TargetFrameworks>$(WindowsTargetFramework);$(DesktopTargetFramework)</TargetFrameworks>`.

Why Uno.Sdk and not plain `Microsoft.NET.Sdk` + `Uno.WinUI`: the .NET SDK rejects the `-desktop` target platform
(`NETSDK1139: The target platform identifier desktop was not recognized`). Only Uno.Sdk teaches it the `desktop` TFM.

Changes:

- `global.json`: `"msbuild-sdks": { "Uno.Sdk": "6.7.30" }`. Only projects that opt in to `Sdk="Uno.Sdk"` resolve it.
- `Directory.Build.props`: **renamed the repo's `TargetFrameworkVersion` property to `BaseTargetFramework`** (and the 4 csproj
  files that use it: Files.Shared, Files.Core.Storage, Files.InteractionTests, Files.App.Controls). `TargetFrameworkVersion`
  is a built-in MSBuild property (`v10.0`); Uno.Sdk pulls in .NET workload manifests that evaluate
  `VersionGreaterThanOrEquals($(TargetFrameworkVersion), 8.0)` and crash on the repo's `net10.0` value
  (`MSB4184`). Added `DesktopTargetFramework` = `$(BaseTargetFramework)-desktop`.
- `Directory.Packages.props`: `Uno.WinUI` 6.7.135 (central package management works with Uno.Sdk).
- csproj: Windows-only properties moved under `Condition="$(TargetFramework.Contains('-windows'))"`:
  `TargetPlatformMinVersion`, `UseWinUI`, `RuntimeIdentifiers`, and the package refs `Microsoft.WindowsAppSDK`,
  `Microsoft.Windows.SDK.BuildTools`, `Microsoft.Windows.CsWinRT`, `CommunityToolkit.Labs.WinUI.DependencyPropertyGenerator`,
  plus the three `PackageReference Update ... ExcludeAssets` lines. `Uno.WinUI` is referenced for the desktop TFM only.
- `EnableWindowsTargeting=true` when not on Windows, so restore can resolve the Windows TFM on Linux.
- `DisableImplicitUnoPackages=true` and `UnoSingleProject=false`: stop Uno.Sdk injecting Uno.Resizetizer, Uno.Sdk.Extras,
  Uno.Settings.DevServer, logging adapters etc. into the library (and into the Windows TFM).

Note: on Linux, Uno.Sdk silently drops the Windows TFM from `TargetFrameworks`, so `dotnet build src/Files.App.Controls`
builds only `net10.0-desktop`. On Windows both TFMs are built.

### Why the Windows build should be unaffected

I cannot build the Windows TFM on Linux (WinUI XAML compiler is Windows-only). Instead I evaluated the project for
`net10.0-windows10.0.26100.0` with both the old (`linux/main`) and the new csproj and diffed properties
(`UseWinUI`, `TargetPlatformMinVersion`, `RuntimeIdentifiers`, `DefineConstants`, `IsAotCompatible`, `SupportedOSPlatformVersion`,
`OutputType`, `Nullable`, `AllowUnsafeBlocks`, ...) and items (`PackageReference`, `Page`, `Compile`, `Analyzer`,
`ProjectReference`, `Reference`, `FrameworkReference`). The only difference is an empty `UnoFeatures` property. Before adding
`DisableImplicitUnoPackages`/`UnoSingleProject=false`, Uno.Sdk added six implicit PackageReferences (Uno.WinUI,
Uno.Resizetizer, ...) to the Windows TFM; these are now gone. Remaining Windows risks, to be verified by the Windows CI job:
(1) Uno.Sdk imports on a Windows host for the `-windows` TFM (the evaluation above ran with Windows targeting, not on Windows);
(2) `Files.App` consuming a now multi-targeted project reference; (3) the `#if WINDOWS` branch in `ThemedIcon.Properties.cs`
keeps the original `[GeneratedDependencyProperty]` declaration verbatim for Windows. All new Uno-only files are guarded by
`#if !WINDOWS`.

## What was needed to build `net10.0-desktop` (every exclusion/shim)

No `Compile Remove` was necessary. Nothing was excluded; there are no `#if` exclusions of unsupported APIs. Changes:

| # | Where | Problem on Uno | Fix |
|---|---|---|---|
| 1 | `Uno/WinRT.Shims.cs` (new, `#if !WINDOWS`) | `WinRT.DynamicWindowsRuntimeCastAttribute` (C#/WinRT) and `using WinRT;` in 25 files | No-op attribute in namespace `WinRT` |
| 2 | `Uno/GeneratedDependencyProperty.cs` (new, `#if !WINDOWS`) | CommunityToolkit.Labs DependencyPropertyGenerator ships its attribute only for `-windows` TFMs and its generator **silently emits nothing on Uno** (it needs WinAppSDK-only types, e.g. `CreateDefaultValueCallback`) | Declare the attribute; do not reference the package on the desktop TFM |
| 3 | `Files.Core.SourceGenerator/Generators/UnoDependencyPropertyGenerator.cs` (new) | 75 `[GeneratedDependencyProperty]` partial properties in Controls need an implementation | Small incremental generator (DependencyProperty.Register + `OnXChanged(new)`, `OnXChanged(old,new)`, `OnXPropertyChanged(args)` partials, `DefaultValue`). Opt-in via MSBuild property `FilesUnoDependencyProperty=true` (set only for the desktop TFM), so it is inert for Windows |
| 4 | `ThemedIcon/ThemedIcon.Properties.cs` | See "Key finding" | `Layers` DP hand-written under `#else` of `#if WINDOWS` |
| 5 | `AdaptiveHeightValueConverter.cs`, `BreadcrumbBarItemAutomationPeer.cs`, `SidebarItemAutomationPeer.cs`, `SidebarViewAutomationPeer.cs`, `Omnibar.Events.cs` | Uno's nullable annotations differ (`GetPatternCore` returns `object?`, `Setter.Value` is `object?`, `FocusManager.GetFocusedElement(XamlRoot?)`); repo has `WarningsAsErrors=nullable` | Added `!` at 5 call sites (no behaviour change; no warning on Windows) |

Remaining warning (not fixed): `CS0108 AdaptiveGridView.ItemClickCommand hides inherited ListViewBase.ItemClickCommand`
(Uno's `ListViewBase` has that member; WinUI's does not).

APIs that compiled without any change on Uno: `Microsoft.UI.Input` (PointerUpdateKind etc.), `Windows.ApplicationModel.DataTransfer`,
`Windows.System.VirtualKey`, `Microsoft.UI.Xaml.Automation.Peers`/`Provider`, `ItemsRepeater`, `x:Load`, `x:Bind`,
`ThemeResource`, `VisualStateManager`, `TabView`, `XamlControlsResources`, `ms-appx:///Files.App.Controls/...` resource URIs.

## Key finding: Roslyn generators do not chain on Uno

Uno's XAML generator is itself a Roslyn source generator and cannot see members emitted by other generators. On WinUI the XAML
compiler runs after C# compilation and sees them. Consequence: any dependency property produced by `[GeneratedDependencyProperty]`
(or my stand-in) is invisible to Uno's XAML generator, which then treats `<Setter Property="X">` on that property as a
"legacy" (non-DP) setter. Simple literal values still work through a plain CLR assignment (they lose Style/DP precedence
semantics), but object-element values fail with the misleading
`UXAML0001: MarkupExtension 'ThemedIconLayers' is not supported` (220 errors in `ThemedIcon/Styles/*.xaml`). Fixed for the one
affected property (`ThemedIcon.Layers`) by declaring the DP in plain source. For the full app this needs a durable
answer: either hand-write (or pre-generate into committed or obj-time files that are real compile inputs) DPs that XAML
touches with complex values, or give the XAML generator a two-pass build. Also affects every other generator-produced
member referenced by XAML in `Files.App` (e.g. CommunityToolkit.Mvvm `[ObservableProperty]` is fine for `x:Bind`, because
x:Bind generates C# that Roslyn compiles later, but `{Binding}`/`TemplateBinding`/Setter-by-name need a visible DP field).

## Spike A: host app

`spikes/UnoHost/` (net10.0-desktop, `Sdk="Uno.Sdk"`, `SkiaRenderer`, not in `Files.slnx`): Omnibar with an
`OmnibarMode` whose inactive content is a BreadcrumbBar (templated `BreadcrumbBarItem`s), a Toolbar with
`ToolbarItem`s (button, separator, toggle) and `ThemedIcon` styles, a SidebarView with `FlatSidebarItem` rows and inner content, a
TabView (add/close handlers), a 200-row ListView with an `x:Bind` `DataTemplate` (`x:DataType`), `x:Load`, a `VisualStateManager`
state group with `Setter Target=`, and `ThemeResource` brushes. Resources merge `Files.App.Controls/Themes/Generic.xaml`.

Build: `dotnet build spikes/UnoHost` is clean (0 warnings, 0 errors).

### Runtime results

| Session | Result |
|---|---|
| X11 via XWayland (`DISPLAY=:0`, KDE Wayland session) | Works. Window 1024x640, all controls render, log empty (no exceptions). |
| Native Wayland (`DISPLAY` unset) | **Does not work**: `InvalidOperationException: No platform host could be selected` (exit 134). Uno 6.7 ships Skia hosts for X11, Linux framebuffer, macOS and Win32 only; there is no Wayland host package. |
| Pure X11 session | Not testable on this machine (only a Wayland session available); expected to work since the XWayland path is the X11 host. |

Interaction test via `xdotool` on the XWayland window (`screenshots/x11-interact.png`): clicking "Toggle VSM" switched the
visual state ("Compact state active"), the TabView "+" button added "Tab 3", clicking a row selected it (selection brush),
the breadcrumb displayed `> home > user > Documents >` with chevrons when the Omnibar was not focused, and focus showed the
editable path text box (`screenshots/x11-xwayland.png`).

Screenshots (captured per window with ImageMagick `import -window`, because `spectacle -a` under Wayland captured other
windows):

- `spikes/UnoHost/screenshots/x11-xwayland.png`: initial state, Omnibar in edit mode, Toolbar, Sidebar, TabView, list.
- `spikes/UnoHost/screenshots/x11-interact.png`: after clicks; breadcrumb visible, VSM toggled, new tab, selected row.

Observations: system dark theme picked up; Fluent styles and `ThemedIcon` vector icons render; the Toolbar toggle icon is black on the
accent-bordered button (a ThemedIcon colour state worth checking later); window decorations come from KWin (system title bar).
Segoe UI Variable / Segoe Fluent Icons are not installed here, so text uses a fallback font.

## Open questions

1. **Native Wayland**: Uno 6.7 has no Wayland host. XWayland works everywhere today, but fractional scaling/HiDPI, IME and
   clipboard/DnD behaviour under XWayland need Phase 7 testing. Watch Uno 7.0 (dev builds) for a Wayland host.
2. **Windows CI**: confirm the Windows TFM still builds with `Sdk="Uno.Sdk"`, and that Uno.Sdk's `.NET workload` manifest probing
   does not slow or break Windows restore. If it does, fall back to a sibling `Files.App.Controls.Uno.csproj` that links the
   same sources (keeps the Windows csproj byte-identical).
3. **Generator chaining** (above): decide how Files.App's many `[GeneratedDependencyProperty]`/`x:Bind` consumers are handled;
   same issue for any DP-producing generator Files.App uses.
4. `Files.App` is a much larger test: `InputKeyboardSource`, `AppWindow` title bar, InteractionTracker, Win2D (SpeedGraph),
   `.resw` loading, `TabView` tear-out are not exercised by this spike.
5. The `UnoDependencyPropertyGenerator` is minimal (no `IsLocalCacheEnabled`, `DefaultValueCallback`, nested types, generic
   types, nullable-value-type `typeof`). Extend as Files.App needs.
6. Uno.Sdk pins: `global.json` now holds `Uno.Sdk` 6.7.30; Dependabot/Renovate rules may need to know.
7. CRLF: files in this branch were written with LF; the repo's `.gitattributes` has `text=auto`, so committed blobs are LF-normalized.

## Recommendation

**Go with Uno Platform Skia desktop** for the UI layer. The control library, the most custom-templated part of the UI, ports with
trivial edits, and XAML features the plan worried about (`x:Bind`, `x:Load`, `ThemeResource`, VSM, TabView, templated items) work in a
real window on Linux. Proceed to Phase 5a on this csproj shape, but schedule (a) a Windows CI run of this branch before
merging, and (b) a decision on generated dependency properties before multi-targeting `Files.App`.
