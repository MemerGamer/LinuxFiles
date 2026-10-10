# Track 2(a) + (b-prep): Windows leaf pruning

This stage prepares deletion; **no Windows sources are deleted in this PR**.
`windows-paths.txt` is an explicit allowlist, not a platform-name heuristic.
The default Linux graph uses `FilesWin32Compat=false` and `net10.0-desktop`.
Stage (a)'s existing compile/asset exclusions stay in place; stage (b)'s leaf
removals are simulated only in a disposable copy. App-internal deletion (c)
remains deferred until two clean upstream syncs.

## Reference audit (2026-10-10)

Each entry was searched by its directory/project name across `Files.slnx`,
all source/test project files and MSBuild props/targets, workflows, scripts,
Linux packaging and `flake.nix`. Then the remaining Linux graph was built
and tested with all candidates absent. The solution is the retained Windows
inventory; Linux CI, publishing and Nix target individual projects instead.
Windows solution entries and Windows workflow references are intentionally
retained here. A later deletion PR must address them if those tools are needed.

| Allowlisted directory | Tracked files | References outside the directory; Linux assessment |
|---|---:|---|
| `src/Files.App.BackgroundTasks/` | 2 | `Files.slnx`; app ProjectReference in the Windows-only ItemGroup. |
| `src/Files.App.Launcher/` | 6 | Windows solution project/dependency; Windows release workflows; app's Windows-only packaged launcher Content item. |
| `src/Files.App.OpenDialog/` | 20 | Windows solution projects/dependencies only; C++/Win32 dialogs. |
| `src/Files.App.SaveDialog/` | 22 | Windows solution projects/dependencies only; C++/Win32 dialogs. |
| `src/Files.App.Server/` | 8 | Windows solution/dependency; app's Windows-only CsWinRT inputs and `BuildFilesAppServer` target. |
| `src/Files.Platform.Windows/` | 3 | Windows solution; app ProjectReference in the Windows-only ItemGroup. |
| `tests/Files.App.UITests/` | 37 | Windows solution and the Windows-only Format XAML workflow. |
| `tests/Files.InteractionTests/` | 12 | Windows solution, CI and sideload workflows; Appium/Axe.Windows tests, Windows RIDs. |

Total: **110 files, 388,950 tracked bytes** at the audited base `4f24a930d`.
No candidate is referenced by Linux build/test/publish scripts or Linux
workflows. The platform test project's explicit linked source files and its
project references remain outside the allowlist. Linux test discovery's
`*.Platform.Tests` / `*.Linux.Tests` globs exclude both Windows test projects.

Reproduce the static search for each entry (also inspect the enclosing
MSBuild conditions and workflow runner, rather than treating hits as active):

```bash
for path in $(sed -e 's/\r$//' -e '/^#/d' -e '/^$/d' scripts/linux/windows-paths.txt); do
  name=$(basename "$path")
  rg -n -F "$name" Files.slnx src tests .github/workflows scripts packaging flake.nix \
    -g '*.sln*' -g '*proj' -g '*.props' -g '*.targets' -g '*.yml' \
    -g '*.sh' -g '*.ps1' -g '*.py' -g '*.nix' -g '!**/bin/**' -g '!**/obj/**'
done
```

Conservatively retained: CsWin32 (Linux opt-in compat inventory still builds
it), Controls/Storage/Shared/Core/Uno projects, all app-internal Windows files,
manifests/installers, shared project/solution/package files, local NuGet
archives and all assets. Windows file names alone do not establish exclusion.

## Pruning and sync behavior

Run from anywhere inside the checkout:

```bash
scripts/linux/prune-windows.sh          # reports tracked candidates; changes nothing
scripts/linux/prune-windows.sh --apply  # future deletion stage only: stages git rm
```

Paths are literal, repository-relative, one per line; a trailing slash denotes
a directory. Blank lines, comment lines and CRLF are accepted. Absolute paths,
traversal, globs and `.git` paths are rejected before any removal. Normal
pruning preflights the complete operation and refuses modified/staged files;
already absent paths are harmless. Untracked files are not removed. The CRLF
shell script normalizes its own input before execution on Linux.

The sync workflow snapshots the main-side allowlist and helper before merging,
so upstream cannot change the policy during that merge. On failure it invokes
`--apply --conflicts-only --paths-file <snapshot>`: only allowlisted `DU`
conflicts (deleted by us, modified by them) get `git rm`. Matching is exact for
files or bounded by the directory slash, and Git paths are read NUL-delimited.
If none remain, `git commit --no-edit` finishes the merge with both parents.
Any remaining conflict aborts the merge and enters the existing tracking-issue
flow. Reverse modify/delete (`UD`), content, add/add and non-allowlisted
conflicts stay for review. Clean merges retain the existing behavior;
upstream additions are not automatically pruned in this preparation stage.

## Disposable validation

Validation uses a fresh `git archive HEAD` copy initialized as its own Git
repository, with this PR's helper/list/workflow copied in. Baseline and pruned
publishes use the same temporary path and HEAD. Only that copy runs `--apply`.
Every build/test/publish runs under
`flock ~/.cache/linuxfiles-agents/build.lock`, at low priority, with MSBuild
node reuse disabled and at most two build workers. No real display is used.

Commands exercised in that copy:

1. `scripts/linux/publish.sh --rid linux-x64 --tarball` before removal.
2. Pruner dry run, then `--apply` in the temporary repository only.
3. Release x64 builds of Shared, Core.Storage, Core.SourceGenerator and
   Platform.Linux with warnings as errors; Controls, Storage and App for
   `net10.0-desktop` with compat off; platform tests with warnings as errors.
4. `scripts/linux/headless-run.sh` launches the rebuilt App with a sandbox HOME;
   its `run` action executes `dotnet test --test-modules Files.Platform.Tests.dll --root-directory <test-output>` on
   the same private Xvfb display, avoiding solution/Windows project discovery.
   The compiled test bundle and pruned source tree are copied beneath a private
   cache directory (source-reading tests locate `Files.slnx` beside it): the
   native-library trust tests correctly reject a world-writable `/tmp` ancestor.
5. `python3 -m unittest discover -s scripts/linux/tests -p test_prune_windows.py -v`
   and the existing `test_compare_publish.py` suite.
6. Repeat the self-contained publish, compare both tarballs using
   `scripts/linux/compare-publish.py --json <evidence-file>`, and verify that
   CsWin32 has no `bin` or `obj` output.

The new Git regression tests exercise dry-run/apply/idempotence, complete
preflight protection, invalid lists, and the actual workflow merge shell
block in isolated repositories: clean, allowlisted deletion, mixed,
non-allowlisted deletion, reverse deletion, content, add/add, and an upstream
policy change. They verify abort/review behavior and merge ancestry.

Validation results are recorded below after the disposable run completes.
