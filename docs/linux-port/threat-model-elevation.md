# Threat model: root actions (Linux)

The "Root actions" context-menu submenu (like Dolphin's) offers delete, rename and paste as root.

## Design
- Every operation is one `pkexec <absolute program> <argv...>` run by `PkexecElevationService`. The program is one of `rm`, `cp`, `mv`, resolved to an absolute path from `$PATH` by `IExecutableLocator`. No shell is involved and nothing is concatenated into a command string.
- polkit authenticates every call (`pkexec`'s default action). Files never caches credentials or keeps a root process around; a dismissed prompt (exit 126/127) is reported as dismissed, not as an error.
- Before anything runs, the confirmation dialog shows the exact command (`ElevatedCommand.DisplayText`, produced by the same `Plan*` method the run uses, so the text cannot drift from what executes). Rename shows it live while the name is edited.

## Gates
- Paths must be absolute, NUL-free, and are normalized (`..` resolved). The filesystem root is refused for delete, move, copy-source and rename.
- Rename takes a plain name: empty, `.`, `..`, or anything containing `/` is refused, so a rename cannot move an item.
- All options precede `--`, so a file named `-rf` is a path, not an option. `mv` uses `-n` so an existing item is never silently replaced.
- Paste reads the system clipboard list (absolute local paths), so the user sees every source in the dialog.

## Residual risks
- The user can still choose to delete or overwrite anything they authenticate for; the dialog is the control.
- Time-of-check/time-of-use: a path could be swapped between the dialog and `pkexec` running; the window is the length of the polkit prompt.
- `cp -a`/`mv` follow the semantics of coreutils for symlinks inside copied trees (preserved as links, not followed).

## Not implemented (LINUX-TODO(root-actions))
- "Open terminal here as root": many terminals refuse to run as root and pkexec drops the environment (no display variables), so it needs per-terminal handling and detection.
- "Edit as root": needs sudoedit semantics (copy to a private temp file, edit as the user, copy back through `PkexecElevationService`), with a check that the original did not change meanwhile.
