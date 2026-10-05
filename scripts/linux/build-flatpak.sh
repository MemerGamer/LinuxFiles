#!/usr/bin/env bash
# Build the Flatpak from artifacts/files-<rid>.tar.gz (run scripts/linux/publish.sh --tarball first).
# Usage: scripts/linux/build-flatpak.sh [--bundle]   (--bundle also writes artifacts/Files-x86_64.flatpak)
# Without flatpak-builder the manifest is only validated.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
app_id="io.github.memergamer.LinuxFiles"
manifest="$root/packaging/linux/flatpak/$app_id.yml"
bundle="false"; [[ "${1:-}" == "--bundle" ]] && bundle="true"

if ! command -v flatpak-builder >/dev/null; then
  echo "flatpak-builder not found: validating manifest structure only" >&2
  python3 - "$manifest" <<'PY'
import sys, re
t = open(sys.argv[1]).read()
for key in ("app-id:", "runtime:", "runtime-version:", "sdk:", "command:", "finish-args:", "modules:"):
    assert re.search(r"^" + key, t, re.M), "missing " + key
print("manifest structure OK")
PY
  exit 0
fi

[[ -f "$root/artifacts/files-linux-x64.tar.gz" ]] || { echo "Missing artifacts/files-linux-x64.tar.gz (publish.sh --tarball)" >&2; exit 1; }

# Runtime/SDK must already be installed (flatpak install flathub org.freedesktop.Platform//25.08 org.freedesktop.Sdk//25.08).
nice -n 19 flatpak-builder --disable-rofiles-fuse --force-clean \
  --state-dir="$root/.cache/flatpak-builder" --repo="$root/artifacts/flatpak-repo" \
  "$root/artifacts/flatpak-build" "$manifest"

if [[ "$bundle" == "true" ]]; then
  flatpak build-bundle "$root/artifacts/flatpak-repo" "$root/artifacts/Files-x86_64.flatpak" "$app_id"
  echo "Bundle: $root/artifacts/Files-x86_64.flatpak"
fi
