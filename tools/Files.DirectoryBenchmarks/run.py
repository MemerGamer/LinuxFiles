#!/usr/bin/env python3
# Copyright (c) Files Community. Licensed under the MIT License.
"""Build and run the Linux-only spike, serially under the agents' shared lock."""

import argparse
import fcntl
import os
from pathlib import Path
import shutil
import subprocess


def main():
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--disk-parent", type=Path, default=repo / ".cache/rust-benchmark")
    parser.add_argument("--tmpfs-parent", type=Path, default=Path("/tmp"))
    parser.add_argument("--runs", type=int, default=12)
    parser.add_argument("--output", type=Path, default=Path("/tmp/linuxfiles-rust-benchmark.csv"))
    parser.add_argument("--skip-rust", action="store_true")
    args = parser.parse_args()
    if args.runs < 10:
        parser.error("--runs must be at least 10")
    args.disk_parent.mkdir(parents=True, exist_ok=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    for parent, require_tmpfs in [(args.tmpfs_parent, True), (args.disk_parent, False)]:
        filesystem = subprocess.check_output(["stat", "-f", "-c", "%T", str(parent)], text=True).strip()
        if (filesystem == "tmpfs") != require_tmpfs:
            parser.error(f"Unexpected fixture filesystem: {parent} ({filesystem})")
        print(f"Fixture parent: {parent} ({filesystem})", flush=True)
    env = os.environ.copy()
    env["MSBUILDDISABLENODEREUSE"] = "1"
    env["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"
    env.pop("DISPLAY", None)
    env.pop("WAYLAND_DISPLAY", None)
    skip_rust = args.skip_rust or shutil.which("cargo") is None
    if skip_rust:
        print("Rust unmeasured: cargo absent or --skip-rust requested", flush=True)
    lock_path = Path.home() / ".cache/linuxfiles-agents/build.lock"
    print(f"Waiting for {lock_path}", flush=True)
    with lock_path.open("rb") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        print("Lock acquired; starting serial builds and measurements", flush=True)

        def run(command, **kwargs):
            subprocess.run(["nice", "-n", "19", *command], cwd=repo, env=env, check=True, **kwargs)

        if not skip_rust:
            manifest = str(repo / "native/linuxfiles-core/Cargo.toml")
            run(["cargo", "test", "--offline", "--locked", "--manifest-path", manifest, "-j", "2"])
            run(["cargo", "build", "--release", "--offline", "--locked", "--manifest-path", manifest, "-j", "2"])
        project = "tools/Files.DirectoryBenchmarks"
        run(["dotnet", "build", project, "-c", "Release", "-nodeReuse:false", "-m:2",
             "-p:UseSharedCompilation=false", "-p:NuGetAudit=false", "-v:quiet", "-clp:ErrorsOnly"])
        output_dir = repo / project / "bin/Release/net10.0"
        if not skip_rust:
            shutil.copy2(repo / "native/linuxfiles-core/target/release/liblinuxfiles_core.so", output_dir)
        command = ["dotnet", str(output_dir / "Files.DirectoryBenchmarks.dll"),
                   "--disk-parent", str(args.disk_parent.resolve()),
                   "--tmpfs-parent", str(args.tmpfs_parent.resolve()), "--runs", str(args.runs)]
        if skip_rust:
            command.append("--skip-rust")
        with args.output.open("w", newline="\r\n") as output:
            # Child output is bytes: normalize CSV to repository CRLF after completion.
            run(command, stdout=output)
    args.output.write_bytes(args.output.read_bytes().replace(b"\r\n", b"\n").replace(b"\n", b"\r\n"))
    print(f"Results: {args.output}", flush=True)


if __name__ == "__main__":
    main()
