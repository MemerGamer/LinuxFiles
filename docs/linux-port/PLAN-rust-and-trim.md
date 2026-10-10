# Plan: keep Uno, Rust backend, drop Windows code

Decision (user, 2026-10-09): keep Uno for the UI, move hot backend paths to Rust, and remove Windows-specific code without breaking the fork's upstream sync. Supersedes the "Rust no-go until benchmark" verdict in `investigations/rust.md` only in direction; the benchmark gate still applies per component.

## Track 1: Rust backend (`native/linuxfiles-core`, cdylib)

Boundary: C ABI, `LibraryImport` in a `Files.App.Linux.Native` wrapper, blittable structs, batch calls (never per-file calls across the boundary), cancellation token via an atomic flag. C# fallback stays until the Rust path is proven, selectable with `FILES_NATIVE=0`.

Components, in order (each needs a benchmark win: >=2x on its phase and >=20% end-to-end, else it stays C#):
1. Directory enumeration + stat (single `getdents64`/`statx` batch, sort keys precomputed).
2. Watcher (inotify with coalescing, replacing the 2 s poll fallback).
3. Archive list/extract (zip/tar/7z via Rust crates, byte progress, cancel).
4. Recursive size / search index.
5. Tag DB (rusqlite, batched writes), only if C# profiling shows it matters.

Packaging: cargo build in CI per target, `.so` bundled in AppImage/Flatpak/tarball, `rustPlatform.buildRustPackage` in the flake, AUR bin unchanged. Add `cargo test`, `cargo clippy`, `cargo deny` to CI.

Phases: P0 benchmark harness (C# baseline, 1 week). P1 enumeration spike behind flag. P2 watcher + archives. P3 default-on if gates pass. Release as alpha4+.

## Track 2: Windows code removal without breaking sync

Already done: soft trim (compile exclusions, #1 `#if WINDOWS`, analysis publish profile).

Hard deletion becomes viable with sync automation:
1. List Windows-only paths in `scripts/linux/windows-paths.txt` (projects, CsWin32, BackgroundTasks, Package manifests, Windows installers, Windows-only tests).
2. Daily sync workflow merges upstream, then for modify/delete conflicts on listed paths runs `git rm` (keep our deletion) and commits; all other conflicts still open a PR for review.
3. Shared files (Files.App.csproj, Files.slnx, Directory.Packages.props) stay, with Windows items conditioned, to keep conflict surface small.
4. Staged: (a) build/bundle exclusion verified by size comparison (`compare-publish.py`), (b) delete leaf Windows-only projects first, (c) delete Windows-only files inside Files.App last, after two clean syncs.
Rollback: every stage is one revertable PR. If a sync produces non-trivial conflicts twice in a row, stop at the previous stage.

## Track 3: Parallel, not blocked

Pixelation diagnosis (1x/1.5x/2x), Wayland host spike decision, per `PLAN-alpha3.md` Wave 2.

## Order

alpha3 (Wave 1 merged) -> P0 benchmark + Track 2(a) -> P1 Rust enumeration + Track 2(b) -> alpha4 -> P2/P3 + Track 2(c) -> alpha5.
Work split: Codex implements Rust crates and CI; Claude Sonnet verifies headless; cross-review before merge.
