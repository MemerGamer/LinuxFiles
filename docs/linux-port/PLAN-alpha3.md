# Plan: 0.1.0-alpha3 and the road to 0.2

Source: seven read-only investigations (Codex, 2026-10-09), kept in [investigations/](investigations/). Findings marked
"unverified" need a headless run on Xvfb (`scripts/linux/headless-run.sh`, never the real display).

## Reported problems and root causes

| Report | Cause found | Fix | Size |
|---|---|---|---|
| Window opacity does nothing (niri) | The slider sets `_NET_WM_WINDOW_OPACITY` on the X11 window; xwayland-satellite ignores it. Uno 6.7.135 already requests a 32-bit visual, so the old "no ARGB" note is wrong. | Report support honestly. Try `WindowHelper.SetBackground(Transparent)` plus a separate background-opacity setting; add `BackdropMode=Solid/Transparent/Blur` (KWin X11 blur property; others unsupported). | S then M |
| Use GTK theme colours | System colours means light/dark only. | `ColourSource=Files/Adwaita/System`: portal `color-scheme`/`accent-color`/`contrast` plus GTK theme CSS named colours mapped to our resource keys. | M (L for full GTK fidelity) |
| Double-click zip opens Nautilus | Activation goes to the default launcher; only typed paths and "Open archive as folder" navigate into archives (`NavigationHelpers.Linux.cs`). | Navigate into supported archives on activation; keep Open With and executable gates. | M |
| Does zip extraction work? | Implemented and unit-tested (SharpCompress, hardened). Progress is only reported per entry, so a big entry looks stuck. Real-run check still needed. | Byte-level throttled progress; open destination only on success. | S |
| Different mouse cursor | Uno maps Arrow to the core `XC_arrow`; we set `left_ptr` for chrome; no XCURSOR theme/size bridge (unverified). | Cursor settings resolver (env, X resources, gsettings) before `UseX11`; fix Arrow mapping (L if it needs an Uno patch). | M |
| Status center icon broken | Icon is a vector `ThemedIcon`, glyph coverage is fine; suspects are `Margin="-16"` and competing visibility states (unverified). | Reproduce headless, then fix layout and state bindings. | S-M |
| No root actions in Nix | Removed on purpose: helper trust rules reject `/nix/store` and only look in `/usr/bin`. | `nixosModules.default` (polkit action, helper, `/run/wrappers/bin`) with a narrow store exception in `ElevationTrust`. Flatpak and AppImage: separately installed host helper (opt-in). AUR already works. | L |
| Debian 13 KDE: about 2 s per action | Cannot be proven without numbers. Candidates: Open With and KDE service menu scans on every right click, whole-database tag writes on reads, watcher poll fallback (2 s interval), software rendering. | Caches, async loading, no-op tag writes, immediate reconcile after our own operations; add `FILES_TRACE=1` timing output; ask the reporter for numbers. | M |
| Pixelated text and icons | Two causes: XWayland upscaling at fractional scale, and Uno font hinting at 1x. Neither reproduced yet. | Spike: compare 1x/1.5x/2x screenshots to separate them; Slight hinting patch; icon cache scale keys. | S then M |

## Proposals

- **Remove unused Windows code: soft only.** Hard deletion makes the daily upstream sync hit modify/delete conflicts and
  kills the path back to Windows. Do the safe part: compile-item exclusions, drop `System.Drawing.Common`, gate SevenZip
  and registry generators on Windows, add a trim/AOT analysis profile. Expected gain about 7-9 MiB unpacked and little startup.
- **Rust: no-go for now.** First take cheap C# wins (batched `getdents64`, pooling, parallel stat) and benchmark 10k/100k
  folders. Spike Rust only for read-only directory scanning, and only if it beats optimised C# by 2x on the phase and 20%
  end to end. Keep it out of the privileged helper and the shared view models.
- **Other UI frameworks: stay on Uno.** The UI is 141 XAML files (31k lines, 1.6k `x:Bind`), so any other framework is an
  XL rewrite (Avalonia 24-48 weeks, GTK4 32-64, Qt 32-96). Uno 6.7.135 has no Wayland host and its host interfaces are
  internal. Decide after the pixelation spike: a native Wayland host for Uno (2-4 weeks for a spike, 12-24+ for parity)
  versus a 1-2 week Avalonia spike.

## Work packages (Codex worktree agents, one branch each)

Wave 1, independent, all into 0.1.0-alpha3:

1. `zip-nav`: archive activation navigates, extraction progress, success-gated open.
2. `cursor-statuscenter`: cursor resolver and status center icon (needs a headless repro first).
3. `perf-actions`: Open With and service menu caches, tag writes, watcher fallback, `FILES_TRACE`.
4. `transparency-colours`: honest support reporting, background transparency, blur token, System colours.
5. `windows-soft-trim`: exclusions, unused packages, trim analysis profile.
6. `nix-root-module`: `nixosModules.default`, trust changes, docs for Flatpak/AppImage helper install.

Wave 2: pixelation spike (1x/1.5x/2x, hinting), Rust-vs-C# directory scan benchmark, Wayland host spike decision.

Overlaps to sequence: 2 and 4 both touch the X11 window and appearance code (merge 4 first); 3 and 1 touch navigation
helpers.

## Process (token-frugal)

- Codex writes and reviews; a different Codex session reviews each branch (read-only). Claude only runs what Codex's sandbox
  cannot: one headless verification pass per wave on the merged branch, git/gh, and the release.
- Every PR gets a review of its latest commit before merge; nothing merges with a failing build.
- Release 0.1.0-alpha3 after Wave 1 passes a headless smoke test; publishing the GitHub release is a manual step for the user.
