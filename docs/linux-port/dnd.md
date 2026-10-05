# Clipboard and drag & drop of files (W-CLIP)

Uno 6.7's X11 host cannot publish file lists on the clipboard (`X11ClipboardExtension` offers text, URI and HTML only) and
`X11DragDropExtension.StartNativeDrag` throws `NotImplementedException`. Files works around both on its own X connection.

## Pieces

| Piece | Where |
| --- | --- |
| `IClipboardService`, `IFileDragSource` | `src/Files.Platform.Abstractions/Clipboard` |
| Format logic (pure, unit tested) | `Files.Platform.Linux/Clipboard/ClipboardFormats.cs` |
| X11 selection owner, requestor and XDND source | `Files.Platform.Linux/Clipboard/X11SelectionHost.cs` (Xlib via `LibraryImport` in `Native/X11Native.cs`) |
| Files glue (`#if !WINDOWS` one-line hooks) | `Files.App/Platforms/Desktop/Services/DesktopFileDragHelper.cs` |
| Crash guard for Uno's failed native drag | `Files.App/Platforms/Desktop/DesktopRuntimeGuards.cs` |

### Why our own Xlib connection and no managed X11 client

A managed X11 protocol client (for example Tmds.X11) would avoid Xlib, but none with a compatible licence and AOT-safe code was
evaluated as worth the dependency: Uno already needs libX11, `LibraryImport` is AOT-safe, and Xlib handles authentication and
extensions. The host opens its own `Display` (so it never competes with Uno's event loop) and runs all Xlib calls for that connection
on one thread; other threads post work to it and wake it through a pipe.

## Clipboard

Targets served for a copied or cut file list: `TARGETS`, `x-special/gnome-copied-files` (`copy` or `cut`, then one URI per line),
`text/uri-list` (CRLF), `application/x-kde-cutselection` (`1`, only for cut), `UTF8_STRING`, `text/plain;charset=utf-8`, `text/plain`
(one path per line). URIs percent-encode everything except RFC 3986 unreserved characters and `/`.

Requests larger than one X request use `INCR` (threshold from `XMaxRequestSize`). Reading prefers `x-special/gnome-copied-files`,
then `text/uri-list` plus the KDE marker, so a cut in Nautilus, Dolphin or Files pastes as a move. Pasted data is untrusted: only
`file:` URIs for the local host and absolute paths without NUL are accepted, and the list is capped (100,000 entries, 64 MiB).

Files integration: after `Clipboard.SetContent` in `TransferHelpers` the same paths are published through `IClipboardService`
(the last X11 selection owner wins, so the call order matters); `UIFilesystemHelpers.PasteItemAsync` first asks `IClipboardService`
and runs a copy or move (cut) through `IFilesystemHelpers.CopyItemsAsync` / `MoveItemsAsync`, falling back to the generic paste.

## Outbound drag (XDND source, version 5)

`BaseLayoutPage.FileList_DragItemsStarting` calls `DesktopFileDragHelper.StartExternalDrag`. The host then follows the pointer with
`XQueryPointer` (Uno's window keeps the implicit pointer grab, so no second grab is needed), finds the deepest XDND-aware window under
it (skipping windows of this process, where Uno handles the drag), and sends `XdndEnter`, `XdndPosition`, `XdndDrop`/`XdndLeave`.
Data is served from the `XdndSelection` selection (`text/uri-list`, `text/plain`). Action is copy, or move while Shift is held; the
receiving application performs the transfer.

When the pointer leaves the window Uno still calls `StartNativeDrag` and raises the `NotImplementedException` as a recoverable
`Application.UnhandledException`. `DesktopRuntimeGuards` marks exactly that exception as handled instead of letting Files' crash
handler exit the app (that was the reported crash). Uno then ends its own drag when the button is released.

## Dragging out to Wayland-native apps on KDE (XWayland bridge)

Files runs under XWayland, so a Wayland-native target (Dolphin, Kate) is not a window on the X server and the XDND tree walk never
finds it. KWin bridges X11 sources to Wayland (its XWayland `XToWlDrag`):

1. KWin watches `XdndSelection` ownership through XFixes. When an X client takes it, KWin starts an X-to-Wayland drag and, once the
   pointer is over a native Wayland surface, creates and maps a per-drag, `XdndAware`, 8192x8192 proxy window (a second one next to its
   permanent, unmapped proxy). `FindXdndTarget` finds that window like any other XDND target and the normal XdndEnter, XdndPosition,
   XdndStatus, XdndDrop handshake runs against it; KWin translates it to `wl_data_device`.
2. KWin ignores an `XdndSelection` owner taken with `CurrentTime`. Qt and GTK pass the real server time. Files used `0`, so no proxy was
   ever created and nothing could be dropped (dragging in worked, because that direction is KWin's source). **Fix**: take the selection
   with a real X server time (zero-length property append on the own window, read the `PropertyNotify` time), and use that time in
   `XdndPosition` (data3) and `XdndDrop` (data2).
3. Nothing else differs from the protocol this file already described: `text/uri-list` offered in `XdndEnter`, `XdndStatus` and
   `XdndFinished` are answered by KWin's proxy window, which is also the `Target` we match messages against.

Verified on a nested KWin Wayland compositor (`kwin_wayland --x11-display <private Xvfb> --xwayland`, windowed on the private display) with
the Files window and a Wayland-native Dolphin (`QT_QPA_PLATFORM=wayland`): before the change the proxy window never appears and the drop is
lost; after it Dolphin shows the drop menu (Move/Copy/Link), and "Copy Here" creates the file. Not verified on the owner's real session:
fractional scaling, real GPU, multi-monitor, `XdndActionAsk`, large selections, and Dolphin's Shift/Ctrl action keys (we still only
send copy or move from the Shift state).

Needs a real-desktop test: drag a file and a multi-selection from Files into Dolphin (folder view and onto a folder), Kate and the desktop;
check copy by default, Shift for move, and that dropping back onto Files itself still works.

Reproduction recipe (all private; never the user's display): start `Xvfb`, run `kwin_wayland --x11-display :N --xwayland --socket <short> `
under `dbus-run-session` with a private `XDG_RUNTIME_DIR` (the socket path must be shorter than 108 bytes) and the app as its client, run
`dolphin` with `WAYLAND_DISPLAY=<socket>`, and drive input with `xdotool` on the outer Xvfb.

## Verified (private Xvfb)

- GTK3 reader sees all targets above, with the right bytes, for copy and cut; Files' reader reads `x-special/gnome-copied-files`
  (cut) and KDE-style `text/uri-list` + `application/x-kde-cutselection` owners.
- `INCR` for a 3000-entry list (service to service, small threshold).
- XDND to a GTK3 drop target: drop with the right URIs, copy and (Shift) move.
- xdotool drag from a sidebar item out of the Files window no longer exits the app.

## LINUX-TODO

- `LINUX-TODO(dnd)`: no drag icon or cursor feedback outside the window (the pointer is grabbed by Uno's window; changing it needs
  `XChangeActivePointerGrab` on that connection, or the Uno extension point). Dropping on a window that answers `XdndStatus` with
  "reject" silently does nothing.
- `LINUX-TODO(dnd)`: the guard only covers file lists (`FileList_DragItemsStarting`). Drags from the sidebar, tabs and the shelf leave
  the window without offering anything; Uno's failure is swallowed.
- `LINUX-TODO(clip)`: `TIMESTAMP`, `MULTIPLE` and `SAVE_TARGETS` are not served, so the list disappears when Files exits unless a
  clipboard manager grabs it from the live selection.
- `LINUX-TODO(clip)`: no XFixes monitoring in `IClipboardService` (`ContentChanged` fires for our own changes and ownership loss);
  paste enablement relies on Uno's `Clipboard.ContentChanged`, which sees the file lists through `text/uri-list`.
- Wayland (no XWayland) would need `wl_data_device` or Uno's native host; not covered.
- Upstream: a proper fix is an `IDragDropExtension.StartNativeDrag` implementation in Uno's X11 host plus a file-list-aware
  `X11ClipboardExtension`; both could reuse the protocol code here. Not proposed upstream from this repo.
