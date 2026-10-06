# Grouped Grid/List/Cards virtualization

`GroupedVirtualizingWrapGrid` uses Uno 6.7's existing container generator with a row map of the flattened source. Unrealized headers use cached measurements from loaded headers of the same kind and size, or the last loaded header measurement until that cache is populated; their geometry remains an estimate until realization. Headers span the panel breadth; partial tile rows consume a whole row. List transposes the same geometry into columns. Only intersecting rows are realized, including boundary rows; a resize rebuilds around the viewport and restores its anchor by item identity after the new extent arrives. Scroll requests made while the map is stale wait for the next prepare pass and resolve the item again, with the list control as a fallback. Arrow navigation uses the row map and skips headers without requiring their containers.

The existing Uno version guard covers both wrap panels and retains the non-virtualizing fallback. Downloads defaults to grouping by date created only when the virtualizing panel is supported. Windows retains its existing panels and behavior.

## Headless measurement (2026-10-06)

The fixture contains 10,000 files, evenly split between `bin`, `csv`, `md`, and `txt`, grouped by type with small Grid tiles. The clock starts at Return in the address bar, from an empty folder. The endpoint is a screen capture containing the correctly positioned first filenames (`file-000…`), rather than the transient overlapping last filenames produced during the old panel's initial load. Capture timestamps exclude OCR processing time. The app runs at nice 19 with .NET SDK 10.0.112 / Uno.UI 6.7.135, in a 1280×800 window on private 1600×1000 Xvfb with sandbox HOME and synthetic drives; no real display is used.

| Build | Time to first correctly positioned tiles | Realized entries |
| --- | ---: | ---: |
| Before | 90.51 s | All 10,004 (10,000 files + 4 headers) |
| After | 1.68 s | 45 at the top; 47 after End |

This is about 54× faster in this environment and meets the requested <2 s Grid threshold. These are individual runs, not a machine-independent guarantee. The panel extent for this fixture is 138,840 px; narrowing the window changes it to 217,880 px while realization remains bounded to the viewport.

## Reproduce

Build using the command in `AGENT-RULES.md`, then run:

```bash
python3 scripts/linux/benchmark-grouped.py --output grouped-results/after
python3 scripts/linux/benchmark-grouped.py --output grouped-results/before --bin /path/to/baseline/bin
```

Pillow, tesseract, and its English language data are required. Use `--tessdata DIR` if the data is not in tesseract's default location. `--layout list` and `--layout cards` use the same 10k fixture. The driver delegates all app/input/capture work to `headless-run.sh`, bounds the run to 180 seconds, and writes timing JSON, the first tile capture, full screenshots, and `app.log`. `FILES_GROUPED_LAYOUT_TRACE=1` enables item counts, offsets, and extents in the app log.

## Verification

The headless observations below are from the original PR measurement; the review fixes add regression coverage for stale geometry and identity anchors.

- Desktop app and virtualization project builds succeeded.
- Platform test build with `-warnaserror` succeeded; eight geometry, navigation, and identity-anchor tests passed.
- Headless Grid checks cover End, Up, deep resize in both directions, watcher insertion, Home, and selecting all without headers.
- 10k List: 1.95 s to visible first filenames, 65 entries realized initially. Cards: 1.16 s, 19 entries realized. Both passed End, directional navigation, and deep resize.
- Small-group headless captures verify Right and Down cross headers without selecting them.
- Headless checks verify Downloads' default date grouping. After watcher insertion, Select All reports 10,001 files selected, excluding the four headers.
