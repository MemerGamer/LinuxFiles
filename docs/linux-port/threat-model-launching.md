# Threat model: opening and launching files (Linux)

Scope: double-click / Enter / "Open with" in `NavigationHelpers.Linux.cs`, `LinuxLauncherService`, `DesktopEntryParser`, `OpenDecision`, and KDE service-menu actions discovered by `ContentPageContextFlyoutFactory` and prepared by `ServiceMenuLaunchPlan`.

Attacker: controls a file's name, content, mode bits and symlinks (downloaded archive, shared folder, USB stick). Goal: get code to run without the user knowingly confirming exactly that code.

## Gates

| # | Gate | Where | Tests |
|---|---|---|---|
| 1 | Decision from file bytes (ELF / `#!` magic), not name or MIME glob | `OpenDecision.Sniff/Decide` | `OpenDecisionTests` |
| 2 | Binaries (+x) always ask Run/Cancel with full path; scripts (+x) ask Run/Display/Cancel; ELF without +x is refused | `OpenDecision`, `ExecutePlanAsync` | `ContentDecidesNeverTheName` |
| 3 | `.desktop`: silent only if under an XDG `applications` dir; otherwise always ask showing the exact argv; +x is not trust | `OpenDecision`, `ExecutePlanAsync` | `Desktop_Trusted...` |
| 4 | Strict `.desktop` parse: NUL rejected anywhere in the file; control chars rejected inside `[Desktop Entry]` (including whitespace-padded group headers such as `\v[Foo]`) and in Exec; one `[Desktop Entry]` group, no duplicate keys, Type+Exec required, no localized `Exec[..]/Type[..]/Terminal[..]/Path[..]/TryExec[..]`; on the launcher path other groups and malformed lines are ignored, service menus stay strict everywhere; invalid = refused (never given to a handler) | `DesktopEntryParser.ParseStrict` | `StrictParse_*` |
| 5 | Display equals exec: the argv is computed once (`DesktopExecExpander.Expand`), shown (control chars escaped), and passed unchanged to `ILauncherService.RunCommandAsync`; nothing is re-parsed or re-expanded after the dialog | `LinuxOpenPlan.Argv`, `RunCommandAsync` | `RunCommand_StartsExactlyTheDisplayedArgv...`, `Exec_ParsedOnce...` |
| 6 | Dialog answer maps to exactly the described follow-up; Cancel/close/dialog failure never opens anything. "Display" opens the file in an explicitly chosen `text/plain` handler, never the file's own MIME default | `OpenDecision.Resolve`, `DisplayAsTextAsync` | `DialogAnswerMapsToExactlyTheDescribedFollowUp` |
| 7 | TOCTOU: identity (dev, ino, mode, size, mtime; statx without following the final symlink) captured before the dialog and re-checked after it and before every start, including shared default-app launches | `FileIdentity`, `LinuxOpenPlan.StillValid` | `FileIdentity_DetectsChange` |
| 8 | Symlinks are resolved first; decision, dialog and exec use the target; a symlink swapped in later fails the identity check | `ResolveFinalTarget`, `FileIdentity.TryCapture` | `FileIdentity_DetectsChange`, launcher symlink test |
| 9 | Defense in depth inside the launcher: `OpenAsync` refuses executable MIME types (x-executable, x-pie-executable, x-sharedlib, appimage, x-desktop); no xdg-open fallback for files with an execute bit (also via symlink); `LaunchUriAsync` refuses `file:` URIs | `LinuxLauncherService` | `Open_ExecutableMimeTypes...`, `Open_ExecBit...`, `LaunchUri_RefusesFileUris` |
| 10 | More than 5 files opened at once asks first; files needing a gate are processed one by one, never in the bulk default-app launch | `OpenFilesLinuxAsync` | (UI path, covered by 1-9) |
| 11 | Dry-run seam (`FILES_LAUNCH_DRYRUN`) so automated runs spawn nothing | `DryRunProcessStarter` | n/a |
| 12 | Drop items onto an executable: same plan as gates 1-2 (only confirmable binaries/scripts); the dialog shows the full argv (target plus every dropped path, `DisplaySanitizer.FullArguments`, refused if too large) and exactly that argv is run after the identity re-check | `NavigationHelpers.RunWithItemsLinuxAsync` | `OnlyConfirmedActionsMayRunAFile`, `DisplaySanitizerTests` |
| 13 | KDE service menus: every invocation (including system menus) uses `LaunchDesktopConfirm` and the existing launcher plan/dialog. User menus never gain trust from their directory or execute bit. Strict group/key validation is shared with the desktop parser; Type=Service and each declared action's Name/Exec are required | `ServiceMenuParser`, `RunServiceMenuLinuxAsync`, `ExecutePlanAsync` | `ServiceMenuTests` |
| 14 | Service Exec: tokenize first, then substitute `%f/%F/%u/%U` only as complete argv elements; no shell or embedded target-code expansion. Explicit shells, shell syntax, unknown codes and oversized argv are refused. Every argv is shown through `DisplaySanitizer.FullArguments`, kept unchanged and identity-checked before its start; cancelling stops the remaining invocations | `DesktopExecExpander.ExpandServiceMenu`, `ServiceMenuLaunchPlan` | `Expansion_*`, `Plan_PinsCodeBeforeDialog_*` |
| 15 | Service discovery: user entries override system entries by basename (hidden/invalid overrides included); every selected MIME/protocol and URL-count restriction must match. Scan at most 512 directory entries and return at most 256 actions for at most 256 selected targets; each file uses the existing pinned regular-file reader's 64 KiB/1000-line/4096-byte-line/200-key limits. Final symlinks, devices and FIFOs are refused | `LinuxServiceMenuService`, `DesktopEntryDisplay.ReadLinesBounded` | `Scan_*`, `Filter_*` |

Service menus are read from XDG data directories' `kio/servicemenus` and legacy `kservices5/ServiceMenus`, including `~/.local/share`.
Actions enter the existing Linux **Show more options** flow (or render inline when the user enables that existing menu setting).
Localized `X-KDE-Submenu` groups are preserved. `X-KDE-Priority=TopLevel` entries sort first inside that flow, followed by `Important`, then normal entries.
MIME filtering includes aliases, parent types, type globs, and KDE's `all/all` / `all/allfiles`; required URL counts and protocol lists accept KDE comma lists as well as semicolons.
See [KDE's service-menu format](https://develop.kde.org/docs/apps/dolphin/service-menus/).

Compatibility limits: commands needing a shell, inline scripts containing target field codes, and extra KDE field codes such as `%D` are refused rather than interpreted.
For `%f`/`%u`, the existing dialog is shown separately for each selected target; `%F`/`%U` shows one command for the selection.
The desktop-file identity is captured around the bounded read at invocation and checked after confirmation and before every start; edits to the selected action since menu discovery require reopening the menu.
Selected files are arguments, not executable identities; as with Open with, an explicitly confirmed handler can itself interpret their contents.

## Review of default-open paths that could execute

- xdg-open fallback: only used when no default app exists, never for +x files, never for executable MIME types.
- `application/x-desktop` through `OpenAsync`: refused; real launches go through gate 3-5.
- Symlink to an executable: classified on the target; launcher guard follows symlinks for the mode check.
- Multi-file batch: plain files only; gated ones go through the single-file flow; each verified by identity before launch.
- "Open with" (user picks the app) and "Display" intentionally hand the file to an application. Those applications may themselves execute content (interpreters, IDEs with run-on-open). This is the user's explicit choice.

## Accepted residual risks (consciously left)

1. Race between the last identity check and the exec in the child process. Needs write access to the file's directory; `/proc/self/fd` execution is not possible through the launcher seam without re-spawning via a helper.
2. Hard-link / bind-mount tricks that keep the same inode and mtime but alter content (mtime can be reset by an attacker with write access; same privilege as above).
3. Default handlers that execute on open (misconfigured MIME defaults, e.g. a script interpreter registered for a text type). We trust the user's mimeapps configuration for non-executable MIME types.
4. `Terminal=true` runs through the detected terminal emulator's `-e` style; the displayed argv is the inner command, the terminal wrapper is shown as "(in a terminal)".
5. `TryExec` and `Path` are not honored for the confirm/launch path (the launcher never used `Path`; `TryExec` only matters for menu visibility). The dialog shows what is run; it does not claim a working directory.
6. Trusted `.desktop` files in `applications` dirs run silently, like GNOME. Anyone who can write there already has the user's privileges.
7. `.desktop` files whose Exec expands field codes with file paths (`%f`) are shown with an empty file list; opening them via this path never passes files.
8. No "remember this launcher" (xattr trust): always prompts.
9. Other callers of Win32-era open paths (recent files widget, toolbar) are not yet ported and not covered here (W-SYS/R1).
