# Appearance: background image, window opacity, backdrop

## App background image
Settings > Appearance > Background image works on Linux. Two things used to hide it:
- `MainPageViewModel` built the image with `new BitmapImage(Uri)` from the stored plain path (`/home/me/pic.png`). Uno does not load a bare file path that way, so the image never decoded. The file is now read and decoded from a stream (as the app icon loader does), and reloaded when the setting changes.
- The light theme file area was an opaque `#FAFAFA` in `LinuxDesktopResources.xaml`, covering the image. It is now `#C0FCFCFC`, the Windows value. Over the solid `#F3F3F3` window fill it looks the same as before when no image is set.

Opacity, fit and alignment settings apply as before. Per-folder `desktop.ini` backgrounds are Windows-only.

## Window opacity
Settings > Appearance > Window opacity (20% to 100%) sets `_NET_WM_WINDOW_OPACITY` on the top-level X11 window (`X11WindowChrome.SetWindowOpacity`); 100% removes the property.
- It fades the whole window, text and icons included. It is not a translucent background.
- Support is reported honestly (`X11AppearanceSupport`): the slider is enabled only on a compositor known to honour the property (KWin, Mutter, Xfwm, picom, compiz). Under xwayland-satellite (niri) the property is not forwarded, so the slider is disabled with an explanation; with no compositor or an unrecognised one the slider is disabled and says so ("unknown"). A successful property write is never treated as proof.
- High contrast forces opaque surfaces.

## Backdrop: Solid / Transparent / Blur
Settings > Appearance > Backdrop (`BackdropMode`) with Background opacity (`BackgroundOpacity`), separate from window opacity.
- Uno 6.7.135's Skia X11 host does request a 32-bit TrueColor visual (24-bit fallback) and a premultiplied BGRA surface. `Uno.UI.Xaml.WindowHelper.SetBackground(window, Transparent)` clears to transparent, so per-pixel alpha is possible without an Uno patch. This supersedes the earlier claim that no alpha visual was available.
- Transparent: the window clear colour is transparent and `App.Theme.BackgroundBrush` carries the single alpha plane; the other region fills become transparent so they do not compound.
- Blur: on KWin (X11) the empty `_KDE_NET_WM_BLUR_BEHIND_REGION` hint is set; the hint blurs behind the alpha pixels only. If unsupported (other compositors, xwayland-satellite, no compositor) the mode falls back to Solid and the UI says why. niri 26.04+ blur needs compositor window rules or `ext-background-effect`, not reachable from this X11 host.
- Requires an alpha-capable compositor; only recognised compositors are enabled, others fall back to Solid.

## Colour source
`ColourSource` = Files | Adwaita | System (migrated from the old `UseAdwaitaTheme`; existing choices are preserved). System reads the XDG portal (`color-scheme`, `accent-color`, `contrast`) and a bounded GTK named-colour reader (`ISystemAppearanceService`), and maps roles onto the app resources. Manual custom colours still win.

## Not offered
- **Native Wayland / org_kde_kwin_blur.** The Skia host is X11 only.
- **Backdrop material (Mica/Acrylic).** Not implemented on Linux; the Backdrop material card is hidden.

## Display scale on niri

On niri with xwayland-satellite, Files can render at 1x when XWayland publishes no DPI, leaving text and icons soft after compositor scaling. Enable **Detect display scale from the compositor (experimental)** in Settings > Appearance to detect the output scale at startup as a last resort. The boolean `DetectCompositorDisplayScale` defaults to false and is read directly from the XDG user settings file before Uno/window creation, without DI. Missing, non-regular or malformed settings leave detection off. The saved preference is read only when niri detection is eligible and no environment override is set. **Restart required; some setups may double-scale.** Existing scale hints retain precedence: `UNO_DISPLAY_SCALE_OVERRIDE`, any `Xft.dpi` resource, `GDK_SCALE`/`GDK_DPI_SCALE`, or `QT_SCALE_FACTOR`/`QT_SCREEN_SCALE_FACTORS` take precedence, including an explicit 1x or malformed hint. Both `NIRI_SOCKET` and `WAYLAND_DISPLAY` must be set.

The fallback queries the Unix socket named by `NIRI_SOCKET` for `Outputs` and `FocusedOutput`. The [niri IPC protocol](https://github.com/YaLTeR/niri/blob/main/niri-ipc/src/lib.rs) encodes these unit requests as JSON strings (`"Outputs"` and `"FocusedOutput"`), each terminated by a newline. It reads `logical.scale` from the focused output, or the first output if focus is unavailable. If socket detection fails, it resolves `niri` through the existing absolute-directory PATH lookup and runs `niri msg --json outputs` (and `focused-output`) without a shell. Socket detection and the CLI fallback each have a 500 ms budget, with up to another 500 ms to reap a terminated CLI process; replies are capped at 256 KiB. Errors, timeouts and invalid output scales leave the existing detection unchanged. Valid scales are clamped to 1–4, and the selected IPC/CLI source is logged once to stderr as `[display-scale]`.

After the display/toolkit hints, `FILES_NIRI_SCALE=1` forces this fallback on and `FILES_NIRI_SCALE=0` forces it off, overriding the saved setting. Unset or other values use the setting. Neither value overrides display/toolkit hints. To choose a scale manually, launch with e.g. `UNO_DISPLAY_SCALE_OVERRIDE=1.25 files` (use your output's scale). Detection runs once before Uno starts; moving the window between outputs does not update the scale. A sandbox that cannot access the niri socket or executable may still need the manual override.
