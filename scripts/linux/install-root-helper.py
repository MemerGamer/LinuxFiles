# Copyright (c) Files Community. Licensed under the MIT License.
"""Install the AOT root helper and polkit policy; invoked explicitly with sudo by install-local.sh."""
import argparse
import os
from pathlib import Path
import stat
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--from", dest="source", type=Path, required=True)
parser.add_argument("--destdir", type=Path, default=Path("/"), help="package staging root (no live system changes)")
args = parser.parse_args()
if args.destdir == Path("/") and os.geteuid() != 0:
    parser.error("live installation requires root")
helper = args.source / "files-elevation-helper"
policy = Path(__file__).resolve().parents[2] / "packaging/linux/io.github.memergamer.LinuxFiles.root-actions.policy"
if helper.is_symlink() or not helper.is_file():
    parser.error("expected a published AOT helper, not a symlink")
for source, relative, mode in ((helper, "usr/lib/linuxfiles/files-elevation-helper", "755"),
                               (policy, "usr/share/polkit-1/actions/io.github.memergamer.LinuxFiles.root-actions.policy", "644")):
    destination = args.destdir / relative
    if args.destdir == Path("/"):
        for parent in reversed(destination.parents):
            if not parent.exists():
                parent.mkdir(mode=0o755)
            info = parent.lstat()
            if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
                parser.error(f"untrusted installation directory: {parent}")
        if destination.is_symlink():
            parser.error(f"refusing symlink destination: {destination}")
    command = ["/usr/bin/install", "-D", "-m", mode]
    if args.destdir == Path("/"):
        command += ["-o", "root", "-g", "root"]
    subprocess.run(command + ["--", str(source), str(destination)], check=True)
print(f"Installed helper and policy under {args.destdir}")
