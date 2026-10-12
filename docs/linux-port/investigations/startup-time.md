# Startup time investigation

Reported baseline: host start to first window about 1.0 s warm; fresh Debian 13
container about 3.5 s warm and 6.8 s first run. These are inputs, not measurements
made in this sandbox. A mapped window, first backdrop render and usable folder
listing are different endpoints; record all three when comparing changes.

## Phases and tracing

Set `FILES_TRACE=1` for opt-in, bounded asynchronous stderr records. Existing
`PerformanceTrace` scopes report duration, parent id and thread, without paths.
New scopes cover:

| Phase | Trace operation | Work and ordering |
| --- | --- | --- |
| Root environment | startup.root-environment | Security checks before any host setup; must stay synchronous. |
| Instance ownership | startup.single-instance | D-Bus ownership/forwarding, socket fallback; must precede window creation. Existing 3 s connect and 5 s forwarding budgets retained to avoid duplicate windows. |
| Cursor | startup.cursor | X resources, then up to two read-only gsettings processes before host creation. |
| App resources | startup.app-resources | App XAML and exception hook setup; stale archive cleanup already runs on a worker. |
| Activation | startup.activation | DI, eager managers, placement, backdrop, navigation and component scheduling. |
| DI registration | startup.configure-services | File logger, registrations, provider build. Most singleton registrations are lazy. |
| User settings | startup.user-settings | Synchronous JSON settings initialization when first resolved; theme, placement and restored tabs depend on settings. |
| Window resources | startup.window-resources | MainWindow constructor/XAML/title setup (base constructor outside scope). |
| Theme discovery | startup.theme-detection | Portal read followed by GTK environment, KDE file and gsettings fallback. |
| First backdrop | startup.first-backdrop | Frame/theme/backdrop preparation, compositor commit, activation and first Rendered event with loaded root. |
| Navigation/components | startup.navigation-and-components | Command-line/session navigation and background component scheduling after backdrop render. |

Scopes overlap: do not sum nested durations. Program/native runtime loading before
App construction is not included in activation. Existing `dbus-connect`,
`dbus.theme`, `process-start` and file-operation traces provide further detail.
First-backdrop is a framework render acknowledgement, not proof that the display
server presented pixels. External window timestamps are still needed.

## Changes and expected savings

Desktop startup previously called Activate directly and immediately constructed
main content. It now uses the existing ActivateWithBackdropAsync helper, which
awaits a lightweight backdrop render before navigation. Terminal discovery and
Sentry configuration follow that render. Main content, drives and startup tasks
therefore cannot monopolize the UI thread before the backdrop acknowledgement.
Settings, placement, eager manager resolution and initial theme stay before it;
moving those blindly could break dependencies or flash the wrong theme.

Theme portal discovery now has a 250 ms synchronous wait, bounded connect and
read waits, and returns each read's result directly instead of shared `_lastRead`.
Late worker failures are caught. Theme gsettings waits shrink from 1500 to 250 ms;
cursor fallback waits shrink from 1500 to 250 ms per missing setting. Slow portal
sessions can initially use the local fallback; the existing portal watcher still
updates theme on change. User-action portal and mount timeouts remain unchanged.
The theme watcher may finish after the foreground wait; this is not cancellation
of a remote D-Bus method. Existing shared connection lifecycle is retained.

Expected savings, **unmeasured**:

- Unresponsive theme portal: up to 1.75 s less foreground waiting per detection.
- Slow theme gsettings fallback: up to 1.25 s less waiting per detection.
- Two slow cursor fallback processes: up to 2.5 s less waiting total.
- Deferring navigation, terminal discovery and Sentry: moves their pre-yield CPU
  and I/O cost after backdrop render; amount depends on session and first-run data.
- Healthy services: timeout reductions alone save approximately zero; process
  startup, XAML parsing, fontconfig and renderer initialization remain.

These are wait-budget differences, not additive predictions for the supplied
3.5/6.8 s timings. Process creation and termination, local file reads and native
calls are not hard-bounded by these waits. Container renderer choice, font caches,
filesystem caches and portal availability can explain substantial differences.

## Services, storage and icons

App.xaml.cs initializes XAML/handlers, not the entire service graph. App.Desktop.cs
registers services then eagerly resolves QuickAccessManager, StorageHistoryWrapper,
FileTagsManager, LibraryManager, logger and AppModel. AppModel is constructed on
the UI thread for Clipboard.ContentChanged. Preserve that affinity.

UserSettingsService/BaseJsonSettings and serializers use JSON files. The desktop
FileTagsDatabase.Linux implementation lazily loads JSON; StorageHistoryWrapper is
in memory. A SQLite package reference does not establish a startup DB open.
InitializeAppComponentsAsync already schedules pinned items, libraries, cloud
sections and tag updates on workers after navigation. DrivesViewModel constructs
a watcher and enumerates via GetDrivesAsync when updated; this work is downstream
of navigation. Keep mount/polkit operation budgets unchanged.

LinuxIconThemeProvider discovers theme via environment and GTK/KDE configuration
and indexes icon files; it does not spawn a portal/gsettings process for theme
selection. MainWindow already schedules its window icon after the next render.
PortalWallpaperService is invoked for wallpaper actions, not eager startup
wallpaper discovery; DesktopWallpaperService is a stub. Its 5 s call/10 minute
confirmation budgets should not be shortened for startup.

## Publishing, JIT and globalization

Files.App.csproj already sets PublishReadyToRun=true for non-Debug builds with a
RID, with composite R2R disabled. scripts/linux/publish.sh supplies Release and a
Linux RID, so its tarball publish already requests R2R. The script now explicitly
passes PublishReadyToRun=true so this packaging intent is visible. The analysis
publish profile also enables it. No published tarball was available for binary
verification here. The previous project comment's 25–30% claim is not a measured
result for the supplied Debian environment.

R2R does not eliminate assembly loading, XAML initialization, native library
loading or all JIT compilation. No explicit tiered compilation/TieredPGO or
runtime ReadyToRun overrides were found in the app/publishing paths examined;
retain runtime defaults rather than trading later responsiveness for speculative
startup gains. Benchmark the same Release RID publication with R2R on/off, and
inspect Files.runtimeconfig.json plus DOTNET_/COMPlus_ overrides. Compare file
size and page faults as well as latency; a larger R2R payload can cost cold I/O.

Do not enable InvariantGlobalization for Files.App. AppLanguageHelper constructs
named cultures and the date formatter uses the selected language's CultureInfo;
localized formatting and sorting need real globalization. Only the isolated
ElevationHelper explicitly uses invariant mode. Preserve app ICU availability;
measure its loading cost rather than disabling culture support.

## Validation and next measurements

The sandbox cannot build/run the Uno desktop app. Platform tests validate the
Linux changes, but do not compile App.Desktop/MainWindow. Review the app changes
by reference searches and run desktop build and UI checks on a supported host.
Use scripts/linux/headless-run.sh with its private display/throwaway home; never
use the user's display or real settings. Capture FILES_TRACE stderr for repeated
warm runs and fresh-home first runs of the same published artifact. Record runtime
version, RID, renderer, portal availability and cache state. Compare backdrop and
listing readiness separately, including closing the window during startup.

Validation in this sandbox: single-node platform build passed with
`-p:NuGetAudit=false` (network auditing failed on the initial attempt); direct
runner: 1208 passed, 41 skipped, 13 failed (X11 display setup and native-directory
fixtures). Focused CursorSettingsResolverTests: 9 passed. `git diff --check` and
`bash -n scripts/linux/publish.sh` passed. Desktop build/run and published binary
verification were not performed; no measured latency improvement is claimed.
