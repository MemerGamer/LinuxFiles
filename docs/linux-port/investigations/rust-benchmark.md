# P0/P1: directory metadata backend benchmark

This spike compares the current C# Posix enumeration/stat path, a buffered C#
implementation, and a Rust C ABI batch implementation. It is isolated from the
application, its packaging, and `Files.slnx`. The code is in
[`tools/Files.DirectoryBenchmarks`](../../../tools/Files.DirectoryBenchmarks/README.md)
and [`native/linuxfiles-core`](../../../native/linuxfiles-core/Cargo.toml).

The requested `docs/linux-port/PLAN-rust-and-trim.md` does not exist at base
`b72499374`. The gate used here comes from [rust.md](rust.md): Rust must improve
the targeted phase by **at least 2× over optimized C#**, and the application must
improve by **at least 20% end-to-end**, without correctness/cancellation or
packaging regressions. A faster native loop alone does not pass that gate.

## Status and decision

**Measured no-go for adoption.** On 2026-10-10 the shared build lock was acquired,
Rust built successfully, and all four 10k/100k tmpfs/disk fixtures completed.
Each candidate/workload/fixture has three warmups and 12 measured runs: **384
individual measurements and 32 median rows** in the [raw CSV](../../../tools/Files.DirectoryBenchmarks/results/2026-10-10.csv).

The **2× phase gate fails**: Rust's scan speedups over optimized C# are
**1.199×, 1.017×, 1.034× and 1.019×** (tmpfs 10k/100k, disk 10k/100k).
The **20% application end-to-end gate remains unmeasured and is not satisfied**.
Even the console materialization/sort proxy improves by only **0.96–3.42%**;
it is not credited as application evidence. Because the required phase gate
already fails on every fixture, no production integration or application A/B
experiment is justified by this spike. Keep C# as the default and retain these
isolated implementations solely as reproducible benchmark evidence.

## Executed method

- Linux x86-64, AMD Ryzen 7 5800X (8 cores/16 threads), kernel
  `7.2.9-zen1-1-zen`, .NET SDK `10.0.112` / runtime `10.0.12`, targeting Release builds.
  Rust/Cargo `1.99.0`; dependency-free crate, built offline with normal portable
  compiler settings (no `target-cpu=native`).
- Private generated fixtures of 10,000 and 100,000 regular files on `/tmp`
  (tmpfs) and the worktree filesystem (btrfs on `/dev/nvme0n1p2`). File sizes vary
  from 0 to 256 bytes; names include hidden and Unicode files. This is warm
  metadata enumeration, not a file-content throughput benchmark.
- Three untimed warmups, then 12 measured runs per candidate/workload/fixture.
  Candidate order rotates; runs are serial. Report column-wise medians. Setup,
  differential checks, result consumption, console output and forced GC are
  outside the stopwatch interval. The runtime's console default GC is used.
- Both optimized implementations read validated `getdents64` records in 64 KiB
  batches and use stack stat buffers. The current path calls the unchanged linked
  `PosixNative.ForEachName` and `TryStat`, including its per-item 256-byte managed
  allocation. libc already buffers current `readdir`: this is not a comparison
  against a kernel directory-read syscall per entry.
- All three scans open/close the directory, issue fd-relative no-follow statx
  with the same `0x134B` mask, decode UTF-8, and return the same managed entry
  list. Compare every name and all PosixStat fields before timing. Rust timings
  include batch interop and managed materialization; no unconsumed native-only
  traversal is credited as a speedup.
- Wall ms use Stopwatch. Allocated bytes use process-wide
  `GC.GetTotalAllocatedBytes(true)` (including async workers); GC values are
  Gen0/Gen1/Gen2 collection-count deltas inside the measured interval. Native
  allocations are excluded from managed bytes. Rust owns one bounded 64 KiB
  directory buffer and a handle per scan; caller-owned records/name buffers are
  included in managed allocations. Native allocation work is included in wall
  time; RSS/native allocator/syscall profiles are not measured.
- All builds/tests and the measurement series take the shared agents' build
  lock; processes run at nice 19, builds use two workers, MSBuild node reuse and
  compiler server creation are disabled. No display or desktop settings are
  used. This remains one machine and one warm-cache fixture family, rather than
  proof across cold caches, network filesystems or HDDs.

`scan-materialize-sort-proxy` additionally constructs the same full-path DTOs
and performs the same ordinal sort for all three candidates. This is a **console
proxy, not application end-to-end time**. The DTO's birth/access timestamps are
placeholders because the measured Posix mask does not supply them. No app icons,
MIME lookup, natural sorting, UI publication, virtualization, rendering or
navigation cancellation is exercised. These omissions apply equally to all
three candidates; they cannot establish the second gate.

The actual listing service uses .NET `FileSystemEnumerable`, not PosixNative.
The separate `production-enumerator-controls` measure the unchanged async
`LinuxFileSystemEnumerator` with the app's default metadata options, and also
with optional Unix-mode hydration (an extra lookup). Hidden files are included
in both. Those controls read additional timestamps and use another result shape,
so they are not equivalent phase comparisons or a gate denominator.

## Results

Measured on 2026-10-10. Every cell is a column-wise median of 12 runs after
three warmups; format is **wall ms / managed allocated bytes / Gen0/Gen1/Gen2**.
The [raw CSV](../../../tools/Files.DirectoryBenchmarks/results/2026-10-10.csv)
contains all runs and medians. The rounded per-run rows reproduce the reported
medians within 0.001 ms. CSV SHA-256 (normalized to LF, matching Git text storage):
`376b9c161652da044a6e39a3d78d082de03f69ff2f63327f213505bc4a7e2de7`. Working-tree files retain CRLF.

### Equivalent scan phase

| Storage | Files | Current Posix C# (ms / bytes / GC) | Optimized C# (ms / bytes / GC) | Rust batch (ms / bytes / GC) | Optimized C# ms / Rust ms |
|---|---:|---|---|---|---:|
| tmpfs | 10,000 | 12.977 / 5,982,656 / 0/0/0 | 13.541 / 3,248,056 / 0/0/0 | 11.294 / 3,313,616 / 0/0/0 | 1.199× |
| tmpfs | 100,000 | 134.367 / 54,572,808 / 2/0/0 | 112.670 / 26,638,208 / 0/0/0 | 110.837 / 26,703,768 / 0/0/0 | 1.017× |
| btrfs disk | 10,000 | 13.431 / 5,982,656 / 0/0/0 | 12.723 / 3,248,056 / 0/0/0 | 12.306 / 3,313,616 / 0/0/0 | 1.034× |
| btrfs disk | 100,000 | 148.260 / 54,572,808 / 2/0/0 | 127.053 / 26,638,208 / 0/0/0 | 124.683 / 26,703,768 / 0/0/0 | 1.019× |

### Materialization/sort proxy (not application end-to-end)

| Storage | Files | Current Posix C# (ms / bytes / GC) | Optimized C# (ms / bytes / GC) | Rust batch (ms / bytes / GC) | Rust time reduction vs optimized C# |
|---|---:|---|---|---|---:|
| tmpfs | 10,000 | 13.615 / 8,885,112 / 0/0/0 | 12.511 / 6,150,512 / 0/0/0 | 12.391 / 6,216,072 / 0/0/0 | 0.96% |
| tmpfs | 100,000 | 148.923 / 83,070,344 / 3/1/0 | 132.816 / 55,135,744 / 1/0/0 | 128.274 / 55,201,304 / 1/0/0 | 3.42% |
| btrfs disk | 10,000 | 15.246 / 10,565,112 / 0/0/0 | 14.224 / 7,830,512 / 0/0/0 | 14.008 / 7,896,072 / 0/0/0 | 1.52% |
| btrfs disk | 100,000 | 172.804 / 99,870,344 / 4/2/0 | 153.852 / 71,935,744 / 2/1/0 | 151.480 / 72,001,304 / 2/1/0 | 1.54% |

### Production enumerator controls (different work; not gate denominators)

| Storage | Files | Default metadata (ms / bytes / GC) | With Unix mode (ms / bytes / GC) |
|---|---:|---|---|
| tmpfs | 10,000 | 16.591 / 4,363,912 / 0/0/0 | 30.030 / 4,029,352 / 0/0/0 |
| tmpfs | 100,000 | 153.113 / 34,948,208 / 1/0/0 | 258.568 / 34,948,208 / 1/0/0 |
| btrfs disk | 10,000 | 19.921 / 5,229,352 / 0/0/0 | 35.697 / 5,229,352 / 0/0/0 |
| btrfs disk | 100,000 | 217.020 / 51,748,208 / 2/1/0 | 375.102 / 51,748,208 / 2/1/0 |

### Gate application

| Required condition | Evidence | Outcome |
|---|---|---|
| ≥2× targeted phase over optimized C# | 1.017–1.199× across the four scan fixtures | Fail |
| ≥20% application end-to-end over optimized C# | No application A/B measurements; proxy reduction only 0.96–3.42% | Unproven; not passed |
| Both conditions met | Phase fails on every fixture | **No-go** |

Speedups use **optimized C# median ms / Rust median ms**. Time reduction uses
**(1 − Rust median ms / optimized C# median ms) × 100%**. The allocating Posix
baseline and non-equivalent production service are not used as denominators.
Rust lowers managed allocation versus the allocating baseline but allocates
65,560 bytes more per scan than optimized C# for its caller-owned record buffer.
It does not show a meaningful time gain on the 100k fixtures.

These are low-priority wall-time observations on a shared machine, without CPU
pinning or isolation from other host activity. The tmpfs 10k optimized scan median
is slower than its proxy median despite the proxy's additional work; separate
series can vary with scheduling, caches and runtime state. Results must not be
interpreted as precise language-only costs or generalized to other machines.
The conclusion is limited to failure to demonstrate the required gain here.

## Correctness and scope

The C ABI returns fixed 64-byte `repr(C)` records, explicit counts and name-arena
offsets/lengths. The arena preserves raw filename bytes; C# decodes only at the
managed boundary. The caller owns result buffers, and paired open/close operations
own the scanner and descriptor. Calls on a handle are exclusive. Status is zero
or positive errno; panics are contained as EIO, with no unwind across C. Invalid
non-null pointers and stale handles remain caller contract violations. The
[header](../../../native/linuxfiles-core/linuxfiles_core.h) documents layout,
lifetimes, errors, EOF and backpressure. All Rust unsafe code is confined to
libc/exported FFI boundaries; parsing/scanning logic uses checked safe slices.
C# imports use source-generated `LibraryImport` under the benchmark-only define
in [`Native`](../../../src/Files.Platform.Linux/Native/DirectoryBenchmarkNative.cs).

The differential fixture exercises hidden/Unicode/long/shell-like names,
directories, directory symlinks and dangling symlinks without executing names.
Both parsers reject malformed directory lengths and invalid names. Rust unit
checks exercise ABI layout, optional stat fields and an undersized name arena
followed by a successful retry without losing the entry. Generated entries are
removed fd-relatively through the held fixture directory; no recursive deletion
of an arbitrary existing directory is performed.

This is not a drop-in enumerator: recursion, inaccessible-item skipping, resolved
link targets, complete timestamp hydration and cancellation are not implemented.
Invalid UTF-8 filename parity is not claimed: current PosixNative decodes then
re-encodes names for stat, while both optimized variants stat raw bytes. ARM64,
AOT/trimming and package loading on supported distributions remain unverified.
Windows paths, application resources, dependency versions and solution membership
are unchanged.

## Reproduction and verification

```bash
nice -n 19 python3 tools/Files.DirectoryBenchmarks/run.py \
  --runs 12 --output /tmp/linuxfiles-rust-benchmark.csv
```

The runner validates tmpfs versus normal disk, serializes Cargo tests/Release
build, C# Release build and measurements under
`~/.cache/linuxfiles-agents/build.lock`. It supports `--skip-rust` for an
unavailable Cargo; the recorded run measured all three candidates. See the harness
README for options and measurement boundaries.

Verification from this run:

- Cargo offline/locked unit tests: **3 passed**, followed by a successful portable
  Release `cdylib` build.
- C# benchmark Release build: **0 warnings, 0 errors**. Differential ABI/name/stat
  checks passed, including directories, symlinks, hidden/Unicode/long/shell-like
  names and malformed directory records. All four fixture checks and all 12-run
  series completed; generated fixtures were removed normally.
- Raw CSV validation: exactly 32 candidate/workload/fixture groups, runs 1–12 in
  each, with all column-wise medians verified against the per-run rows.
- Platform test-project Release/x64 build (including the Linux platform and
  elevation helper): **0 warnings, 0 errors** with warnings treated as errors.
  Platform test run: **1,149 passed, 0 failed, 0 skipped**.
- Main desktop `net10.0-desktop` Release/x64 build: **succeeded, 586 warnings,
  0 errors**. All verification ran serially under the shared build lock, with
  node reuse and shared compilation disabled. No app was launched or real display
  used. The lock briefly remained held by an idle compiler server from another
  completed build; graceful `dotnet build-server shutdown --vbcscompiler` released
  it before this verification began.

The additional verification commands (under one `flock` acquisition) were:

```bash
flock ~/.cache/linuxfiles-agents/build.lock bash -c '
  set -e
  export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
  unset DISPLAY WAYLAND_DISPLAY
  nice -n 19 dotnet build tests/Files.Platform.Tests -c Release -p:Platform=x64 \
    -nodeReuse:false -m:2 -p:UseSharedCompilation=false -p:NuGetAudit=false \
    -warnaserror -v:quiet -clp:ErrorsOnly
  nice -n 19 dotnet test --project tests/Files.Platform.Tests/Files.Platform.Tests.csproj \
    --no-build -c Release -p:Platform=x64
  nice -n 19 dotnet build src/Files.App -f net10.0-desktop -c Release -p:Platform=x64 \
    -nodeReuse:false -m:2 -p:UseSharedCompilation=false -p:NuGetAudit=false \
    -v:quiet -clp:ErrorsOnly
'
```

No Rust backend is enabled in production. Application end-to-end A/B performance,
cancellation/recursion parity, ARM64, AOT/trimming and distribution/package loading
remain unverified. The phase gate already fails, so these are not deferred steps
toward a claimed go. Any future UI experiment must use
`scripts/linux/headless-run.sh` with the synthetic drive fixture and private Xvfb.
