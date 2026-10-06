# Threat model: root actions (Linux)

Root actions offers delete, rename and paste (copy or move). It stays hidden in virtual locations and in AppImage/Flatpak, and unless pkexec, the native helper and its policy are installed at trusted system locations. AppImage/Flatpak neither install nor execute a host elevation helper.

## Privilege boundary

The only elevated executable is `/usr/lib/linuxfiles/files-elevation-helper`, a small .NET 10 Native AOT console program. Native packages install it root-owned, mode 0755, with root-owned ancestors that are not writable by group/others. It has no setuid bit. The matching `packaging/linux/io.github.memergamer.LinuxFiles.root-actions.policy` requires `auth_admin` for an active session; other sessions are denied. Authorization is not retained (`auth_admin_keep` is never used).

Files executes the trusted absolute pkexec path with exactly one argument: the fixed helper path. It sends the confirmed plan on stdin, closes stdin, and reads JSON results capped at 512 KiB. It never executes rm, cp, mv, a shell, commands from JSON, or programs found through PATH. The subprocess starts with a cleared environment and LANG=C. pkexec removes dangerous loader variables before launching the AOT executable; the helper requires effective uid 0 and a non-root PKEXEC_UID, then clears its environment, sets umask 0077 and changes cwd to `/`. It takes no command-line switches and has no daemon, plugin loading, configuration files or test mode.

## Frozen confirmation and protocol

ElevationPlanPreview copies the plan into read-only collections and shows the exact operation, absolute sources and target encoded as JSON, together with the executable and the stdin marker. DisplaySanitizer escapes control, bidi and zero-width characters; oversized previews cannot be confirmed. Confirmation hands out that displayed plan once. Cancel, close and Escape do nothing; Cancel is the default button. Only one dialog can be open.

The application compares the complete serialized request with the displayed command data before sending it. Its path checks are UI preflight only. It never trusts unprivileged post-operation filesystem checks, including for destinations under `/root`.

The helper independently requires version 1, exactly the known JSON fields, unique property names, a known operation, 1–64 unique, non-overlapping sources, and normalized absolute paths with no NUL, empty components, dot components or root `/`. Rename carries an absolute target in the same parent; copy/move cannot target the source or its descendants or introduce duplicate destination names. Input is strict UTF-8, at most 64 KiB, with bounded JSON depth. Unknown/missing fields, trailing data and unsupported versions fail closed. Results have one ordered entry per source; malformed, missing, duplicate or mismatched results can never become success in the app.

## Descriptor-relative execution

After authorization, the helper opens `/` once and walks every ancestor with single-component descriptor-relative opens. It prefers openat2 with `RESOLVE_NO_SYMLINKS|RESOLVE_BENEATH`. Only ENOSYS permits fallback to the per-component `O_PATH|O_NOFOLLOW|O_DIRECTORY` walk. Other errors fail closed. Each opened ancestor must be root/caller-owned and not group/other-writable, except root-owned sticky directories. No path operation follows a user-controlled symlink.

The complete source trees and destination directory are pinned before mutation. Every entry is checked with statx before and after opening, requiring identity, mode, uid, size, timestamps and mount id. Sources are regular files or directories only: symlinks (including inside trees), special files and mount crossings are refused. Tree walks stop at 4096 entries or depth 64; descriptor exhaustion and enumeration errors are failures. Invalid UTF-8 names are refused, rather than converted into different names. The helper checks source/destination containment by descriptor identity as well as by path.

- Delete uses unlinkat relative to pinned parents, recursing only into pinned source directories. Identity and tree membership are checked again before deleting; absence is confirmed with a descriptor-relative statx lookup. Only ENOENT proves absence; permission/stat failures never do.
- Rename uses renameat2(RENAME_NOREPLACE) with the pinned parent and plain leaf names. Collisions, including dangling symlinks, fail atomically. No unsafe rename fallback exists. The renamed identity and source absence are checked inside the helper.
- Copy opens source files without following links, creates destination files with O_CREAT|O_EXCL and directories with mkdirat (existing names fail), and transfers bytes through descriptors. Copies are root-owned mode 0600, directories mode 0700, and carry no original ownership, setuid/setgid bits, timestamps, xattrs or hardlink relationships. Setuid/setgid source trees are refused. Size and SHA-256 of source and the helper's own created output are verified; completed output and its destination parent are fsynced and its directory membership and metadata are checked again.
- Move follows the same verified-copy path in the same authenticated helper process. It removes only the pinned originals, checking identity and file metadata again before removal. A skipped/colliding/failed/unverified copy never triggers source deletion. Each item reports its own result; failure does not erase another item's verified success.

Partial outputs remain in place and are reported as failures. The helper never performs recursive cleanup through an untrusted destination name. Partial deletion/move can leave a partially changed source tree; surviving originals remain, and the UI receives failure rather than success.

## Races and residual limits

An ancestor replaced by a symlink during polkit authentication is refused. An ancestor renamed/replaced after opening cannot redirect the operation: it acts on the original pinned directory, even if its pathname now displays another object. Destination collisions never replace existing objects; copy verification concerns the helper-created inode, not an unrelated file that happens to exist.

Authentication authorizes the absolute plan after the prompt; the app does not attest every source inode before authentication. Concurrent modifications to regular file contents or directory entries are rejected when detected by identity, metadata, membership and byte checks. Linux has no atomic compare-inode-and-unlink primitive and no snapshot of concurrently writable file contents: a writer retaining access can still race a final check, and operations are not a transaction. No symlink race can escape the pinned parents, and no unexpected verification failure is accepted. For move, modifications detected before deletion preserve the affected original; undetectable writes after its final check remain a limitation. Users should quiesce writers before requesting privileged moves.

Root authentication intentionally permits destructive actions on root-owned data. An administrator already able to replace the installed executable/policy or perform privileged mount operations is outside this boundary. Kernel/glibc with statx mount identity and renameat2 support are required; missing required facilities disable/fail the operation rather than weakening checks.

## Packaging and verification

AUR source packages AOT-publish the helper and install it and the policy at the fixed paths. Binary AUR packages use the helper in the release tarball. `publish.sh` includes it in `elevation-helper/`; AppImage/Flatpak explicitly remove it and set FILES_DISABLE_ROOT_ACTIONS=1 and add a .root-actions-disabled marker (the app also detects their runtime). `install-local.sh --install-root-helper` is explicit opt-in to a separate system installation through sudo; a default user-local install cannot supply a trusted helper. Policy/helper installation is never performed by the running app.

Non-root tests run HelperEngine directly against private temp trees, substituting the current uid for the elevated identity and the observed root-directory owner for root in uid-mapped sandboxes. Hooks exist only in that in-process API; stdin cannot enable them. Tests cover both openat2 and forced fallback, authentication-time and post-pin symlink swaps, source leaf races, nested copy/move, no-replace collisions, corrupt/modified copies, changed originals and partial batches. App tests cover protocol failures, forged plans, helper-only verification and packaging/trust gates. Real polkit authentication, root-only directories, mount races and distro package installation require a separate administrator review; automated tests never invoke pkexec or use the real display.

## Not implemented

Open terminal as root and edit as root remain outside this helper's protocol. Symlink copying, timestamp/permission preservation, transactional rollback and concurrent-writer snapshots are unsupported.
