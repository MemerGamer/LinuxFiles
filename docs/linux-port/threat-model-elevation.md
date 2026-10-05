# Threat model: root actions (Linux)

The "Root actions" context-menu submenu (like Dolphin's) offers delete, rename and paste (copy or move) as root. It is hidden in the Recycle Bin, archives, FTP and search results, and when `pkexec`, `rm`, `cp` or `mv` is missing from a trusted location.

## Design
- Every command runs as `pkexec <absolute program> <argv...>`, never through a shell. Programs are `rm`, `cp` and `mv` only (`mv` just for rename).
- **Trusted programs.** `$PATH` is never consulted. `SystemToolResolver` accepts `pkexec`, `rm`, `cp`, `mv` only from `/usr/bin` then `/bin`, after resolving symlinks, and only if the file is owned by root and not group/world-writable. The path used is the one chosen, so multi-call binaries keep their name. (Layouts such as NixOS, where `pkexec` lives in `/run/wrappers/bin`, are not supported and the menu stays hidden.)
- **Plan, show, run the same object.** `Plan*` returns an `ElevatedPlan` (operation, inputs, exact commands). The dialog shows those commands with `DisplaySanitizer.FullArguments`: one escaped argument per line, control/bidi/zero-width characters visible as escapes, never truncated, and refused when too large to show. `RunAsync(plan)` re-derives the plan from its inputs, compares it with the object that was shown, and refuses any difference (forged program or arguments, or a changed file system), then verifies the effect afterwards.
- polkit authenticates every command (`pkexec`'s default action). Files caches no credentials and keeps no root process. Exit 126 is reported as "dismissed"; 127 (could not start or authenticate) is an error.

## Filesystem gates (checked at plan time and again before running)
- Paths must be absolute and NUL-free; the root `/` is refused. Symlinks in parent directories are resolved first and the resolved path is what appears in the command and runs.
- **Directory trust (TOCTOU).** Every directory from `/` to the parent of each source, and to each destination, must be owned by root or the current user and must not be group/other-writable (root-owned sticky directories such as `/tmp` are accepted). Otherwise another user could swap a component between confirmation and execution, and the operation is refused. Residual window: the current user themselves can still swap things in their own directories between the dialog opening and `exec`, which only affects what that user already controls.
- **Copy and paste.** `cp -R -P -n --preserve=timestamps,links -- sources... dest/`. `-n` never replaces and, with the lstat pre-check that `dest/<name>` does not exist (including dangling or planted symlinks), prevents writing through a symlink. Ownership and mode are not preserved: copies belong to root and keep no setuid/setgid. Sources containing setuid/setgid files, or unreadable folders, are refused.
- **Move** is copy, then delete the originals (two polkit prompts). A move through `mv` would keep user ownership and setuid bits in a root-owned place. The delete only runs after the copies are verified to exist.
- **Delete** is one `rm -rf -- p1 p2 ...` for all selected items (one prompt).
- **Rename** takes a plain name (empty, `.`, `..`, or any `/` refused), uses `mv -n -T --`, and refuses when the target exists. Ownership does not change.
- `mv -n`/`cp -n` exit 0 when they skip, so after every run the effect is verified (target present, source absent) and a no-op is reported as failure.
- Error text from the tools, and every file name, is escaped before being shown.

## Residual risks
- The user can still delete or overwrite anything they authenticate for; the exact-command dialog is the control.
- Files owned by the user and placed in root-owned folders by copy become root-owned and editable only by root, but their content is the user's; that is the point of the feature.
- `cp -R -P` copies symlinks inside a tree as symlinks (not followed).
- Delete uses `rm -rf`: a path swapped to a symlink by the current user only removes the link.

## Not implemented (LINUX-TODO(root-actions))
- "Open terminal here as root": many terminals refuse to run as root and `pkexec` drops the environment (no display variables), so it needs per-terminal handling and detection.
- "Edit as root": needs sudoedit semantics (copy to a private temp file, edit as the user, copy back through the elevation service), with a check that the original did not change meanwhile.

## Confirmation integrity
The dialog is driven by `ElevationPlanPreview`: the plan is recomputed on every input change (rename), the confirm button is enabled only while a plan is valid and fully displayable, and `Confirm(shownText)` returns that very plan object only when the text on screen equals its display string. It returns the plan once (double click or Enter cannot confirm twice), ignores updates after confirming or closing, and never returns a plan that was too large to display. Cancel, close and Escape run nothing; the default button is Cancel. Only one root-action dialog can be open at a time.
