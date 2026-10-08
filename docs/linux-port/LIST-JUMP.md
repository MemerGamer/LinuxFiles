# Details/Grid list jumps and the Details header

## Cause and change

On Uno desktop, `TryHandleListJumpKey` implements Home, End, Page Up and
Page Down. Previously every queued key performed selection, ScrollIntoView
and focus work synchronously. The Details focus handler also scrolls again.
A held key can therefore drain a backlog of expensive layout work before
Uno paints.

The first jump still runs immediately. A 16 ms dispatcher timer coalesces
subsequent jumps: Page Up/Down accumulate from the pending index using the
last realized page size, while Home/End replace the pending target. Only
that target is selected, scrolled and focused at the next tick. Repeating
an unchanged endpoint does no additional layout work. Unloading, disposal,
a different key, or a changed selection invalidates pending navigation;
the callback also checks that the target still occupies its index.
This implementation is excluded from Windows.

The column header was the ListView.Header, inside its ScrollViewer. The
attached StickyHeaderBehavior needs
`ElementCompositionPreview.GetScrollViewerManipulationPropertySet`, an
unsupported Uno API already recorded in PLAN.md section 0. The Linux
Details page now removes that behavior and reparents the existing HeaderGrid
into an Auto-height row above the list. It remains outside the virtualized,
vertically scrolling content; a separate horizontal scroller follows the
list's horizontal offset. Existing column bindings, sorting, resizing,
theme updates and key handling are retained. The selection canvas stays in
the list row. Windows keeps its original header and composition behavior.

## Verification (2026-10-08)

Artifacts are in `/tmp/cx-listjump-shots`; screenshots are not committed.

- Baseline and changed desktop app builds succeeded with zero errors.
  The final build has existing warnings (see `after-build.log`).
- The prescribed build command cannot start MSBuild IPC nodes in this
  restricted environment. Successful builds additionally use
  `-p:BuildInParallel=false -p:UseSharedCompilation=false` and temporary
  in-process Uno task overrides via
  `-p:CustomAfterMicrosoftCommonTargets=/tmp/cx-listjump-shots/inprocess.targets`.
  These overrides only replace Uno TaskHostFactory registrations for the
  build; no package or project build configuration was edited.
- The platform test project builds with `-warnaserror`: zero warnings/errors
  using `BuildInParallel=false` and `UseSharedCompilation=false`.
- The prescribed `dotnet test --project ... --no-build` aborts because its
  named-pipe server cannot bind a socket (`tests.log`). Running the compiled
  test executable directly yields 1,067 passed, 41 skipped and 9 failed
  (`tests-direct.log`). Failures are outside the edited UI: three thumbnail
  sandbox/fallback tests, three single-instance socket tests, and three
  trusted native-library-directory tests. This is not a passing full suite.
- Before and after headless attempts used `scripts/linux/headless-run.sh`,
  `FILES_SANDBOX_DRIVES=scripts/linux/showcase-drives.txt`, and `seed.py`,
  which creates exactly 5,000 files in the sandbox's Videos/Large folder.
  Both failed before launch: Xvfb cannot create listening sockets and the
  private D-Bus daemon cannot bind its socket. Their logs are in `before/`
  and `after/`. No real display was used; the runner's cleanup completed.
- No screenshots or repaint timings could be obtained. Header placement,
  horizontal alignment, sorting/resizing and sustained keyboard repainting
  still require a successful headless run. Build success is not visual or
  performance verification.

## Pending headless checks

In an environment allowing private local sockets, run the same before/after
fixture through headless-run.sh only. Select Details with Ctrl+Shift+1 and
Grid with Ctrl+Shift+4. Capture the initial view, one Page Down, sustained
Page Down/Page Up, Home and End. Check that a single press has the same
selection/focus behavior, that alternating jump keys end at the latest
target, and that navigation/layout switching or mouse selection cannot
apply an old pending target. Repeat with grouping enabled.

For Details, verify that the header stays visible after wheel and keyboard
scrolling, including End, and that column sorting/resizing, horizontal
scrolling and rectangle selection still align with the rows. Record frames
on the private display during repeat input and measure the longest interval
without a changed list frame while the selection is advancing, plus the
delay from the last sent key to the final target. Use identical input and
capture rates for both versions, excluding stationary Home/End endpoints.
Keep all frames and timing results under `/tmp/cx-listjump-shots`.
