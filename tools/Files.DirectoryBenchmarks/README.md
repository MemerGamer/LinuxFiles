# Linux directory benchmark spike

This console project is deliberately outside `Files.slnx`. It links the current
`PosixNative.cs` and `LinuxFileSystemEnumerator.cs` sources. `ELEVATION_HELPER`
only excludes unrelated directory-operation helpers from the linked Posix source;
the measured enumeration/stat methods are unchanged. The additional native
declarations live in `Files.Platform.Linux/Native/DirectoryBenchmarkNative.cs`
under `LINUX_DIRECTORY_BENCHMARK`; production builds compile none of that file.

Measured on 2026-10-10: **no-go**. Rust achieved only 1.017–1.199× scan
speedup over optimized C#, below the required 2×; application end-to-end gains
are unmeasured. The materialization/sort proxy improved by only 0.96–3.42%.
See the [investigation and median tables](../../docs/linux-port/investigations/rust-benchmark.md)
and [all 384 measurements](results/2026-10-10.csv). These implementations remain
isolated from the production backend.

From the repository root:

```bash
nice -n 19 python3 tools/Files.DirectoryBenchmarks/run.py \
  --output /tmp/linuxfiles-rust-benchmark.csv
```

The runner takes `flock ~/.cache/linuxfiles-agents/build.lock` through Python's
`fcntl.flock`, builds/tests serially with two workers at nice 19, builds the
dependency-free Rust crate offline, copies its `.so` beside the benchmark, and
runs without display variables. The lock file must already exist. Use
`--skip-rust` when Cargo is unavailable. An installed Cargo whose build fails is
an error, not an automatic omission. Neither project is wired into app packaging.

Defaults: 12 measured runs (minimum 10), three warmups, generated 10k/100k regular
files on `/tmp` (tmpfs on this host) and `.cache/rust-benchmark` (normal disk). Override parents with
`--tmpfs-parent`/`--disk-parent`; the runner checks their filesystems. Each run
creates private random fixture directories, writes varying 0–256 byte payloads,
and deletes only its own entries through held directory descriptors. Names include
hidden and Unicode files. Fixture creation, validation, checksums, console output
and forced GC happen outside timed intervals. Warmup primes metadata/page caches;
no cache dropping, CPU affinity or system settings changes are performed.

The `scan` workload includes open/close, directory enumeration, no-follow stat,
UTF-8 decoding, managed result allocation and Rust batch interop. Baseline,
optimized C# and Rust request the same statx mask `0x134B`: type/mode, UID, mtime,
inode, size and mount ID (device fields accompany statx). All return identical
managed entries. Both optimized candidates use a 64 KiB getdents64 buffer and
stack stat storage. Rust fills caller-owned batches of 1024 fixed 64-byte records
and a 64 KiB name arena; no per-entry callbacks or native allocations.

`scan-materialize-sort-proxy` adds the same managed full-path/DTO construction and
ordinal sort to each scan. It is **not app end-to-end time**: timestamps unavailable
from this stat mask have placeholders, and there is no icon/MIME work, natural
sorting, UI publication, layout, rendering or cancellation. The separately timed
`production-enumerator-controls` calls the actual async .NET service with hidden
files included. `current-dotnet-service` uses the app listing's default metadata
options; `current-dotnet-service-with-mode` also requests Unix mode (an additional
stat). Their work and result shape differ from `scan`; do not use them as the
gate denominator.

CSV includes every individual run and column-wise medians (even run counts average
the middle two values). Candidate order rotates each iteration; candidates are
serial. Wall time uses Stopwatch; allocation uses process-wide
`GC.GetTotalAllocatedBytes(true)` to cover async worker threads. Gen0/1/2 columns
are collection-count deltas during the interval. Managed byte counts exclude
Rust/native allocations: the scanner allocates one bounded buffer/handle per scan,
and those allocations are included in wall time. This is not an RSS/allocator
profile. The process uses the console runtime's default GC, reported in the CSV.

Before timing, the harness compares every name and PosixStat field, checks service
length/mtime/mode, checks ABI layout, and exercises directories, dangling/directory
symlinks, long names, Unicode, shell-like names and malformed getdents records.
Rust unit tests additionally check optional stat fields and name-arena backpressure.
The prototype fails a scan on stat errors; it does not implement the production
service's inaccessible-item skipping, recursion, symlink resolution or cancellation.
Invalid UTF-8 filenames are not a parity claim: current PosixNative decodes names
before re-encoding them for stat, whereas the optimized candidates stat raw names.
This limitation precludes treating the prototype as a drop-in backend.

Interrupting the runner terminates the foreground child; normal completion leaves
no running processes. The fixtures are cleaned in `Dispose` on ordinary failures.
Abrupt termination can leave private `linuxfiles-bench-*` fixtures; inspect and
clean only fixtures from your own invocation. No app launch is needed for this
console benchmark. Any future UI verification must use `headless-run.sh` with
`FILES_SANDBOX_DRIVES=scripts/linux/showcase-drives.txt`.
