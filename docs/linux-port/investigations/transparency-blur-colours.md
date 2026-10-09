The niri no-op is most likely a compositor compatibility issue. **The repository’s claim that Uno cannot provide per-pixel transparency is also outdated for the pinned package.**

Read-only investigation completed: rules and plan read first; no edits, builds, app launches, commits, pushes or comments.

**Findings and root causes**

- **The slider is wired correctly.** [AppearancePage.xaml:140](src/Files.App/Views/Settings/AppearancePage.xaml:140) binds `.2–1` to the persisted setting; [MainWindow.Linux.cs:61](src/Files.App/MainWindow.Linux.cs:61) dispatches changes to `SetWindowOpacity`.
- [X11WindowChrome.cs:126](src/Files.Platform.Linux/Windowing/X11WindowChrome.cs:126) writes `_NET_WM_WINDOW_OPACITY` as CARDINAL/32, correctly using a native-sized C long; 100% deletes it. This fades text and icons too.
- **Likely niri cause:** current xwayland-satellite neither advertises nor forwards this property; its property handler ignores it. Thus a successful X11 write produces no visual change. This is a source-based inference; the user’s installed bridge/version was not inspected. [Satellite source](https://raw.githubusercontent.com/Supreeeme/xwayland-satellite/main/src/xstate/mod.rs)
- **The UI overstates support:** [AppearanceViewModel.cs:331](src/Files.App/ViewModels/Settings/AppearanceViewModel.cs:331) checks only `OperatingSystem.IsLinux()`. Compositor presence alone would also be insufficient.
- **Important documentation correction:** [appearance-transparency.md:17](docs/linux-port/appearance-transparency.md:17) says Uno uses the default visual. Decompiling installed **6.7.135**—pinned at [Directory.Packages.props:49](Directory.Packages.props:49)—shows `CreateSoftwareRenderWindow` requests 32-bit TrueColor, falling back to 24-bit; GLX also prefers depth 32.
- Its `X11SoftwareRenderer` creates a BGRA8888/premultiplied surface. **A public hook exists:** `Uno.UI.Xaml.WindowHelper.SetBackground(window, brush)` updates `X11Renderer`’s clear colour, whose default is white. These are binary-symbol findings, not proof of end-to-end compositor alpha support.
- **App layers still cover transparency:** [MainPage.xaml:18](src/Files.App/Views/MainPage.xaml:18) paints the root background; [LinuxDesktopResources.xaml:147](src/Files.App/Styles/LinuxDesktopResources.xaml:147) and [AdwaitaResources.xaml:26](src/Files.App/Styles/AdwaitaResources.xaml:26) supply opaque fills. [AppThemeResourcesHelper.cs:26](src/Files.App/Helpers/UI/AppThemeResourcesHelper.cs:26) deliberately preserves Linux’s solid default.
- **No blur setting/token exists.** Linux forces `BackdropMaterialType.Solid` and ignores backdrop writes at [AppearanceSettingsService.cs:100](src/Files.App/Services/Settings/AppearanceSettingsService.cs:100); the backdrop card is hidden at [AppearanceViewModel.cs:329](src/Files.App/ViewModels/Settings/AppearanceViewModel.cs:329).
- **System colours currently means light/dark only.** [LinuxColorScheme.cs:67](src/Files.Platform.Linux/Theme/LinuxColorScheme.cs:67) reads portal preference and fallbacks; [AppResourcesService.cs:35](src/Files.App/Services/App/AppResourcesService.cs:35) loads a fixed Adwaita dictionary. Portal `0`/unknown currently becomes light instead of allowing fallback.

**Concrete proposed changes**

- **S — Correct support reporting:** update `AppearanceViewModel`, `AppearancePage`, localized descriptions and the transparency document. Track whole-window opacity support separately from per-pixel alpha and blur; allow “unknown.” Detect satellite explicitly; do not treat an existing atom or compositor selection as proof.
- **M — First alpha experiment:** in `MainWindow.Linux.cs`, use `WindowHelper.SetBackground(..., new SolidColorBrush(Colors.Transparent))`; add a separate background-opacity setting and adjust existing surface brushes through `ResourcesService`. Keep text/icons opaque and audit overlapping fills so opacity is not compounded.
- **L, conditional — Uno patch/fork:** only if that experiment fails, patch `Hosting/X11XamlRootHost.cs` and relevant `Rendering/*` paths to select an XRender-confirmed alpha visual, preserve premultiplied alpha and support transparent clearing consistently. Validate GLX/EGL/Vulkan individually. Another native Skia window/host would duplicate input, lifetime and hosting work and is also **L**.
- **Native Wayland is not a switch in the pinned Skia Desktop host.** [Program.cs:100](src/Files.App/Platforms/Desktop/Program.cs:100) explicitly selects X11; official Desktop documentation lists Linux X11/framebuffer. A Wayland host or legacy GTK-head migration is **L**, including replacement of X11-dependent integration. [Uno Desktop documentation](https://platform.uno/docs/articles/features/using-skia-desktop.html)
- **M — Blur setting/backend:** add `BackdropMode=Solid|Transparent|Blur` plus `BackgroundOpacity`, separate from existing `WindowOpacity`. Resolve `BlurRequested` against backend capability and retain a readable fallback.
- For KWin X11, extend `X11WindowChrome` using existing [X11Native.cs:89](src/Files.Platform.Linux/Native/X11Native.cs:89) wrappers: write `_KDE_NET_WM_BLUR_BEHIND_REGION` as CARDINAL/32 rectangles in device pixels, or an empty whole-window region; delete it when disabled. Detect KWin’s advertised effect support. The hint does not make opaque pixels transparent or specify blur radius. [KDE implementation](https://raw.githubusercontent.com/KDE/kwindowsystem/master/src/platforms/xcb/kwindoweffects.cpp)
- `org_kde_kwin_blur` requires the owning Wayland `wl_surface`, unavailable through this X11 host; a separate Wayland connection cannot access it. [KDE protocol](https://github.com/KDE/plasma-wayland-protocols/blob/master/src/protocols/blur.xml)
- **niri is version-dependent:** documented blur exists since **26.04**, through `ext-background-effect` or compositor window rules. Current satellite source does not bridge KDE’s X11 blur hint. Other compositors need their own supported protocol/rules; there is no universal X11 blur token. [niri effects](https://raw.githubusercontent.com/niri-wm/niri/main/docs/wiki/Window-Effects.md)

**“System (GTK/portal) colours” design — M for portal/basic palette; L for comprehensive GTK fidelity**

Add `ISystemAppearanceService` under `Files.Platform.Abstractions`, its implementation under `Files.Platform.Linux/Theme`, and a palette snapshot/change event. Add `ColourSource=Files|Adwaita|System` to the appearance contract/service/view model/page; migrate `UseAdwaitaTheme` without changing existing choices.

Use portal `ReadAll`/`SettingChanged` for `color-scheme` (`0/1/2`), `accent-color` (sRGB doubles) and `contrast` (`0/1`). Missing/invalid values remain unset. **The portal exposes preferences, not GTK’s full palette.** [Settings specification](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Settings.html)

Resolve theme identity through GTK/XSettings where available, `GTK_THEME`, read-only `gsettings get org.gnome.desktop.interface gtk-theme`, then XDG-aware `gtk-3.0`/`gtk-4.0/settings.ini`. Read selected-theme CSS and user `gtk.css` overrides. Theme names alone supply no colours. [GTK settings](https://docs.gtk.org/gtk4/class.Settings.html)

Prefer an optional isolated GTK resolver using `gtk_style_context_lookup_color` for legacy named colours. A bounded managed CSS reader can support literals/aliases initially, but imports, expressions and cascade need explicit fallback. Modern libadwaita uses CSS variables; compatibility `@theme_*` aliases may miss overrides, so full modern resolution needs a spike. [GTK lookup](https://docs.gtk.org/gtk3/method.StyleContext.lookup_color.html), [libadwaita colours](https://gnome.pages.gitlab.gnome.org/libadwaita/doc/main/css-variables.html)

| GTK/libadwaita role | Existing resource targets |
|---|---|
| `theme_bg_color` / window background | `App.Theme.BackgroundBrush`, secondary file-area fill |
| `theme_base_color` / view background | File-area fill, `LayerFillColorDefaultBrush` |
| Headerbar / sidebar backgrounds | Address-bar, toolbar, sidebar and info-pane fills |
| `theme_fg_color`, `theme_text_color` | `TextFillColor*Brush`, control/toolbar foreground states |
| Selected/accent background and foreground | `SystemAccentColor*`, accent brushes, item/tile selection states |
| Borders / popover / card colours | Stroke/divider brushes, `Files.Linux.FlyoutSurfaceBrush`, card fills |

Apply a final system dictionary in `ResourcesService`, overriding hard-coded control states as well as accent keys; preserve correctly typed `Color`/`SolidColorBrush` values. Refresh on the UI thread using existing [AppThemeModeService.cs:87](src/Files.App/Services/App/AppThemeModeService.cs:87) machinery. Preserve explicit light/dark and manual colour choices; high contrast should force readable, opaque surfaces.

**Risks and verification**

- Risks: renderer/driver alpha differences, stacked fills, stale resources across windows, selection contrast, GTK3/GTK4 divergence, Flatpak theme visibility, optional native-library availability and hostile CSS/imports. Keep reads bounded and native interop in `Files.Platform.Linux/Native`.
- **Only app execution can verify** slider binding/persistence, native property placement/removal, actual window depth/alpha format, transparent clear, opaque text, repaint/resize behaviour and live palette/resource refresh.
- Use only [headless-run.sh](scripts/linux/headless-run.sh:1), private Xvfb, disposable HOME and isolated buses. Bare Xvfb proves properties/render structure; visual transparency/blur needs a private compositing WM, e.g. the script’s `FILES_HEADLESS_WM`.
- niri/XWayland behaviour needs an isolated nested compositor/satellite harness rooted in Xvfb; bare Xvfb cannot prove it. That harness and GPU-specific behaviour remain unverified. Never use the real display.
