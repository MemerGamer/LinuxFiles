# Plan: keep Uno, Rust backend, drop Windows code

Decision (user, 2026-10-09): keep Uno for the UI, move hot backend paths to Rust, and remove Windows-specific code from the independent fork. Supersedes the "Rust no-go until benchmark" verdict in `investigations/rust.md` only in direction; the benchmark gate still applies per component.

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

## Track 2: Windows code removal in the independent fork

The [upstream policy](upstream-policy.md) supersedes the earlier sync-preservation plan. Upstream stays read-only; fixes are cherry-picked selectively, with no upstream merges.

Already done: soft trim (compile exclusions, `#if WINDOWS`, analysis publish profile).

1. Stage 1: remove Windows leaf projects, their build references, Windows CI/packaging, and sync/pruning automation. Keep the desktop app and remaining tests building.
2. Later stages: remove Windows-only code inside `Files.App` after checking Linux consumers and build exclusions. Preserve shared assets needed by Linux, localization, licenses and notices.
3. Verify each stage with focused builds and tests; compare publish manifests when changing shipped assets (`compare-publish.py`).

Rollback: each stage remains a scoped, revertible change.

## Track 3: Parallel, not blocked

Pixelation diagnosis (1x/1.5x/2x), Wayland host spike decision, per `PLAN-alpha3.md` Wave 2.

## Order

alpha3 (Wave 1 merged) -> P0 benchmark + Track 2 publish verification -> P1 Rust enumeration + Track 2 Stage 1 -> alpha4 -> P2/P3 + later Windows removal -> alpha5.
Work split: Codex implements Rust crates and CI; Claude Sonnet verifies headless; cross-review before merge.
