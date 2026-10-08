# Appearance: background image, window opacity, backdrop

## App background image
Settings > Appearance > Background image works on Linux. Two things used to hide it:
- `MainPageViewModel` built the image with `new BitmapImage(Uri)` from the stored plain path (`/home/me/pic.png`). Uno does not load a bare file path that way, so the image never decoded. The file is now read and decoded from a stream (as the app icon loader does), and reloaded when the setting changes.
- The light theme file area was an opaque `#FAFAFA` in `LinuxDesktopResources.xaml`, covering the image. It is now `#C0FCFCFC`, the Windows value. Over the solid `#F3F3F3` window fill it looks the same as before when no image is set.

Opacity, fit and alignment settings apply as before. Per-folder `desktop.ini` backgrounds are Windows-only.

## Window opacity (new)
Settings > Appearance > Window opacity (20% to 100%) sets the `_NET_WM_WINDOW_OPACITY` property on the top-level X11 window (`X11WindowChrome.SetWindowOpacity`); 100% removes the property.
- It fades the whole window, text and icons included. It is not a translucent background.
- It needs a compositing window manager (KWin, Mutter, picom, ...). Without one the property is ignored. On Wayland sessions it goes through XWayland, and compositors that do not honour the property ignore it.
- Checked structurally under Xvfb (no compositor): the property is set to the expected cardinal. The visual effect is not verified there.

## Not offered
- **Per-pixel transparency (ARGB visual).** Uno's Skia X11 host creates the window with the default visual and its own GL/software surface; there is no hook to request a 32-bit visual or an alpha-capable surface. Making panels see-through to the desktop would need an Uno change.
- **Backdrop material (Mica/Acrylic).** Not implemented on Linux, so the Backdrop card is hidden there. The window uses a solid fill equal to the Mica base colour.
