#!/usr/bin/env bash
# Point flake.nix at a published release.
# Usage: scripts/linux/update-nix-flake.sh <version> <SHA256SUMS>
#   <version>   release tag without the linux-v prefix, e.g. 0.1.0 or 0.1.0-alpha1
#   <SHA256SUMS> the release's checksum file (download it with
#               `gh release download linux-v<version> -R MemerGamer/LinuxFiles -p SHA256SUMS`)
# Rewrites `version` and the two SRI hashes in flake.nix; commit the result in a PR.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ver="${1:?version required, e.g. 0.1.0 or 0.1.0-alpha1}"
sums="${2:?SHA256SUMS file required}"
[[ "$ver" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$ ]] || { echo "Invalid version: $ver" >&2; exit 2; }

python3 - "$root/flake.nix" "$ver" "$sums" <<'PY'
import base64, re, sys
flake, ver, sums = sys.argv[1:4]
hex_of = {}
for line in open(sums):
    parts = line.split()
    if len(parts) == 2:
        name = parts[1].lstrip("*")
        if name in hex_of:
            sys.exit("duplicate entry for %s in %s" % (name, sums))
        hex_of[name] = parts[0]
text = open(flake).read()

def sri(name):
    if not re.fullmatch(r"[0-9a-f]{64}", hex_of.get(name, "")):
        sys.exit("no sha256 for %s in %s" % (name, sums))
    return "sha256-" + base64.b64encode(bytes.fromhex(hex_of[name])).decode()

text, n = re.subn(r'(?m)^(\s*version = ")[^"]*(";)', lambda m: m.group(1) + ver + m.group(2), text)
if n != 1:
    sys.exit("version line not found in flake.nix")
for name in ("files-linux-x64.tar.gz", "files-packaging.tar.gz"):
    pat = r'(url = "\$\{base\}/%s";\s*hash = ")[^"]*(";)' % re.escape(name)
    text, n = re.subn(pat, lambda m: m.group(1) + sri(name) + m.group(2), text)
    if n != 1:
        sys.exit("hash for %s not found in flake.nix" % name)
open(flake, "w").write(text)
PY
echo "Updated flake.nix to $ver"
