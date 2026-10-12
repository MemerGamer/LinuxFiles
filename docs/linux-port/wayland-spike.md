# Native Wayland host spike — issue #113

Investigated 2026-10-12. Documentation only; no package, host or runtime changes.

## Decision

Keep XWayland with the existing scale resolver for the immediate blurry-without-scale problem (option D). Pursue a native host through Uno collaboration (A) only after confirming a supported hosting contract and maintenance ownership. A standalone libwayland host (B) is substantial platform work; GTK4 (C) reduces protocol work but still requires a new Uno adapter. Neither is an environment-variable switch.

The [earlier pixelation investigation](investigations/pixelation-findings.md) records the reporter's successful `UNO_DISPLAY_SCALE_OVERRIDE=1.25` workaround on niri + xwayland-satellite. That is reported evidence, not a reproduction in this spike. Missing render scale can make the compositor enlarge an already rasterized buffer; native fractional scaling addresses that path, but does not fix Skia font hinting or every source of soft text. Uno also documents unreliable DPI detection under XWayland and the override in its [Desktop host guidance](https://platform.uno/docs/articles/features/using-skia-desktop.html).

## Evidence and package status

Repository guidance read: `CLAUDE.md`, `AGENTS.md`, `AGENT-RULES.md` and `upstream-policy.md`. Uno contribution here means contribution to the framework, not resuming sync with Files Community.

The app pins `Uno.Sdk` **6.7.30** in [global.json](../../global.json) and `Uno.WinUI` / `Uno.WinUI.Runtime.Skia.X11` **6.7.135** in [Directory.Packages.props](../../Directory.Packages.props). Its [desktop entry point](../../src/Files.App/Platforms/Desktop/Program.cs) explicitly calls `UseX11`, with software, OpenGL, OpenGLES or Vulkan selected through `RenderingBackendSelector`. Changing the GPU API does not change the window protocol.

Inspected `~/.nuget/packages/uno.*` with file searches, nuspec metadata, XML documentation and binary strings. A temporary .NET tool used `PEReader` / `System.Reflection.Metadata` to enumerate type and method definitions without loading Uno or decoding IL. No decompiler was used. No host sources or host XML docs were found in the runtime packages; Android Uno XML supplied a focus-contract description, not proof of Skia behavior.

| Cached package | Version | Relevant assembly / finding |
| --- | --- | --- |
| `Uno.WinUI.Runtime.Skia` | 6.7.135 | `Uno.UI.Runtime.Skia.dll`; shared host infrastructure, not a Wayland shell |
| `Uno.WinUI.Runtime.Skia.X11` | 6.7.135 | `Uno.UI.Runtime.Skia.X11.dll`; net9.0 and net10.0 assets |
| `Uno.WinUI.Runtime.Skia.Linux.FrameBuffer` | 6.7.135 | `Uno.UI.Runtime.Skia.Linux.FrameBuffer.dll`; framebuffer target, not a compositor client |
| `Uno.WinUI.Runtime.Skia.Win32`, `.MacOS` | 6.7.135 | `Uno.UI.Runtime.Skia.Win32.dll` and `.MacOS.dll`; sibling shells, no Linux Wayland implementation |
| Wayland, Gtk or GTK4 host | None found | Cache absence alone does not establish absence from all NuGet releases |

Package IDs use `Uno.WinUI`, while assembly names retain `Uno.UI.Runtime.Skia.*`. The X11 nuspec identifies source commit `da217df2312e9e39676880f9cb4b8d519cc297e0`, branch `release/stable/6.7`, Apache-2.0, and SkiaSharp **3.119.0**. Preserve framework licensing if borrowing source. SDK `Uno.Implicit.Packages.ProjectSystem.Desktop.targets` includes the X11 runtime. LinuxFiles disables implicit Uno packages and references X11 explicitly.

Current [Uno Desktop documentation](https://platform.uno/docs/articles/features/using-skia-desktop.html) lists Linux X11 and framebuffer, plus Win32 and AppKit; it supplies no native Wayland host. The [GTK documentation](https://platform.uno/docs/articles/features/using-skia-gtk.html) describes **GTK3**, retired starting with Uno 5.2, and warns that acceleration can require X11. The fetched [legacy NuGet listing](https://www.nuget.org/packages/Uno.UI.Runtime.Skia.Gtk) displays stable **5.6.99** and prerelease **5.7.0-dev.814**; its dependency graph uses GtkSharp 3.x and Uno.UI 5.x. This older, cached web listing is not evidence of a compatible 6.7 GTK4 package or an exhaustive latest-version query. NuGet flat-container queries were unavailable through the web tool.

[Uno issue #14075](https://github.com/unoplatform/uno/issues/14075) is an open report about GTK GL/GLES on Wayland using Uno **4.10.26**, with no assignee or milestone in the fetched page. It shows demand and historical failures, not a scheduled native Desktop host. No committed Wayland delivery date or GTK4 host plan was found in the consulted sources. Recheck with Uno maintainers before funding A; do not interpret this limited search as proof that no private or newer work exists.

## How the pinned X11 host is structured

The following boundaries are confirmed by metadata/symbols; method bodies, threading guarantees and runtime behavior were not inspected.

| Boundary | Observed contract / symbols | Wayland implication |
| --- | --- | --- |
| XAML root | `Uno.UI.Hosting.IXamlRootHost`: `RootElement`, `InvalidateRender`, `ResignNativeFocus`. Interface is internal. `X11XamlRootHost` is also internal, despite some public members. | A separate assembly cannot simply implement this interface through public APIs. First prove a supported extension path, obtain Uno API changes, or maintain framework source changes. No reflection workaround. |
| Native window | Public sealed `Uno.UI.NativeElementHosting.X11NativeWindow`, `WindowId`; host has `RootX11Window`, `TopX11Window`, configure/close hooks. | Replace XIDs with host-owned surfaces and native-window lifetime/presenter integration. A Wayland object cannot be substituted for an XID. |
| Dispatch and rendering | `InitializeX11EventsThread`, `Run`, `QueueAction`, `InitRenderThread`, `RenderLoop`, `CreateFramePacer`; `X11SoftwareRenderer`, `X11OpenGLRenderer`, `X11EGLRenderer`, `X11VulkanRenderer`. | Integrate Wayland dispatch, Uno dispatcher and render scheduling; EGL in the existing X11 assembly is not evidence of Wayland hosting. |
| Input | `X11PointerInputSource`, `X11KeyboardInputSource`, `SetPointerSource`, `SetKeyboardSource`, XI2 symbols. | Supply pointer, keyboard, touch, focus and text-input adapters preserving Uno event semantics. |
| Clipboard / DnD | `X11ClipboardExtension`, `X11DragDropExtension`, owner-loop and URI-list symbols. | Replace X selections/XDND with Wayland data offers/sources and asynchronous fd transfers. |
| Display | `X11DisplayInformationExtension`, XRandR symbols, `XftDotdpi`, `EnvironmentUnoDisplayScaleOverride`, `SetCurrentScale`, `RasterizationScale`. | Supply per-surface output/scale updates rather than inferring native scale from X resources. |

LinuxFiles adds its own dependencies: [MainWindow.Linux.cs](../../src/Files.App/MainWindow.Linux.cs) extracts `X11NativeWindow.WindowId` for [X11WindowChrome](../../src/Files.Platform.Linux/Windowing/X11WindowChrome.cs); [LinuxClipboardService](../../src/Files.Platform.Linux/Clipboard/LinuxClipboardService.cs) uses [X11SelectionHost](../../src/Files.Platform.Linux/Clipboard/X11SelectionHost.cs) for file clipboard and outbound XDND. [DesktopRuntimeGuards](../../src/Files.App/Platforms/Desktop/DesktopRuntimeGuards.cs) explicitly guards Uno's unsupported outbound native drag. These features require native replacements, not merely Uno's basic text clipboard. Keep the existing `IClipboardService` / `IFileDragSource` abstraction boundaries.

## What a native host needs

1. **Connection, window and lifecycle.** Negotiate registry versions, create `wl_surface` plus `xdg_surface` / `xdg_toplevel`, acknowledge configure serials before matching commits, handle resize/close/maximize/fullscreen and multiple windows. Handle ping/pong, output removal, connection failure and shutdown. Map app-id, decorations and presenter behavior. Interactive move/resize requires a valid input serial; arbitrary global positioning and X11 root-window access cannot be carried over. Surfaces belong to their connection: a second connection cannot operate Uno's existing X11 window. See the [Wayland model](https://wayland.freedesktop.org/docs/book/Protocol.html) and [xdg-shell protocol](https://wayland.app/protocols/xdg-shell).
2. **Skia buffers and scheduling.** For B, create a Wayland EGL display, `wl_egl_window`, EGL context/surface and a compatible Skia GPU render target. Coordinate configure sizes, context affinity, frame callbacks, presentation, damage and teardown. Provide a software `wl_shm` fallback with buffer release tracking. Handle unavailable drivers and context loss; showing one frame is not a complete host.
3. **Fractional scale.** Use `wp_fractional_scale_v1` preferred scale (units of 1/120) with `wp_viewporter`: render at the appropriate physical buffer size, set logical viewport destination, and keep buffer scale at 1 for that fractional path. Update Uno `XamlRoot.RasterizationScale`, layout, hit testing and scale-sensitive bitmap caches together. Fall back to integer output/preferred buffer scale when extensions are missing. For a logical 800×600 surface at 1.25, the target buffer is 1000×750, not an 800×600 bitmap enlarged afterward. Round other sizes consistently and test monitor transitions. See [fractional-scale protocol](https://wayland.app/protocols/fractional-scale-v1) and [viewporter](https://wayland.app/protocols/viewporter).
4. **Input and editing.** Adapt `wl_seat` devices, surface-local pointer coordinates, scroll frames, keyboard keymaps/modifiers/repeat through xkbcommon, cursor scaling and focus cancellation. Add IME/preedit/commit via supported text-input protocols; selection and rename must work with non-Latin input. Test Uno pointer capture semantics when the compositor owns the grab.
5. **Clipboard and file DnD.** Implement `wl_data_device_manager` offers, MIME negotiation, serial-authorized selection/drag, fd streaming, cancellation and copy/move completion. Preserve `text/uri-list` and desktop file-copy/cut formats from `ClipboardFormats`; clipboard persistence depends on an external manager. Bound untrusted offers and transfer sizes; never interpolate filenames into shell commands. Primary selection is a separate optional protocol. See the [core protocol](https://wayland.app/protocols/wayland).
6. **Desktop integration and packaging.** Adapt chrome/appearance capabilities and portal parent-window identity, accessibility, native-control embedding and multi-window activation. Keep optional compositor protocols optional; X11 opacity/blur properties have no universal Wayland equivalent. Use `Files.Platform.Abstractions` / `.Linux`, source-generated `LibraryImport` and generated protocol bindings or a narrow native shim. Wayland's generated C inline wrappers are not exported library functions; callback ownership and ABI need explicit AOT-safe design. See [Wayland binding generation](https://wayland.freedesktop.org/docs/book/Protocol.html#code-generation).

## Options and effort

Estimates are engineering judgment, **person-days for one developer experienced in Uno, C# interop and Linux graphics**, not elapsed calendar promises. Prototype means a native Uno window with rendering, basic input and scale; usable delivery includes file clipboard/DnD, IME, lifecycle and compositor testing. Usable totals include prototype work. Learning time, external review queues and continuing maintenance are excluded; uncertainty is high until hosting access is proven.

| Option | Prototype | Usable delivery | Main risks |
| --- | ---: | ---: | --- |
| **A — contribute a host / hosting API to Uno** | 15–25 days | 60–100 days | Requires maintainer agreement, host implementation and upstream tests/API review; acceptance and release timing unknown. LinuxFiles service/chrome integration remains local work. Could use B or C internally. |
| **B — own libwayland + wl_surface + EGL/Skia host** | 15–30 days | 70–120 days | Internal Uno contracts may require framework patches; owns protocols, event loops, drivers, IME, accessibility and transfer security. Highest recurring maintenance cost. |
| **C — new GTK4 shell around Uno Skia** | 10–20 days | 40–75 days | GTK manages Wayland windows/input/data transfer, but no compatible Uno GTK4 host was found. Must adapt dispatch, rendering, scale, text editing, accessibility and Uno internals; bindings and AOT packaging need proof. |
| **D — XWayland + compositor scale / existing resolver** | 1–2 days | 3–7 days | Validation and targeted follow-up only; mixed-output scaling and compositor double scaling remain possible. Does not deliver native Wayland or universal automatic scale detection. |

For C, [Gtk.GLArea](https://docs.gtk.org/gtk4/class.GLArea.html) provides a GL context/render callback, but bridging its framebuffer into Skia and Uno remains implementation work. GTK4 fractional surface support is exposed through [Gdk.Surface.get_scale](https://docs.gtk.org/gdk4/method.Surface.get_scale.html) since 4.12; older GTK runtimes and managed bindings need an explicit compatibility decision. Porting the retired GTK3 package is not a dependency upgrade to GTK4. GTK-rendered widgets also do not automatically make a Skia-painted Uno tree accessible.

## Recommendation and gates

Use D for #113: preserve explicit overrides and existing Xft/Qt/GDK handling in [DisplayScaleResolver](../../src/Files.Platform.Linux/Windowing/DisplayScaleResolver.cs). The opt-in niri IPC fallback already exists; do not estimate it as new work. Validate at 1.0/1.25/1.5/2.0 and distinguish crisp rasterization from correct logical window size. Avoid changing compositor settings globally or forcing a scale for every Wayland session. A startup process-wide scale is not per-window mixed-DPI support.

For native support, first allocate **3–5 days within A's estimate** to establish whether Uno will expose/support the needed root, window, input and rendering contracts. Keep X11 fallback. If the contract is viable, evaluate C with a bounded 10–20 day prototype; choose B only with explicit long-term host ownership. Stop if success requires runtime reflection into internal Uno types. No framework upgrade or new dependency is proposed by this document.

Before any native-host release, require Wayland-only startup with `DISPLAY` unset, crisp fractional rendering and correct hit testing, mixed-DPI monitor moves, resize/minimize/restore, multi-window close, keyboard shortcuts and IME rename, clipboard ownership changes and large file offers, inbound/outbound DnD with Nautilus/Dolphin, cancellation without accidental moves, and GPU/software fallback. Cover GNOME/Mutter, KDE/KWin and niri/wlroots-family environments separately; no single compositor proves interoperability. Use a private nested/headless compositor and throwaway home through an extended `scripts/linux/headless-run.sh` workflow. Xvfb alone cannot verify Wayland scaling.

## Validation and limitations

Performed targeted repository/cache searches, package metadata/XML/string inspection, read-only PE metadata enumeration, and public documentation/issue checks. Source fetches failed; architecture details beyond metadata are proposed requirements, not verified call flow. Cache inventory is exhaustive only for the local installed packages, not all published versions or development branches.

No Uno desktop build or application run was attempted, as required by the sandbox constraint. Platform build/tests were not run for this documentation-only change; they would not validate a Wayland host or compositor scaling. No native prototype, GPU check, accessibility check or new visual reproduction was performed. Final document verification uses `git diff --check` and local relative-link checks.
