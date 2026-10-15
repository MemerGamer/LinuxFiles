# Media and HTML previews without native window embedding

Status: media playback and rendered HTML previews are implemented for the preview pane. The release notes page still opens the browser. This note records why, how, and what a fully interactive HTML view would cost.

## What the X11 host actually did

`NativePreviewSupport.IsAvailable` (true on the X11 host) suggested that X11 embeds native previews. It does not in LinuxFiles:

- `MediaPlayerElement` needs the `Uno.WinUI.MediaPlayer.Skia.X11` add-in (libVLC) and `WebView2` needs `Uno.WinUI.WebView.Skia.X11` (WebKitGTK 4.1, embedded as an X11 child window). LinuxFiles references neither package. `LibVLCSharp.dll` only ships because `Uno.WinUI.Runtime.Skia.X11` depends on it.
- On the desktop target the info pane never creates `MediaPreview` or `HtmlPreview`. Media showed cover art and tags only, and HTML showed its source as highlighted code on X11 or as plain text through `NativePreviewFallback` on Wayland.
- `ReleaseNotesPage` does instantiate `WebView2` on X11, but without the add-in there is no native web view behind it.

So the Wayland work did not need to match an existing X11 implementation. The new previews draw through Skia, so they work the same way on both hosts.

## Media: libmpv software rendering (implemented)

- `Files.Platform.Linux/Media/MpvMediaPlayer.cs` loads `libmpv.so.2` at runtime (`LibraryImport`, no `DirectPInvoke`), so mpv is an optional dependency. Without it, the previous metadata preview is shown.
- Frames come from the libmpv render API with `MPV_RENDER_API_TYPE_SW` (`bgr0`, forced opaque) on a dedicated render thread. They are sized to the pane in device pixels (at most 1920x1080) and copied into a `WriteableBitmap`. UI updates are coalesced, so a slow UI thread drops frames instead of queuing them. mpv handles audio output itself.
- Nothing is decoded when a file is selected. mpv is created on the first Play (button or Ctrl+Space), so browsing stays cheap and untrusted media is only parsed when the user asks for it. Playback stops as soon as the selection changes, before the 100 ms preview debounce.
- The file is opened with `PreviewFile.OpenRead` (regular files only, inode pinned), and mpv loads it through `/proc/self/fd/<fd>`, so FIFOs, devices, and path swaps after the check are rejected.
- Hardening: user configuration, scripts, ytdl, and auto-loaded external files are disabled. Only the lavf demuxer runs, limited to `protocol_whitelist=[file]` and a container whitelist (no HLS, DASH, or concat). A file that expands into a playlist or changes path after loading is rejected, so a text playlist renamed to `.mp4` never fetches URLs. `audio-fallback-to-null` lets audio-only files play when no audio device is available.
- Cost: software scaling and conversion use CPU in proportion to the pane size, which is small. `hwdec` stays off for robustness. A future OpenGL render path (see the subsurface section) could hand frames to Skia as GPU textures.

## HTML: offscreen WebKitGTK snapshot in a helper process (implemented)

- `Files --linuxfiles-html-snapshot <path> <css width> <max height>` is handled first in `Main`. It initialises GTK 3 and WebKitGTK 4.1 in a short-lived child process, loads the file in a `GtkOffscreenWindow`, takes a snapshot, and writes a PNG to stdout. The app shows the PNG in a scroll viewer at the pane width.
- The work runs out of process because `gtk_init` changes the process locale and needs its own main loop, and a WebKit crash must not take down the file manager. WebKit also starts its own sandboxed web process.
- Hardening: the WebKit web process runs in WebKit's bubblewrap sandbox (`webkit_web_context_set_sandbox_enabled`, set before the first view is created, with no extra sandbox paths). If `bwrap` or `xdg-dbus-proxy` is missing, or WebKit would skip bubblewrap (Snap or Flatpak markers, `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS`), the helper refuses to render and the text fallback is shown, the same fail-closed policy as unsandboxed external thumbnailers. JavaScript, plugins, WebGL, and media are off, and navigation, new windows, and downloads are refused. The data manager is ephemeral.
- No network: the bounded document (up to 8 MiB) is loaded with `webkit_web_view_load_html`, with a Content-Security-Policy `<meta>` prepended and the file's own URI as the base. The policy allows only `file:` and `data:` images, styles, and fonts. `<link rel=preconnect>` ignores CSP and was verified to open a TCP connection, so the `LinkPreconnect`, `LinkPreconnectEarlyHints`, `LinkPrefetch`, and `SpeculationRulesPrefetch` WebKit features are turned off. This needs WebKitGTK 2.42 or later. On older versions the helper refuses to render and the text fallback is shown.
- Limits: 10 s in the helper, 15 s in the parent, a 64 MiB PNG cap, and a height cap of 4096 CSS pixels. The offscreen window is that tall and the snapshot covers only the visible region, so a page with a huge CSS height cannot make WebKit allocate a huge image. Trailing rows in the page background colour (the top-left pixel) are trimmed, so solid footers in another colour are kept. HiDPI uses the WebKit page zoom (1x or 2x) rather than `GDK_SCALE`, because GTK 3 only honours `GDK_SCALE` on X11. The helper is killed when the selection changes.
- What users see: the preview is a static picture. Links do not respond, `:hover` and scripted content are not shown, very long pages are cut off at the height limit, and documents that are neither UTF-8 nor BOM-marked Unicode fall back to source text. "Open with default app" stays available.
- Optional dependency: `webkit2gtk-4.1` (and GTK 3). When it is missing, or rendering fails, the plain text fallback is shown as before.

## Packaging

- AUR: `mpv` and `webkit2gtk-4.1` are listed as optional dependencies.
- Nix: neither library is in the flake's `runtimeLibs` yet, because WebKitGTK would add a large closure. Both previews fall back on NixOS until the flake adds them (perhaps behind an option).
- Flatpak: not covered yet.

## Release notes (not changed)

`ReleaseNotesPage` points at the upstream Files blog (`files.community/blog/posts/v…?minimal`), which describes the Windows app and needs network access and interactive links. That makes it a poor fit for a snapshot. On Wayland it keeps opening the page in the default browser. A better fix is to render the LinuxFiles GitHub release body (Markdown) with the existing Markdig preview renderer, which needs a fetch and caching design of its own.

## Interactive HTML on Wayland: options and cost

A live, scrollable, clickable web view on the native Wayland host would need one of these:

1. **Native element hosting via `wl_subsurface` in the Uno Wayland host.** The host would create a subsurface per native element, keep its position and clip in sync with layout (including scrolling, transforms, and DPI changes), and hand the toolkit a surface to draw into. libmpv could render into it through its OpenGL render API on an EGL window surface. WebKitGTK cannot: GTK 3 and GTK 4 widgets only render into their own toplevel `GdkSurface`, not into a foreign `wl_surface`. Airspace problems apply as on X11, because flyouts, menus, and tooltips over the element would be hidden. Estimated cost: a new Uno host subsystem (about 1.5k lines plus tests), upstream review, and it still would not help HTML.
2. **WPE WebKit with a custom view backend (`libwpe` + `wpebackend-fdo`, or the WPE platform API).** WPE exports frames as EGLImages, dma-bufs, or SHM buffers that can be drawn into Skia, and it accepts injected pointer, keyboard, and scroll events. This is the right route to a real web view on Wayland. Cost: new native dependencies that most desktop distributions do not install by default, GL and EGL interop with Uno's Skia context (or an SHM copy), input translation, and lifecycle and threading work. Estimated at 2 to 3k lines across an Uno add-in, `Uno.UI.WebView.Skia.Wayland` (`INativeWebView`), and its tests. This is the recommended follow-up if interactive web content becomes a requirement. It would also give LinuxFiles a working `WebView2` on Wayland for the release notes page.
3. **WebKitGTK offscreen with input forwarding.** Extend the snapshot helper into a long-lived process that streams damaged regions over shared memory and receives input events. This avoids new dependencies but means reimplementing a compositor protocol between the two processes, with high latency and complexity. Not recommended over WPE.

For read-only previews, the static snapshot gives most of the value at a small fraction of these costs.
