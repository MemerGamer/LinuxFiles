# Select-all with 10,000 files

## Findings (static inspection, 2026-10-12)

No desktop runtime profiling was possible in the sandbox. Complexity statements below describe source paths, not measured speedups. Uno SDK is pinned to 6.7.30; the reported Uno quadratic selection cost remains a runtime hypothesis to measure separately.

- `ItemManipulationModel.SelectAllItems()` raises `SelectAllItemsInvoked`; Details header checkbox and selection commands reach `BaseGroupableLayoutPage.ItemManipulationModel_SelectAllItemsInvoked`.
- The shared path already snapshots existing selection into a reference-equality hash set, flattens grouped file items through `GetAllItems`, and adds only missing items to `ListViewBase.SelectedItems`. Group headers are excluded. It uses individual `Add` calls rather than `SelectAll`; the existing code notes that Uno does not implement SelectAll. Each Add can still incur Uno internal selection/list scans and container updates. App suppression does not batch the Uno collection itself.
- `isSelectingAll`, restored in `finally`, already makes each intermediate `FileList_SelectionChanged` return before enumerating selection, applying themes, publishing page selection, or calling layout handlers. One aggregate event follows. There is no app-side status/context/preview rebuild per Add on this path.
- The final shared refresh already themes realized panel children instead of calling `ContainerFromItem` for every added item. However, Details and Grid `OnSelectionChanged` still called `SetCheckboxSelectionState` for every added file. That helper called `ContainerFromItem`, whose item-source scan is documented in nearby code, then `SelectedItems.Contains`. Across n additions these can produce quadratic work even after event suppression. GridLayoutPage also implements List and Cards modes, so List shares this problem.
- `BaseLayoutPage.SelectedItems` invalidates the context-menu build, updates selection size/count (status bar), emits property change, and publishes toolbar selection once after suppression. Its getter can sort and rotate a selection; this is final-refresh work, not repeated for each Add. `NavigationToolbarViewModel.SelectedItems` updates action availability and overflow; `ContentPageContext.UpdateSelectedItems` publishes the final context. `InfoPaneViewModel` listens for the context SelectedItem change and delays preview refresh by 100 ms. Async preview work and rendering are outside the synchronous trace.

## Change

For multi-item events, Details and Grid/List now build one reference-equality selection hash set and walk realized panel children, passing each existing container and its selection state directly to the checkbox helper. No panel means no realized checkbox work; later container preparation retains its existing selection synchronization. Single-item events use their known added/removed state instead of scanning SelectedItems. Checkbox event detachment remains intact, preventing selection feedback. Details still updates its header checkbox once, with header event suppression.

The checkbox refresh is O(s + r), where s is final selection count and r is realized container count, plus visual checkbox work. It avoids n item-source lookups. It does not promise linear total select-all: Uno still receives individual collection mutations, and nonvirtualized layouts may realize every item. Existing shared suppression, grouping, selection ordering, and context publication are preserved.

## Timing

Run with `FILES_TRACE=1`. Existing `select-all` encloses two new child scopes:

- `select-all.control-mutation`: individual Uno SelectedItems additions, including suppressed event dispatch and synchronous control work.
- `select-all.page-refresh`: final aggregate event, selection publication, theme/checkbox updates, and synchronous subscribers.

The existing bounded background trace writer reports milliseconds, parent IDs and UI-thread state without paths or filenames. Disabled tracing uses the existing no-op scope. Fully selected repeat invocations have only the outer scope. Compare before/after on the same machine with 1k, 5k and 10k items, keeping grouping, viewport, layout and preview settings fixed; collect several warm runs and report medians. A dominant quadratic mutation phase identifies remaining Uno work; a reduced refresh phase validates this app-side fix. No timings have been collected here.

## Headless verification and screenshots

On a machine that can run Uno desktop:

1. Build `src/Files.App` for `net10.0-desktop` using `-m:1 /nodeReuse:false`. Create a temporary seed script for `FILES_SANDBOX_SEED` that receives the throwaway home as its first argument and creates 10,000 synthetic files in its Documents directory (repeat with 1k/5k). Never seed the real home.
2. Run `FILES_TRACE=1 FILES_SANDBOX_SEED=/tmp/select-all-seed.sh scripts/linux/headless-run.sh -s 90 -o /tmp/select-all-details -a /tmp/select-all-actions.txt`. Use a fresh output directory for each layout. Keep the default throwaway home and private Xvfb display; do not set FILES_REAL_HOME.
3. In the actions file, navigate to the seeded Documents folder using private-display xdotool input, wait until the item count reaches 10,000, choose Details/Grid/List using the layout menu, focus the file area, then `shot before`, `key ctrl+a`, wait for completion, and `shot selected`. Inspect count, checkbox state and row/icon highlighting. In Details also test the header checkbox. Enable the info pane for a repeat and confirm final multi-selection state.
4. Scroll to the end and `shot selected-end` to check newly realized checkbox state; clear selection and `shot cleared`. Repeat from a partial selection, repeat Ctrl+A when all selected, and test grouped folders (headers must remain unselected). Check individual select/deselect and rectangle selection after the bulk operation. Record trace lines from the output app log and inspect every screenshot before sharing.
5. Repeat identical steps before/after the patch; screenshots establish correctness, trace phases establish synchronous timing. The runner cleans up its app/display processes. Keep screenshots, logs and throwaway homes outside Git.

## Checks in this sandbox

- `dotnet build tests/Files.Platform.Tests -m:1 /nodeReuse:false`: passed, no warnings/errors.
- `dotnet test --project tests/Files.Platform.Tests/Files.Platform.Tests.csproj --no-build -m:1 /nodeReuse:false`: blocked by named-pipe socket permission denial.
- Direct execution of the built test DLL: 1,208 passed, 13 failed, 41 skipped (1,262 total). Output included Xvfb socket permission failures and a trusted native-directory failure; these checks do not cover the modified Uno layout code.
- Desktop build/run, screenshots and performance measurements were not performed. Reference searches and diff/line-ending checks supplement inspection, but do not replace desktop compilation and headless verification.
