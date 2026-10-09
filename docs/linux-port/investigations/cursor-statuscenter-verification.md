# Cursor and Status Center follow-up (#128, #129)

## Implemented scope

- Resolve missing `XCURSOR_THEME` and `XCURSOR_SIZE` before `UseX11`. Preserve nonempty
  environment values, then prefer X resources, then read-only
  `gsettings get org.gnome.desktop.interface cursor-theme/cursor-size`.
- Apply resolved values to both the managed and libc process environments. No desktop
  settings are written. GNOME reads have a 1.5-second timeout and bounded output;
  unavailable settings leave Xcursor defaults intact. Reject fallback theme path names,
  control characters and excessive names/sizes. Invalid explicit resources do not cause
  a GNOME override.
- Load named chrome resize/title cursors through optional `libXcursor.so.1`, retaining
  the existing X11 core shape when the library, symbol or named image is unavailable.
- Replace the Status Center button's `Grid Margin="-16"` with centered 24-by-24 bounds
  and give its progress ring the same explicit dimensions. Retain the 16-by-16 vector icon,
  existing resources, visibility bindings and completion acknowledgement behavior.

## Uno Arrow mapping remains blocked on a host change

Inspected the installed Uno 6.7.135 `Uno.UI.Runtime.Skia.X11.dll` with `ilspycmd`:
`Uno.WinUI.Runtime.Skia.X11.X11PointerInputSource` is internal and its `PointerCursor`
setter maps Arrow (0) to `XC_arrow`. `Uno.UI.Hosting.X11HostBuilder` exposes rendering
backend, frame rate, media preload and HarfBuzz options, with no cursor mapping hook.
Changing the content Arrow to `XC_left_ptr` therefore needs an Uno fix/patch or a new
supported host hook. No reflection or replacement pointer source was introduced.
Our chrome title grip uses the named `left_ptr` cursor.

## Verification in the restricted worktree

- Platform/test build passed with `-warnaserror`: 0 warnings, 0 errors. The sandbox
  required `-p:RestoreDisableParallel=true -p:BuildInParallel=false -p:NuGetAudit=false`
  in addition to the prescribed low-priority, `-m:2`, no-node-reuse build options.
  The audit override applies only to this command, because the online vulnerability
  feeds are unreachable; no project configuration was changed.
- All 9 `CursorSettingsResolverTests` passed using the built test executable directly.
- The prescribed `dotnet test --project ... --no-build` aborts while creating its IPC
  named pipe (`SocketException (13): Permission denied`). Running the built test DLL
  directly with `DISPLAY` and `WAYLAND_DISPLAY` unset executes the suite:
  1,085 passed, 41 skipped, 9 failed (1,135 total).
- Those 9 failures are outside this work package: 3 bubblewrap thumbnail integration
  checks, 3 socket single-instance checks, and 3 `TrustedNativeDirectory` checks.
  The latter reject the sandbox's root directory, which is owned by UID 65534 rather
  than root/the current user. Private D-Bus/Xvfb checks also skip after socket binds
  are denied. Re-run these tests outside the restricted sandbox.
- The desktop app build reaches Uno resource injection but fails with `MSB4216`
  because its out-of-process task host cannot start/connect, followed by `MSB4181`.
  Full app/XAML compilation remains unverified and must be repeated outside the
  sandbox. The modified XAML is well-formed XML; no app was run.

## Exact remaining headless checks

The app was not launched for this work package. Every app check below must use
`scripts/linux/headless-run.sh` with
`FILES_SANDBOX_DRIVES=scripts/linux/showcase-drives.txt`, its throwaway HOME and private
display, and launch dry-run retained. Never use the real display or change real desktop/
system settings or user configuration. Use a private window manager advertising `_NET_WM_MOVERESIZE` for
chrome grip checks. All helpers and private processes must be cleaned up.

1. Cursor environment precedence: start with a known installed theme and size explicitly
   exported; give the private X server conflicting `Xcursor.theme`/`Xcursor.size`
   resources. Confirm both environment values win independently.
2. X resource precedence: unset both variables, seed the private server's cursor
   resources before app startup, including wildcard resource forms, and confirm they
   beat the GNOME fallback. Repeat with only theme or size explicit in the environment.
3. GNOME fallback and failures: with no cursor resources or environment overrides,
   supply sandbox-only read-only `gsettings get` responses for theme and size. Verify
   fallback; repeat with unavailable schemas/tool, malformed output, excessive output
   and a stalled helper. Startup must continue and timeout helpers must be reaped.
4. Cursor images: capture the pointer with XFixes or an equivalent private-display
   helper (ordinary screenshots can omit it). Compare content Arrow, splitter resize,
   title grip, four sides and four corners under two installed themes at sizes 24 and 48.
   Confirm the content Arrow limitation separately from the themed chrome cursors.
5. Cursor fallback: use a missing theme/image and an isolated runtime without
   `libXcursor.so.1`/its loader symbol. Grips must still show the corresponding core
   shape and dragging/resizing must work without errors. Confirm cursor handles and
   display connections are released on close.
6. Status Center idle layout: empty the cards and check the 16-by-16 vector icon is
   centered, visible and unclipped at 1x, 1.5x and 2x, in light/dark themes, at narrow
   and wide window widths. Check hover/focus, tooltips, click target and flyout position.
7. Status Center active layout: run one, then multiple sandbox file operations. Check
   the 24-by-24 determinate ring, numeric badge, progress updates and icon exclusion.
   Include an operation that cannot provide progress and enough concurrent items to
   produce a two-digit badge.
8. Status Center transitions: capture idle -> running -> success, idle -> running ->
   error/cancel, mixed running/error, and a failure added without a preceding running
   card. Check before/after opening the flyout and after removing completed/all cards.
   Confirm a visible icon or badge always remains and success acknowledgement works.

The reported Status Center failure cannot be conclusively diagnosed without those
rendered checks. In particular, `StatusCenterViewModel.HasAnyItemInProgress` mutates
`ShowProgressRing`, completed success stays latched until the flyout opens, and visual
states also set `StatusCenterIcon.Visibility`. These interactions are retained for
now; investigate them if checks 7–8 reproduce a blank or stale indicator.
