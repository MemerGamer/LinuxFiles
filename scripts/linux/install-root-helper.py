# Copyright (c) Files Community. Licensed under the MIT License.
"""Install trusted build output, equivalent to sudo make install (trust the installer and build)."""
import argparse
import hashlib
import os
from pathlib import Path
import stat
import struct
import subprocess
import tempfile

POLICY = '<?xml version="1.0" encoding="UTF-8"?>\n<!DOCTYPE policyconfig PUBLIC "-//freedesktop//DTD PolicyKit Policy Configuration 1.0//EN" "http://www.freedesktop.org/standards/PolicyKit/1/policyconfig.dtd">\n<policyconfig>\n  <vendor>LinuxFiles</vendor>\n  <vendor_url>https://github.com/MemerGamer/LinuxFiles</vendor_url>\n  <action id="io.github.memergamer.LinuxFiles.root-actions">\n    <description>Perform the confirmed Files root operation</description>\n    <message>Authentication grants root file access to the listed source and target paths (including their contents). Review the operation and request SHA-256: $(command_line). Program: $(program)</message>\n    <defaults>\n      <allow_any>no</allow_any>\n      <allow_inactive>no</allow_inactive>\n      <allow_active>auth_admin</allow_active>\n    </defaults>\n    <annotate key="org.freedesktop.policykit.exec.path">/usr/lib/linuxfiles/files-elevation-helper</annotate>\n  </action>\n</policyconfig>\n'
MAX_HELPER_BYTES = 256 * 1024 * 1024
HELPER_PATH = "/usr/lib/linuxfiles/files-elevation-helper"
POLICY_PATH = "/usr/share/polkit-1/actions/io.github.memergamer.LinuxFiles.root-actions.policy"


def refuse_package_owned_paths():
    # Query fixed system tools, never the caller's PATH or a shell.
    for tool, option in (("/usr/bin/pacman", "-Qo"), ("/usr/bin/dpkg-query", "-S"), ("/usr/bin/rpm", "-qf")):
        if not os.access(tool, os.X_OK):
            continue
        for path in ("/usr/lib/linuxfiles", HELPER_PATH, POLICY_PATH):
            result = subprocess.run([tool, option, path], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
            if result.returncode == 0:
                raise ValueError(f"refusing root-helper installation: {path} is owned by a distro package; "
                                 "use the packaged helper instead (omit --install-root-helper)")
            if result.returncode != 1:
                raise ValueError(f"cannot check package ownership of {path} with {tool}; no helper files installed")


def snapshot_helper(source, destination, expected_hash):
    # Verify only the private snapshot; a user-writable build tree can change during sudo.
    fd = os.open(source, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, "rb") as input_file, destination.open("xb") as output_file:
        if not stat.S_ISREG(os.fstat(input_file.fileno()).st_mode):
            raise ValueError("expected a regular published AOT helper")
        digest = hashlib.sha256()
        total = 0
        while chunk := input_file.read(65536):
            total += len(chunk)
            if total > MAX_HELPER_BYTES:
                raise ValueError("helper exceeds size limit")
            digest.update(chunk)
            output_file.write(chunk)
        output_file.flush()
        os.fsync(output_file.fileno())
    if digest.hexdigest() != expected_hash.lower():
        raise ValueError("helper snapshot SHA-256 mismatch")
    with destination.open("rb") as binary:
        header = binary.read(64)
    if (len(header) != 64 or header[:7] != b"\x7fELF\x02\x01\x01"
            or struct.unpack_from("<HH", header, 16) not in ((2, 62), (3, 62), (2, 183), (3, 183))):
        raise ValueError("expected a linux-x64/linux-arm64 ELF helper")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--from", dest="source", type=Path, required=True)
    parser.add_argument("--sha256", required=True, help="SHA-256 of the approved published helper")
    parser.add_argument("--destdir", type=Path, default=Path("/"), help="package staging root (no live system changes)")
    args = parser.parse_args(argv)
    args.destdir = args.destdir.resolve()
    live = args.destdir == Path("/")
    if live and os.geteuid() != 0:
        parser.error("live installation requires root")
    if len(args.sha256) != 64 or any(c not in "0123456789abcdefABCDEF" for c in args.sha256):
        parser.error("expected a SHA-256 hex digest")
    if live:
        try:
            refuse_package_owned_paths()
        except (OSError, ValueError) as error:
            parser.error(str(error))
    # Fixed /tmp, ignoring caller-controlled TMPDIR; mkdtemp is private and owned by the invoking installer.
    with tempfile.TemporaryDirectory(prefix="linuxfiles-helper-install-", dir="/tmp") as temporary:
        private = Path(temporary)
        info = private.lstat()
        if info.st_uid != os.geteuid() or stat.S_IMODE(info.st_mode) != 0o700:
            parser.error("untrusted snapshot directory")
        helper = private / "files-elevation-helper"
        try:
            snapshot_helper(args.source / "files-elevation-helper", helper, args.sha256)
        except (OSError, ValueError) as error:
            parser.error(str(error))
        policy = private / "root-actions.policy"
        policy.write_text(POLICY, encoding="utf-8")
        if live:
            os.chown(helper, 0, 0)
            os.chown(policy, 0, 0)
        for source, relative, mode in ((helper, HELPER_PATH.lstrip("/"), "755"),
                                      (policy, POLICY_PATH.lstrip("/"), "644")):
            destination = args.destdir / relative
            if live:
                for parent in reversed(destination.parents):
                    if not parent.exists():
                        parent.mkdir(mode=0o755)
                    info = parent.lstat()
                    if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
                        parser.error(f"untrusted installation directory: {parent}")
            if destination.is_symlink():
                parser.error(f"refusing symlink destination: {destination}")
            command = ["/usr/bin/install", "-D", "-m", mode]
            if live:
                command += ["-o", "root", "-g", "root"]
            subprocess.run(command + ["--", str(source), str(destination)], check=True)
    print(f"Installed helper and policy under {args.destdir}")


if __name__ == "__main__":
    main()
