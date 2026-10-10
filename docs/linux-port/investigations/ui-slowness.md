# UI slowness investigation (software rendering, headless)

Question: why does the UI feel slow (~2 s per action reported on Debian 13 KDE), and is it fixable? Everything below was
measured in private Xvfb runs (`scripts/linux/headless-run.sh`, software rendering, `nice -n 19`, Debug build, JIT) on a
16-core desktop CPU with low load. A laptop CPU will be roughly 2-3x slower, which matches the reports. Real KDE
compositor/GPU behaviour is not covered.

## Method

- Wall time: `ffmpeg x11grab` at 20 fps on the private display, hashing each frame. For every action (xdotool input,
  timestamped by a marker script) the table gives `first` (input to first visual change) and `settle` (input to the last
  change before 700 ms of stillness). Resolution is 50 ms.
- UI-thread stalls: `FILES_TRACE=1` now also starts a 10 ms dispatcher timer that logs `[files-ui] stall-ms=N` whenever a
  tick arrives more than 50 ms late (`UiResponsivenessProbe`). `max stall` is the longest single block of the UI thread.
- Where the time goes: `dotnet-trace collect --profile dotnet-sampled-thread-time --format Speedscope` around one action,
  then inclusive/exclusive time of the UI thread (`Uno_Event_Loop`) from the Speedscope JSON.
- Fixtures: folders with 1,000 and 10,000 empty files of ten extensions, plus a 5-file folder.
- Idle: per-thread CPU ticks over 30 s with no input.

## Results (ms; before -> after the checkbox change; two runs each, they agreed within ~10%)

| Action | settle before | settle after | max UI stall before -> after | Top contributors (UI thread) |
| --- | --- | --- | --- | --- |
| Startup to Home painted (Debug/JIT) | ~4700 | n/a | n/a | first paint 2.0 s; JIT, XAML load, Home page |
| Navigate into 1k folder | 1370 | 1020-1120 | 384 -> 223 | item realization/measure (~45%), Skia picture record on UI thread (~30%), StatusBar forced `UpdateLayout` (~15%) |
| Navigate into 10k folder | 675 | 540-585 | 295 -> 239 | same (virtualized, so ~1k cost) |
| Layout details -> grid (1k) | 762 | 460-560 | 273 -> 118 | realizing the new panel's items |
| Layout grid -> list (1k) | 1109 | 1108-1206 | 1142 -> 585 | item realization: 2.4 s CPU -> 1.1 s CPU in the profile |
| Layout list -> cards (1k) | 1116 | 815-864 | 531 -> 370 | item realization |
| Layout cards -> details (1k) | 524 | 273 | 129 -> 153 | item realization |
| Select all, 1k | 1481 | 378-429 | 147 -> 216 | selection events, checkbox state |
| Select all, 10k | 1771 | 1736-1782 | 796 -> 802 | Uno `SelectedItems.Add` per item: `ContainerFromItem` + locked `BulkConcurrentObservableCollection.Count` (O(n^2)) |
| Sort by column click (1k / 10k) | 190-250 | same | 260 | sort + relayout; 10k sort profiles at ~20 ms CPU |
| Context menu: file / folder / empty | 130-190 / 430 / 70 | same | 70-170 | `context-menu-build` 120-230 ms on the UI thread (trace), flyout open ~100 ms |
| Open Settings | 360 | 330 | 230 | window/page creation |
| New tab / close tab | 410-610 / 870-1070 | 460-860 / 740 | 70-170 / 290 | page construction, item realization of the tab shown again |
| Window resize | 90 / 76 | same | up to 130 | relayout |
| Search box typing (8 chars) | n/a | n/a | no stalls | typing path is cheap; results come from a worker |
| Idle (30 s, no input) | 0 ticks on every thread | | | no repaint loop |

Visual changes at idle: 0 in 5 s. Idle CPU is zero because nothing requests a frame. Caveat found while measuring: simply
subscribing to `CompositionTarget.Rendering` makes Uno render at 60 fps (about 8% CPU idle), so do not add such a probe.

## Findings

1. **Rendering backend.** `RenderingBackendSelector` picks Uno's software renderer when no GPU node exists or
   `LIBGL_ALWAYS_SOFTWARE`/llvmpipe is set; headless runs report `Software (LIBGL_ALWAYS_SOFTWARE is set)`. Raster
   happens on `X11RenderThread`; it was idle in all profiles. Frame invalidation does not cause continuous full-window
   repaints: idle is 0 frames and 0 CPU.
2. **The UI thread, not rasterization, is the bottleneck.** Skia raster runs off-thread. What blocks the UI thread is
   (a) creating and measuring item containers (Uno `VirtualizingPanelLayout.FillLayout`, templates, bindings) and (b) Uno's
   `RecordPictureAndReturnPath` (picture recording and `SKPath.Op` clip math), which runs on the UI thread. Both are CPU
   bound and independent of GPU, so software rendering is not the main cause; a GPU would not remove (a) or (b).
3. **Per-item `CheckBox` cost (fixed here).** Every file item has a selection CheckBox. Uno's default CheckBox template
   builds an `AnimatedIcon` with a Lottie visual and expression animations per instance, even though the box is invisible
   (`Opacity=0`) until hover/selection. In the profile this was about a third of the time to realize a list/grid/details
   panel (AnimatedIcon construct + arrange + animation re-evaluation + the GC pressure they cause). Fixed with a plain
   border + path template (`Files.SelectionCheckBoxStyle`).
4. **Icons/thumbnails** do not block the UI: icon resolution and file enumeration run on worker threads
   (`folder-enumeration-batch` 0.6-1.5 ms per batch of 25 on the worker). `folder-enumeration` for 10k files was 175-400 ms
   wall, off the UI thread.
5. **No sync-over-async stalls were seen** in the traced actions. The only blocked-UI pattern was lock contention
   overhead in `BulkConcurrentObservableCollection.Count` during select-all of 10k items.

## Remaining larger ideas (not done)

- Select-all of 10k items is O(n^2) inside Uno's `ListViewBase` (per-item `SelectedItems.Add`). Needs either a bulk
  selection path (suppress per-item container lookups; set selection ranges) or a lock-free `Count`/`IndexOf` snapshot.
- Item templates are still heavy (bindings, `x:Bind` updates, `Files.ListViewItemTemplate` build ~200 ms per screenful).
  Candidates: fewer elements per row, `x:Phase`/deferred columns, `x:Load` for rarely visible parts (shield icon, tag
  badges, rename popup), reuse via a smaller recycling pool.
- `StatusBar.QueueRemeasure` calls the global `UpdateLayout()` from a property-changed handler (about 165-230 ms once per
  navigation, because it forces the item layout early). Measuring only the bar's own panels would avoid the forced
  global pass.
- Uno picture recording (`RecordPictureAndReturnPath`, `SKPath.Op`) takes 250-400 ms of UI time per navigation. Fixing it
  is upstream (dirty-region/clip handling) or by reducing the number of clipped/animated elements.
- Startup: ~4.7 s to a painted Home page in a Debug JIT build; ReadyToRun/AOT builds and trimming the work done before
  the first frame (service construction, settings load) should be measured separately on a release build.
- Context menu model building now runs on a worker (`context-menu-snapshot` ~2-14 ms and `context-menu-flyout` ~1-15 ms on the UI
  thread, `context-menu-model` off-thread). Before, a file/folder menu blocked the UI thread for 150-175 ms (headless, same fixture).
  A newer menu request cancels the previous build.
- Select-all of 10k items is still O(n^2) inside Uno (`SelectedItems.Add`); the app-side workaround only avoids the per-item
  `ContainerFromItem` theme pass (stall 817 -> 604-630 ms headless). A real fix needs a bulk selection API in Uno.

## Reproducing

```bash
FILES_TRACE=1 FILES_SANDBOX_SEED=<seed script> FILES_SANDBOX_DRIVES=scripts/linux/showcase-drives.txt \
  nice -n 19 scripts/linux/headless-run.sh -s 20 -o /tmp/out -a <actions>
```

where the actions file records the screen (`run <ffmpeg x11grab ... -f framemd5>`), sends xdotool input and writes
timestamp markers; `[files-ui]` lines in `app.log` give stalls. Only the private Xvfb display is used.
