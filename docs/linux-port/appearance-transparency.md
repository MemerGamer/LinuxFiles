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
