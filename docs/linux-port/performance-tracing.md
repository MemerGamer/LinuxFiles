# Slow-action tracing (issue #131)

Set `FILES_TRACE=1` before starting Files. Other values leave tracing disabled. The flag is read once per process;
disabled scopes do not allocate, read clocks, or queue records. Enabled tracing buffers at most 1,024 records,
drops records when full, and writes `[files-trace]` lines to stderr on a worker. Slow stderr must not stall the UI.
Buffered records can be lost at shutdown; this is diagnostic output, not an audit log.

For an authorised headless verification run, from the repository root:

```bash
FILES_TRACE=1 FILES_SANDBOX_DRIVES=scripts/linux/showcase-drives.txt \
  nice -n 19 scripts/linux/headless-run.sh -s 40 -o /tmp/files-perf-actions -a /tmp/perf-actions.txt
```

Use only the private Xvfb harness and its throwaway HOME/session bus. Never use the real display, real home,
real desktop settings, or host inotify-limit changes. The harness captures stderr in `app.log` and cleans up
its processes. Default dry-run launches do not measure real process spawning; use a disposable fixture program
in the sandbox if actual spawn measurements are needed.

Each record carries `id`, `parent`, `operation`, elapsed `ms` (monotonic clock), completion `thread`, optional
`ui` flag, and `outcome`. Parent IDs follow async execution context. A thread ID identifies the completion
thread, not a thread occupied throughout an await. No paths, arguments, selected names, bus addresses,
message bodies, credentials, or process output are included.

| Operation | Measured boundary |
| --- | --- |
| `context-menu-build` | Item/background flyout opening through model building and awaited extension loading; excludes visible-frame latency and deferred submenu loading. |
| `open-with-load` | MIME lookup, application query and default lookup on a worker; excludes UI icon decoding/population. |
| `open-with-query` | Registry worker query, including candidate resolution. |
| `parsed-file-cache` | File identity check plus parsing on a miss; outcomes `hit`, `miss`, `missing-or-unreadable`. |
| `service-menu-scan` | Directory scan, cached parsing and selection filtering; MIME lookup precedes this scope. |
| `tag-db-save` | Actual atomic database save attempt; unchanged tags/empty missing entries produce no save record. |
| `dbus-connect` | Session/system connection attempt, including its timeout. |
| `dbus.*` | Outbound call to reply/fault, grouped by platform feature. Caller timeouts can occur before this record; a call that never completes has no completion record. |
| `process-spawn` | `Process.Start` only, across platform runners. |
| `process-wrapper-exit` | Detached-launch wrapper wait, not external application readiness. |
| `folder-enumeration` | Batch enumerator lifetime, including consumer time between batches. |
| `folder-enumeration-batch` | Directory entries/metadata read on a worker, excluding consumer/UI rendering time. |

`completed-or-unwound` means the scope ended, including exception/cancellation paths; it does not claim success.
D-Bus records explicitly report `reply`/`fault`. Correlate caller logs to distinguish cancellations/timeouts.

Application, association and MIME mapping caches check device/inode/mode/size/nanosecond mtime on every use.
Atomic replacements, edits, removals and user overrides are visible on the next query without watcher resources
or an invalidation delay. Directory scans still discover newly installed entries, including nested application
entries. Caches are instance/culture specific and bounded to 4,096 parsed files each. KDE menu parsing uses
no-follow identities; launch-time strict parsing and identity checks remain independent of this display cache.
Open With populates on pointer entry or keyboard focus, with a localized loading placeholder, and queries run
off the UI thread. Tag edits use one ordered background write queue; failed database saves stay dirty for retry.

Watcher degradation is logged once per watcher, including the polling interval and exception type (or forced
polling), without a folder path. Existing post-operation refresh hooks also refresh polling-backed listings,
so app create/rename/paste/trash/restore actions need not wait for the normal two-second external-change poll.

## Pending private Xvfb checks

1. Repeat file and background right-clicks cold/warm, including mouse and keyboard Open With activation.
   Confirm the loading placeholder resolves, cancellation on closing/navigation is safe, applications/default
   labels remain correct, and registry work reports `ui=False`. Compare menu/query/scan timings (median/p95).
2. Seed sandbox application associations and KDE service menus; add, edit, atomically replace and remove them
   between queries. Confirm overrides, Hidden/NoDisplay, removal lists, localization, selection filtering and
   fresh execution-time validation still behave correctly.
3. Browse a large tagged/untagged fixture directory repeatedly. Confirm warm reads do not emit tag saves;
   edit/remove tags rapidly and reopen Files to check the final database/xattr values and write ordering.
4. Inject a test watcher factory using `FolderWatcherOptions.ForcePolling` (do not alter host limits). Verify
   one fallback log, prompt create/rename/copy/move/trash/restore updates after our operations, stable selection,
   no duplicate rows when the next poll arrives, and ordinary polling of external fixture edits. Check native
   watcher behaviour separately. The production app has no force-polling environment switch.
5. Use only private mock D-Bus services and a harmless disposable process fixture to confirm trace output for
   delayed replies, faults, spawns and wrapper exit. Compare tracing off/on responsiveness; verify off produces
   no trace records. Navigation should emit enumeration and batch timings; no record should expose fixture paths.

These checks were not run by the implementation agent. Xvfb software rendering does not establish real KDE
compositor/GPU performance or production D-Bus daemon latency.
