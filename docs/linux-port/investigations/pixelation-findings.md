# Pixelated / soft text and icons on Wayland (niri + xwayland-satellite, fractional scale)

Status: original headless diagnosis below; the missing-scale niri case is now addressed by the [startup IPC fallback](../appearance-transparency.md#display-scale-on-niri). Follows `ui-frameworks-wayland.md`.

## Method
Headless only (`scripts/linux/headless-run.sh`, private Xvfb, sandbox HOME, `FILES_SANDBOX_DRIVES=scripts/linux/showcase-drives.txt`, 1600x1000), debug build of main (5 Oct). The scale is driven by `FILES_HEADLESS_XFT_DPI` (writes `Xft.dpi` to the private display): 96 = 1x, 144 = 1.5x, 192 = 2x. Uno reads `Xft.dpi` itself; `UNO_DISPLAY_SCALE_OVERRIDE=<factor>` (e.g. `1.5`, not a DPI) also works, `DisplayScaleResolver` falls back to `QT_SCALE_FACTOR`/`GDK_SCALE`. At 1.5x/2x the window is the same 1280x800 physical size, so fewer logical pixels fit (at 2x the sidebar collapses). Pixel crops were taken with ImageMagick (nearest-neighbour zoom).

## Findings (Xvfb, i.e. the app's own rasterisation)
- **Icons are rasterised at device scale, not upscaled.** `FileThumbnailHelper.GetIconAsync` multiplies the requested size by `AppWindowDPI` (= `XamlRoot.RasterizationScale`) and `LinuxIconHelper` renders theme SVGs at that pixel size; `LinuxIconThemeProvider.FindInTheme` prefers an exact-size raster, then scalable artwork, before resampling. Crops at 1.5x (`images/pixelation-icons-1.5x.png`: sidebar 24 px icon left, Quick access tile icon right) show crisp edges with no bilinear halo; at 2x the tile icons are sharp. Glyph icons (toolbar, tab bar, chevrons) are font vectors and stay sharp at every scale.
- **Text is grayscale-AA, subpixel-positioned, hinted by Skia's default.** Decompiling Uno 6.7.135 `FontDetails.CreateSKFont` shows only `Edging = SubpixelAntialias; Subpixel = true`; `SKFont.Hinting` stays at Skia's default (Normal) and nothing is exposed to change it. Grayscale AA is expected (no LCD geometry in Uno). At 1x the stems are vertically snapped and spacing is slightly uneven (see `third-party-fonts.md`); at 1.5x and 2x text is clean.
- **Fontconfig cannot override it.** A sandbox `fonts.conf` forcing `hintstyle` to hintnone/hintslight/hintfull produced pixel-identical screenshots (`compare -metric AE` = 0; `images/pixelation-hintstyle-1x.png` rows are the same). So the only way to change hinting is an Uno patch/upstream change, or reflection into the internal `FontDetails` memoizer (rejected by the AOT/reflection rules).
- **DPI reporting is correct when `Xft.dpi` is an integer** (`DisplayScaleResolver` already converts fractional `Xft.dpi` and the Qt/GDK variables to an override). Icon caches (`IconCacheService`, `LinuxIconThemeProvider`) key on logical size, not scale; only a live scale change (moving between monitors) would serve stale-resolution bitmaps. Not observed, not fixed.

## Cause breakdown for the reported blur
1. **XWayland upscaling (dominant in the original report).** If the compositor/xwayland-satellite does not publish a fractional scale (no `Xft.dpi`, no `GDK_SCALE`), Uno renders at 1.0 and the compositor stretches the whole buffer by 1.5. Everything (text and icons) is then uniformly soft regardless of how well the app rasterises. The reporter subsequently confirmed that `UNO_DISPLAY_SCALE_OVERRIDE=1.25` renders crisply at the correct size on their niri + xwayland-satellite setup. The experimental opt-in startup IPC fallback supplies that missing scale when enabled in Settings > Appearance; native Wayland (fractional-scale-v1 + viewporter, see `ui-frameworks-wayland.md`) remains a separate host improvement. This could not be reproduced here: Xvfb has no compositor resampling.
2. **Hinting at 1x (small, upstream).** `Normal` hinting on Selawik gives the blocky look at 100%. Needs Uno to expose `SKFont.Hinting` (Slight/None); no supported switch.
3. **Icons/DPI plumbing (no defect found).**

## Manual scale override
Make the X11 side advertise the real scale so Uno renders at device resolution: launch with `UNO_DISPLAY_SCALE_OVERRIDE=1.5` (match the output scale) or export `Xft.dpi: 144` into the XWayland session. Whether xwayland-satellite then also rescales the surface (double scaling) depends on its version and was not verifiable without the real display; check on the real session.

## Not done
The original investigation made no code changes. The niri missing-scale fallback is now implemented; visual confirmation on the reporter's compositor and live scale changes between monitors remain unverified. Candidate follow-ups: ask/patch Uno for `SKFontHinting.Slight`; native Wayland host.
