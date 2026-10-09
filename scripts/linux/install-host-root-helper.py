#!/usr/bin/env python3
# Copyright (c) Files Community. Licensed under the MIT License.
"""Opt in to a host-only helper install from a verified matching native release.

This does not enable root actions inside AppImage or Flatpak. Trust the checkout
and published build as for sudo make install; supply the approved helper digest.
"""
import argparse
from pathlib import Path
import subprocess


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--from", dest="source", type=Path, required=True,
                        help="unpacked native release directory (containing elevation-helper)")
    parser.add_argument("--sha256", required=True, help="approved SHA-256 of files-elevation-helper")
    parser.add_argument("--destdir", type=Path, help="stage without sudo or host system changes")
    args = parser.parse_args(argv)
    if len(args.sha256) != 64 or any(c not in "0123456789abcdefABCDEF" for c in args.sha256):
        parser.error("expected a SHA-256 hex digest")
    if args.destdir is not None and args.destdir.resolve() == Path("/"):
        parser.error("omit --destdir to opt into a live administrator installation")
    installer = Path(__file__).resolve().with_name("install-root-helper.py")
    command = ["/usr/bin/python3", str(installer), "--from", str(args.source.resolve() / "elevation-helper"),
               "--sha256", args.sha256]
    if args.destdir is not None:
        command += ["--destdir", str(args.destdir.resolve())]
    else:
        command = ["/usr/bin/sudo"] + command
    subprocess.run(command, check=True)


if __name__ == "__main__":
    main()
