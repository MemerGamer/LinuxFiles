# Linux security and robustness review — October 2026

Reviewed 2026-10-12 at `09d886fd6`. Code was not modified; this report is the only tracked change. Scope: Linux platform file operations, trash, archive extraction, tags/xattrs, launching/Open With/service menus, thumbnails, D-Bus integrations; elevation helper, polkit/root-mode flow; Linux packaging and installation scripts. Read `CLAUDE.md`, `docs/linux-port/AGENT-RULES.md`, fork policy and existing elevation/launching threat models.

Priorities: P0 critical immediate exposure; P1 command execution or significant data loss; P2 bounded security/robustness defects requiring particular inputs or environments; P3 lower-impact compatibility failures. Found **4 P1, 5 P2 and 1 P3**; no P0 identified. “Reproduced” means an isolated non-root probe against the built assemblies, using only disposable `/tmp` trees. Source-supported findings have not been dynamically reproduced. This is a scoped review, not a claim that every input or race has been exhausted.

## Findings

### R1 — P1: Open With expands filenames into nested shell code

**Location:** `src/Files.Platform.Linux/Launching/DesktopExecExpander.cs:272` (especially line 280); `src/Files.Platform.Linux/Launching/LinuxLauncherService.cs:107`.

`Quote` tracks the desktop Exec string's quoting, but not the quoting inside a shell's `-c` script. For an Exec value `sh -c "viewer \"%f\""`, expansion inserts a single-quoted filename inside the script's double quotes. The outer tokenizer consumes the escaping of `$`/backticks. The resulting script resembles `viewer "'/tmp/$(printf INJECTED).txt'"`: single quotes inside double quotes are literal, so command substitution executes. A downloaded ordinary file with a substitution in its name can therefore execute commands when opened through an otherwise trusted shell-based default handler/Open With entry. No executable bit or malicious desktop file is needed; the handler must have this nested-quote form.

**Evidence:** Reproduced with `/bin/sh` and harmless `printf INJECTED`. The expanded script contained the literal `$(printf INJECTED)` filename; shell output instead contained `INJECTED`. Existing `DesktopExecInjectionTests` cover unquoted placeholders inside scripts, not this nested case. Service-menu expansion uses a separate restrictive path; this finding concerns `Expand`.

**Suggested fix:** Tokenize the desktop Exec before substituting filenames and keep targets as argv data. Reject field-code substitution into shell script arguments; support a safe explicitly defined form passing targets as positional arguments to a fixed script (`sh -c 'viewer "$1"' sh <target>`). Do not attempt to fix arbitrary nested script syntax with another escaping pass. Add nested double/single quotes, substitutions and backticks to launch tests.

### R2 — P1: Archive merge can delete/overwrite outside the extraction destination

**Location:** `src/Files.Platform.Linux/Archives/LinuxArchiveService.Extract.cs:318`, `:345`, `:349`, `:364`, `:366`, `:368`.

The staged extraction validates entry names, but final merge classifies destination entries and then uses absolute pathname operations. It neither pins destination ancestors nor reopens them with no-follow checks. Suppose `dest/sub/victim` conflicts with an archive entry. While the conflict dialog is open, a writer renames `dest/sub` and replaces it with a symlink to an outside directory containing `victim`. Choosing overwrite calls `File.Delete(dest/sub/victim)` and `File.Move` through the new ancestor, destroying the outside file and publishing archive bytes there. The symlink check at line 328 applies only to source symlink entries, not ordinary files. A shared writable destination permits another user to race this; a concurrent same-user process suffices in a private destination.

**Evidence:** Reproduced using `ResolveConflict` to make the ancestor swap at the dialog boundary. Extraction reported success and the outside file changed from `OUTSIDE` to `ARCHIVE`. This is distinct from lexical zip-slip: `../`, absolute names and staged symlink traversal are already rejected.

**Suggested fix:** Pin the destination root and recursively merge through `DirectoryHandle` children with `O_NOFOLLOW`; use descriptor-relative stat, delete and rename, including keep-both publication and directory replacement. Keep handles across conflict callbacks and revalidate identities where replacement semantics require them. Add the callback-boundary ancestor-swap test.

### R3 — P1: Empty Trash follows a symlinked home trash container

**Location:** `src/Files.Platform.Linux/Trash/LinuxTrashService.cs:184`, `:189`, `:654`, `:754`; related pathname deletion/permission retry at `:890` and `:916`.

The home trash is accepted without checking `Trash`, `files` or `info`; `IsTrusted` immediately returns true for it. If `$XDG_DATA_HOME/Trash/files` is a symlink to Documents, `EmptyAsync` enumerates Documents and deletes its entries even when there are no `.trashinfo` records. This requires a pre-existing bad layout or a process able to change that container; it is a destructive robustness defect, not a new privilege escalation. Pathname deletion and `MakeTreeWritable` also leave ancestor/chmod races after checks for topdir trash.

**Evidence:** Reproduced with a private DataHome and fake mount resolver exposing no real mounts. `Trash/files` pointed to a disposable outside directory; its `important` file was deleted by `EmptyAsync`.

**Suggested fix:** Validate home trash ownership, type and permissions as well as topdir trash. Pin the trash root and open `files`/`info` without following links before enumeration. Use the existing descriptor-relative deletion and permission helpers for emptying and permanent deletion, rather than path-based chmod/retry. Deliberate relocation should use a separately validated configuration, not implicitly trust container symlinks.

### R4 — P1: Cross-device moves delete originals changed after copying

**Location:** `src/Files.Platform.Linux/FileOperations/LinuxFileOperationsService.Copy.cs:309`; `src/Files.Platform.Linux/FileOperations/LinuxFileOperationsService.Move.cs:77`, `:79`, `:140`; `src/Files.Platform.Linux/Native/PosixFileOperationsNative.cs:19`.

Copy verification checks only output length and the source's current size. `VerifyMoveSource` then calls `SameEntry`, which compares inode/device/type, excluding size, mtime and ctime. An editor can update the same inode after copying finishes but before deletion. The final check passes, the updated original is unlinked and only stale copied bytes survive. Same-size edits during the copy also evade the size-only verification. This affects ordinary cross-device moves and directory merges using that path; it does not describe the helper's stronger metadata/hash verification.

**Evidence:** Reproduced by forcing the cross-device path and changing `OLD` to `NEW` in `BeforeDeleteEntry`. Result was `Succeeded`, original absent, destination still `OLD`. The hook deterministically simulates a concurrent editor.

**Suggested fix:** Carry the opened source's identity, size, nanosecond mtime and ctime through publication and recheck the pinned inode and source name immediately before unlink. Verify copied content against the source and preserve the original on detected changes. Use the helper's content/metadata checks as a reference while acknowledging the unavoidable final check/unlink race; the present gap accepts changes made before the final check.

**Remediation and threat model:** Cross-device deletion protects against accidental concurrent modification, such as an application saving during a move: data differing from the copied version must be retained. Before each quarantine rename, fstat of the held descriptor must match the copied identity, size, mtime and ctime; writes bump ctime, which a writer cannot restore by setting mtime. Only after that check passes may post-rename verification accept rename's ctime update, while still requiring size, mtime, device/inode and link count to match. Immediately before unlink, recheck the held version and lstat the quarantine name against its device/inode. Unsupported atomic rename uses the same final held-fd/version and original-name identity checks. Verification failure retains or restores the source without overwriting a newly created original name. Regular-file tag re-keying runs only after successful deletion.

A hostile process running as the same user and racing the quarantine name is outside this threat model. POSIX offers no unlink-by-inode; a writer using an open descriptor can still write in the final stat/rename or stat/unlink window. These checks reject observed changes, but cannot guarantee exclusion of writes during those unavoidable syscall gaps.

### R5 — P2: Direct thumbnail cache hits bypass image validation

**Location:** `src/Files.Platform.Linux/Thumbnails/LinuxThumbnailService.cs:179`, `:239`, `:249`, `:259`; contrast external output validation at `:481`.

`TryReadValid` validates byte length and text metadata, then returns cache bytes without checking dimensions or decoding. The current-size bucket therefore bypasses protections applied to generated images and borrowed cache buckets. A corrupt cache file with matching URI/mtime is accepted repeatedly; a small compressed PNG with enormous decoded dimensions reaches the UI decoder without the thumbnail bucket cap. This requires control/corruption of the shared per-user thumbnail cache, not merely an arbitrary source image. The size check and subsequent `ReadAllBytes` also use separate opens, so growth/replacement defeats the read cap.

**Evidence:** Reproduced acceptance of a tiny deliberately invalid PNG declaring 100000×100000 pixels, carrying the matching URI and mtime. No large allocation or UI decoding was attempted.

**Suggested fix:** Read from one pinned regular-file descriptor through a hard byte cap, check IHDR dimensions against the bucket before decoding, and decode/reencode before returning direct hits. Apply the same bounded read to external output. Ignore/remove invalid entries instead of repeatedly returning them.

### R6 — P2: Tar hardlinks become incorrectly targeted symbolic links

**Location:** `src/Files.Platform.Linux/Archives/ArchiveSource.cs:190`, `:198`; `src/Files.Platform.Linux/Archives/LinuxArchiveService.Extract.cs:141`, `:151`.

`ArchiveSource` maps both tar hardlinks and symlinks to `LinkTarget` without retaining their type. Extraction always creates a symbolic link relative to the link's parent. Tar hardlink names are relative to the archive root. A tar containing regular `base` and hardlink `dir/alias` → `base` should produce another link to root `base`; it instead produces `dir/alias` → `dir/base`, yielding a dangling link or linking to unrelated content. Extraction still reports success.

**Evidence:** Reproduced with `TarWriter`: successful extraction, `LinkTarget == "base"` on `dir/alias`, reading alias throws `FileNotFoundException` although root `base` exists.

**Suggested fix:** Preserve link kind in `EntryData`. Resolve hardlink targets against the staged archive root, validate and pin regular targets, and create descriptor-relative hardlinks after targets are extracted (or explicitly skip unsupported hardlinks and count/report them). Never silently reinterpret a hardlink as a parent-relative symlink.

### R7 — P2: Compatibility rename fallback silently loses no-replace semantics

**Location:** `src/Files.Platform.Linux/Native/PosixFileOperationsNative.cs:68`, `:78`, `:121`, `:131`.

When `renameat2(RENAME_NOREPLACE)` is unavailable/unsupported, directories and entries that cannot be hardlinked use a separate destination stat followed by plain `renameat`. Another creator can publish a file/empty directory between them; the fallback overwrites it despite a no-replace/keep-both/restore contract. This is reachable on filesystems without rename flags and through the non-hardlink fallback (e.g. filesystems refusing hardlinks). The comment acknowledges the race, but callers receive success without knowing collision safety was weakened.

**Evidence:** Source-supported; the exact native-call race was not reproduced. The privileged helper correctly fails closed instead of using this fallback.

**Suggested fix:** Fail unsupported atomic no-replace moves with a specific error rather than invoking overwriting rename. Where appropriate, offer a copy/publication route that can preserve collision safety; otherwise require an explicit replace decision. Test a destination created after the absent check.

### R8 — P2: Trash metadata reads are unbounded and can open special files

**Location:** `src/Files.Platform.Linux/Trash/LinuxTrashService.cs:386`, `:420`, `:450`.

Listing reads `.trashinfo` with `File.ReadAllText`, which follows links and does not require a regular file or size cap. A `.trashinfo` symlink to `/dev/zero`, paired with an existing trashed entry, causes an unbounded read before parsing; a FIFO can block listing. Restore has a no-follow regular-file open, but still uses unbounded `ReadToEnd`. A very large metadata file can exhaust memory even though legitimate content is only a path and date. A corrupt/imported or attacker-prepared trash layout is sufficient; ownership checks on the container do not validate children.

**Evidence:** Source-supported; device/FIFO exhaustion was intentionally not executed.

**Suggested fix:** Use `PosixNative.OpenFileAt` for listing/date reads too, reject oversized metadata via the descriptor, and stream no more than a small fixed limit (e.g. 64 KiB) with cancellation. Apply the same cap to restore; reject excess bytes instead of parsing a truncated prefix.

### R9 — P2: Checked-in AUR recipes permit unverified privileged payloads

**Location:** `packaging/linux/aur/linuxfiles/PKGBUILD:17`; `packaging/linux/aur/linuxfiles-bin/PKGBUILD:29`; helper installation at source recipe `:41` and binary recipe `:43`.

Both checked-in recipes use `sha256sums=('SKIP' ...)`. Building directly from these recipes accepts substituted tag/release assets without makepkg detecting a mismatch; the package installs their native privileged helper and matching policy. An asset compromise/replacement can therefore enter the root-authorized helper trust boundary. `gen-aur.sh` fills checksums for the release-generated binary recipe, so this finding does **not** apply to a correctly generated/pinned release recipe. The source recipe has no corresponding checksum-generation step here.

**Evidence:** Source-supported; no downloads or installation performed.

**Suggested fix:** Keep checked-in consumable recipes pinned to real release checksums. If they are templates, make template usage fail and require generation of a checked recipe; extend generation to the source recipe. Validate that published recipes contain no `SKIP` entries before release.

### R10 — P3: Local install prefixes are not escaped for desktop Exec or sed

**Location:** `scripts/linux/install-local.sh:36`.

The script inserts `$prefix/bin/files` directly into a sed replacement and unquoted desktop Exec. A prefix containing spaces installs successfully but its launcher is split into a different executable and arguments. A prefix containing `&` causes sed to reinsert the matched `Exec=files`; `|` breaks the replacement expression. This is not shell interpolation execution, but valid path characters corrupt the generated launcher and can select the wrong executable.

**Evidence:** Source-supported; no real-home installation performed.

**Suggested fix:** Generate desktop entries with literal replacement in Python and quote/escape the executable according to desktop Exec rules (including backslashes, quotes and percent signs). Normalize the prefix to an absolute path. Test space, ampersand, delimiter, quote and percent characters using a private staging prefix.

## Boundary checks and limitations

No additional demonstrated helper privilege-boundary bypass was found. The reviewed helper independently validates the bounded request, clears its environment, uses no shell, pins source trees, refuses symlinks/special files/mount crossings, hashes copies and uses atomic no-replace rename. Its documented authorization grants root access to approved paths; manually invoking it still requires polkit authorization. Root-mode is an unprivileged helper-enabled mode; uid-0 startup redirects settings and disables single-instance registration. Existing explicitly accepted root-terminal/administrator-trusted-installer behavior was not relabeled as a vulnerability.

Tags/xattrs have bounded read buffers and a fixed `user.xdg.tags` key; native calls follow symlinks, so this review does not establish no-follow tag semantics. Service menus prepare argv separately and refuse shell expansion. Reviewed D-Bus calls use typed serialization and fixed service names; portal/UDisks signal matches constrain the sender. No demonstrated D-Bus command injection was found. Real polkit authorization, root-only access, filesystem/mount races and native package installation remain unverified. No dependency vulnerability assessment was possible with unavailable feeds.

## Validation

- Initial single-node test-project build failed with NU1900 because vulnerability feeds were unreachable. `dotnet build tests/Files.Platform.Tests -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false` succeeded with zero warnings/errors. Audit was disabled only on this invocation, without changing project/dependency files.
- Launching the built test DLL directly through Microsoft.Testing.Platform was blocked by sandbox socket permissions (`SocketException (13)` creating its control pipe). No official suite result is claimed.
- A temporary direct reflection runner invoked existing `DesktopExecInjectionTests`, `ElevationHelperTests`, `FileTagsDatabaseTests` and `FileTagsHelperTests`, including data rows and per-test initialize/cleanup: **69 passed, 0 failed, 2 inconclusive**. This bypasses the test-host IPC; it is narrower than running the full suite.
- `python3 -B -m unittest discover -s scripts/linux/tests -p 'test_*.py'`: **19 passed**.
- Temporary probes reproduced R1–R6 against built platform assemblies. All writes were confined to `/tmp`; R2/R4 used existing callback/hook seams for deterministic race timing. R5 established acceptance only, not decoder exhaustion. Source-supported R7–R10 were not dynamically reproduced.
- Uno desktop build/run and UI verification were not performed, as instructed. No user's desktop, settings, real trash or session bus was accessed. No commit made.
