# Invalid item dialog after session restore

## Status

Investigated by source inspection; the reported first-right-click failure was not reproduced. The fix is defensive. A loading/state race is plausible, but there is no proven right-click-to-dialog chain. Uno desktop build and UI execution were unavailable in this sandbox.

## Text and all producers

`Strings/en-US/Resources.resw` defines `InvalidItemDialogTitle` as “Invalid item” and `InvalidItemDialogContent` as “The item referenced is either invalid or inaccessible.{0}Error message:{0}{1}”. Other locales have the same resource keys. `{0}` is a newline and `{1}` is `FileSystemStatusCode.ToString()`, hence “Generic”. This is separate from the invalid-item-name rename resource.

Repository-wide searches for `Invalid item`, `InvalidItemDialog`, `ShowInvalidItem`, and `DynamicDialog` found these four original dialog call sites (now routed through `NavigationItemGuard.ShowInvalidItemAsync`):

| Producer | Conditions |
| --- | --- |
| `NavigationToolbarViewModel.HandleItemNavigationAsync` | Omnibar path submission fails folder lookup/access check, file lookup, command launch, and URI launch (false or URI/argument exception). Displays the **folder** lookup error, not the final launcher error. |
| `NavigationToolbarViewModel.CheckPathInputAsync`, URI-false branch | Legacy toolbar query submission follows the same lookup/command fallbacks, then URI launch returns false. |
| `NavigationToolbarViewModel.CheckPathInputAsync`, exception branch | Same legacy path, but URI parsing/launch throws `UriFormatException` or `ArgumentException`. |
| `OpenFileLocationAction.ExecuteAsync` | Selected shortcut has a nonblank target, but its containing-folder lookup fails with an error other than `NotFound`. A null containing directory explicitly creates a `Generic` result. `NotFound` uses a different dialog; blank targets return silently. |

`DialogDisplayHelper.ShowDialogAsync(title, message)` constructs a `DynamicDialog` / `DynamicDialogViewModel` and awaits `ShowAsync`. `DynamicDialogFactory` has no additional producer of this particular dialog. No pre-existing `ShowInvalidItem` method was found.

`FilesystemTasks.GetErrorCode` maps otherwise unclassified exceptions to `Generic`. In the Linux legacy storage bridge (`StorageFileExtensions.Linux.cs`), resolver statuses outside the explicitly supported success/not-found/unauthorized/type-mismatch cases throw `NotSupportedException`, which becomes `Generic`. The label alone does not identify a race or a missing file.

## Restore, incomplete items, and right-click

- `MainPageViewModel.RestoreSessionTabsAsync` deserializes saved `TabBarItemParameter` values, opens the previously selected tab first, adds other tabs without selecting them, and then restores tab ordering. Parameters may be a string, pane arguments, or null. This path calls `NavigationHelpers.AddNewTabByParamAsync`, not either toolbar path validator.
- Saved missing/inaccessible locations pass through shell enumeration and its location-unavailable UI (`ShowLocationInaccessibleOrMissing` / `ShowLocationUnavailable`), not this dialog. A later explicit toolbar submission of a bad saved location can reach the toolbar producers above.
- Toolbar validators already ignore empty/whitespace normalized input and an unchanged path. Empty path text therefore does **not** independently prove this dialog's origin. They perform several awaits; the modern validator re-reads `ContentPageContext.ShellPage` after lookups, so failure completion can outlive the original pane context.
- `ListedItem.ItemPath` and `ItemNameRaw` are nullable. Linux enumeration normally assigns both before publishing items; icon/metadata completion can legitimately happen later. `ItemPropertiesInitialized` is not a reliable test for identity readiness and is deliberately not used by the guard. The local filesystem root name already falls back to its path.
- `BaseLayoutPage.FileListItem_RightTapped` selects an item. Item flyout opening can also select the flyout target as a workaround, then uses the selection to build commands. Background flyout opening uses `CurrentFolder`. Neither handler directly emits “Invalid item”. Previously there was no identity/loading guard, and a missing `CurrentFolder` threw an exception.
- `EnsurePageIsCurrentAsync` waits up to 500 ms for pane activation and then another 10 ms; it does not wait for folder enumeration. `ShellViewModel.IsLoadingItems` is set around enumeration, and the focused restored tab is intentionally available while other tabs load. This gives a real window for interaction with loading/stale folder state.
- Omnibar `LostFocus` only updates focus state. Query submission occurs on Enter or suggestion click; ordinary right-click focus loss does not submit a path. “Open file location” requires command execution, not merely menu opening.

The most plausible race candidate is interaction during restored-pane activation/enumeration, with menu work or an earlier asynchronous navigation failure completing against changing context. The direct evidence supports that timing window, **not** a confirmed causal connection to the exact dialog. An already pending toolbar failure becoming visible on first interaction remains possible. Blank shortcut identity/target data and unsupported legacy lookup results are alternate candidates.

## Guarded change

`NavigationItemGuard` checks that the source shell exists, is the current instance/pane, and is not loading. Its item overload additionally requires a nonempty `ItemPath` and `ItemNameRaw`; it accepts whitespace names (legal Linux filenames) and does not wait for icons or metadata.

Item and background context menus stop before command construction when their inputs are loading/incomplete. A missing current folder is ignored rather than thrown. Open-file-location checks identity before lookup and retains the source item/shell across the await. All four invalid-item producers now check source-shell readiness immediately before displaying; the modern toolbar validator captures its original shell rather than using the later replacement context for this check. Valid, ready-pane failures retain their existing localized dialog/error code.

Existing `PerformanceTrace` supplies opt-in `FILES_TRACE=1` logging. `operation=navigation-item-guard` has outcomes `no-shell`, `inactive-pane`, `loading`, or `placeholder`; `operation=invalid-item-dialog` records the error code for failures that reach display. No paths, filenames, session data, or typed input are logged. Menus are not automatically reopened; retry after loading completes. This does not cancel existing lookups or establish a navigation-generation token, so a transition that finishes before the final check can still require investigation.

## Verification and follow-up

- Reference searches covered all resource producers and callers, session restoration, Omnibar submission/focus, Linux enumeration, and item/background context menus.
- `git diff --check` passed; existing modified-file BOM/newline conventions were preserved.
- Single-node platform build initially failed because sandbox networking blocked NuGet vulnerability feeds (`NU1900`). Retried with invocation-only `-p:NuGetAudit=false`; build passed with zero warnings/errors. No project/dependency configuration changed.
- The standard single-node `dotnet test --project ... --no-build` command could not start its test host: named-pipe/socket creation was denied by the sandbox. Direct test-assembly execution (`dotnet tests/Files.Platform.Tests/bin/Debug/net10.0/Files.Platform.Tests.dll --no-ansi --progress off --output Minimal`) ran 1,262 tests: 1,208 passed, 41 skipped, 13 failed. Failures included denied X11/DBus/socket binding, bubblewrap sandbox setup, and three trusted-native-directory assertions; the latter were not diagnosed in this UI-focused task. This is not a clean full-suite pass.
- The platform project does not compile the changed Uno UI files. Desktop compilation and runtime behavior remain unverified.

On a capable machine, use `FILES_TRACE=1 scripts/linux/headless-run.sh` with the script's private display/throwaway home. Restore multiple tabs and immediately right-click both background and an item; retry after enumeration, switch panes during pending navigation, submit a genuinely invalid path once ready, and execute Open file location for invalid shortcuts. Check for suppression records during loading and error-code records only for ready-pane failures. Include root, archive, trash, and a whitespace-only filename to check identity handling. Do not collect real session paths in trace logs.
