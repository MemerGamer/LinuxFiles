# Building and publishing the Uno fork packages

This directory contains an **artifact-only build workflow for MemerGamer/uno**, not a LinuxFiles workflow. Copy `wayland-packages.yml` into the fork's `.github/workflows/wayland-packages.yml` on `wayland/host`, replacing the existing GitHub Packages workflow. The fork must contain `build/wayland/build-local-packages.sh` and `prepare-desktop-nuspecs.py`; the workflow delegates to the existing builders with `--strict --tfm net10.0` and verifies all eight package identities, dependency versions and desktop assemblies. Preserve Uno's licenses and notices.

Revision 4 has not been built or pinned. Follow [the LinuxFiles pin update procedure](../wayland-packages.md#updating-the-committed-pins): **build packages -> compute sha256 -> commit pins -> publish release**. The workflow deliberately stops after uploading the Actions artifact, allowing the exact package bytes to be reviewed and pinned before publication.

1. Enable Actions and dispatch **Build Uno Wayland packages** against the reviewed `wayland/host` commit with version `6.7.135-wayland.4`. The workflow file must also be on the default branch to appear in the dispatch UI. No custom secrets, PAT or write permissions are needed for the build.
2. Download the `uno-wayland-VERSION` artifact. Verify its source commit and metadata, compute SHA-256 locally and commit the eight reviewed pins to LinuxFiles. Verify local import using `build/fetch-uno-wayland.sh --import /path/to/artifact`. Do not rebuild these packages after pinning.
3. From the artifact directory, publish those exact bytes using the maintainer's usual GitHub authentication. For revision 4 (replace the placeholder with the reviewed full Uno commit):

   ```sh
   sha256sum --check --strict SHA256SUMS
   gh release create uno-wayland-6.7.135-wayland.4 *.nupkg SHA256SUMS \
     --repo MemerGamer/uno --target REVIEWED_UNO_COMMIT \
     --title uno-wayland-6.7.135-wayland.4 --prerelease --latest=false
   ```

4. Verify all nine public assets with a clean anonymous LinuxFiles fetch and a fresh NuGet cache. Revision 3 packages must not be relabeled or repacked as revision 4.

Never overwrite a published package version. For host changes, build a new cohort from a reviewed commit, commit new pins and only then publish. If publication partially fails, inspect it; do not blindly rerun or clobber assets. Once packages have been publicly consumed, use a new version. Keep source tags/releases and license attribution available for redistributors.

The candidate workflow has only been statically checked in LinuxFiles. Maintainers must execute it in the fork and validate strict packing on a clean runner. This change does not modify the external repository, commit pins or publish packages automatically.
