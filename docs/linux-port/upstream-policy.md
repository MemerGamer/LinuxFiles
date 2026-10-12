# Upstream policy

LinuxFiles is now an independent Linux fork of [Files](https://github.com/files-community/Files). `main` is the integration branch. We no longer merge upstream branches or run an automatic sync workflow.

The `upstream` remote for `files-community/Files` stays read-only: fetch and inspect its history, but never push to it, target it with pull requests, or post comments there as part of work on this fork.

Bring over individual fixes selectively with `git cherry-pick -x <commit>`, on a fork branch. Review each change for Linux compatibility, keep its upstream attribution, and verify the affected build and tests before merging into this fork. Do not restore removed Windows projects or packaging as a side effect.

Preserve `LICENSE-MIT`, `LICENSE-MPL`, `NOTICE.md`, `.github/NOTICE.md`, applicable file headers, and Files Community contributor credit. MIT notices continue to apply to inherited MIT code; MPL terms continue to apply to files bearing MPL headers. Independence does not change those licenses or imply endorsement by the Files Community.

Stage 1 removes the Windows leaf projects and their build references, Windows CI and MSIX packaging, and the former sync/pruning automation. Windows code inside `Files.App`, including `#if WINDOWS` branches, remains for later stages.
