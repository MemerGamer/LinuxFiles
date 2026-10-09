#!/usr/bin/env python3
# Copyright (c) Files Community. Licensed under the MIT License.
"""Compare publish directories or tarballs without extracting untrusted archives."""
import argparse
import json
import os
from pathlib import Path, PurePosixPath
import tarfile


def manifest(source):
    source = Path(source)
    entries = {}

    def add(name, size, kind, target=None):
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or not path.parts:
            raise ValueError(f"Unsafe manifest path: {name!r}")
        name = str(path)
        if name in entries:
            raise ValueError(f"Duplicate manifest path: {name!r}")
        entries[name] = {"bytes": size, "kind": kind}
        if target is not None:
            entries[name]["target"] = target

    if source.is_dir():
        for folder, dirs, files in os.walk(source, followlinks=False):
            for name in dirs + files:
                path = Path(folder) / name
                relative = path.relative_to(source).as_posix()
                if path.is_symlink():
                    add(relative, 0, "symlink", os.readlink(path))
                elif path.is_file():
                    add(relative, path.stat().st_size, "file")
        packed_bytes = None
    else:
        with tarfile.open(source, "r:*") as archive:
            for member in archive:
                if member.isdir():
                    continue
                if member.isfile():
                    add(member.name, member.size, "file")
                elif member.issym() or member.islnk():
                    add(member.name, 0, "symlink" if member.issym() else "hardlink", member.linkname)
                else:
                    raise ValueError(f"Unsupported tar member: {member.name!r}")
        # publish.sh archives one RID directory; align it with a publish directory.
        roots = {PurePosixPath(name).parts[0] for name in entries}
        if len(roots) == 1 and all(len(PurePosixPath(name).parts) > 1 for name in entries):
            entries = {name.split("/", 1)[1]: value for name, value in entries.items()}
        packed_bytes = source.stat().st_size
    return {"source": str(source), "packed_bytes": packed_bytes,
            "content_bytes": sum(item["bytes"] for item in entries.values()),
            "file_count": len(entries), "entries": dict(sorted(entries.items()))}


def compare(before, after):
    old, new = before["entries"], after["entries"]
    return {"before": before, "after": after,
            "content_bytes_saved": before["content_bytes"] - after["content_bytes"],
            "packed_bytes_saved": (before["packed_bytes"] - after["packed_bytes"]
                                   if before["packed_bytes"] is not None and after["packed_bytes"] is not None else None),
            "removed": sorted(old.keys() - new.keys()),
            "added": sorted(new.keys() - old.keys()),
            "changed": sorted(name for name in old.keys() & new.keys() if old[name] != new[name])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    parser.add_argument("--json", type=Path, help="Write full size manifests and differences")
    args = parser.parse_args()
    result = compare(manifest(args.before), manifest(args.after))
    for label in ("before", "after"):
        data = result[label]
        print(f"{label}: {data['content_bytes']:,} content bytes ({data['content_bytes'] / 1048576:.2f} MiB), "
              f"{data['file_count']:,} files; tarball bytes: {data['packed_bytes']}")
    print(f"Saved: {result['content_bytes_saved']:,} content bytes; tarball bytes: {result['packed_bytes_saved']}")
    print(f"Removed: {len(result['removed'])}; added: {len(result['added'])}; size/type/link changes: {len(result['changed'])}")
    if args.json:
        args.json.write_bytes((json.dumps(result, indent=2, ensure_ascii=True).replace("\n", "\r\n") + "\r\n").encode())


if __name__ == "__main__":
    main()
