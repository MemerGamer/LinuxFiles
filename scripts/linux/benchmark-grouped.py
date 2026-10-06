# Copyright (c) Files Community
# Licensed under the MIT License.

"""Headless 10k grouped-folder benchmark; run with python3, after building Files.App.
Requires Pillow, tesseract and its English language data. Uses only headless-run.sh.
"""

import argparse
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time


MODES = {"grid": 4, "list": 1, "cards": 2}


def seed(home):
    home = Path(home)
    if os.environ.get("FILES_HEADLESS") != "1" or home != Path(os.environ["HOME"]):
        raise RuntimeError("The fixture requires headless-run.sh's sandbox HOME")
    (home / "Documents" / "empty").mkdir()
    entries = []
    for name, mode in MODES.items():
        folder = home / "Documents" / name
        folder.mkdir()
        for index in range(10000):
            extension = ("txt", "csv", "md", "bin")[index % 4]
            (folder / f"file-{index:05d}.{extension}").write_bytes(b"fixture\n")
        entries.append({"FilePath": str(folder), "Preferences": {
            "LayoutMode": mode, "IsAdaptiveLayoutOverridden": True,
            "DirectoryGroupOption": 5}})
    data = home / ".local/share/files"
    data.mkdir(parents=True, exist_ok=True)
    (data / "layoutpreferences.json").write_text(json.dumps(entries))
    settings = home / ".config/files/settings"
    settings.mkdir(parents=True, exist_ok=True)
    (settings / "user_settings.json").write_text(json.dumps({"GridViewSize": 1}))


def measure(output, target, tessdata):
    from PIL import ImageGrab

    def key(*args):
        subprocess.run(["xdotool", *args], check=True, stdin=subprocess.DEVNULL)

    def navigate(path):
        key("key", "ctrl+l")
        time.sleep(.2)
        key("key", "ctrl+a")
        key("type", "--clearmodifiers", "--delay", "1", str(path))
        time.sleep(.2)

    # Begin from an empty listing; Home cards and transient overlapping last items cannot count.
    navigate(Path(target).parent / "empty")
    key("key", "Return")
    time.sleep(2)
    navigate(target)
    start = time.monotonic()
    key("key", "Return")
    elapsed = None
    crop = (275, 205, 555, 365) if Path(target).name == "grid" else (275, 150, 1000, 600)
    while time.monotonic() - start < 150:
        screen = ImageGrab.grab(xdisplay=os.environ["DISPLAY"])
        captured = time.monotonic()
        frame = screen.crop(crop)
        buffer = io.BytesIO()
        frame.resize((frame.width * 3, frame.height * 3)).save(buffer, format="PNG")
        command = ["tesseract", "stdin", "stdout", "--psm", "11"]
        if tessdata:
            command.extend(["--tessdata-dir", tessdata])
        result = subprocess.run(command, input=buffer.getvalue(), stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, env=dict(os.environ, OMP_THREAD_LIMIT="1"))
        if result.returncode:
            raise RuntimeError(result.stderr.decode())
        if "file-000" in result.stdout.decode():
            footer = io.BytesIO()
            screen.crop((260, 758, 400, 790)).resize((420, 96)).save(footer, format="PNG")
            count = subprocess.run(command, input=footer.getvalue(), stdout=subprocess.PIPE,
                                   stderr=subprocess.PIPE, env=dict(os.environ, OMP_THREAD_LIMIT="1"))
            if "10000" not in count.stdout.decode().replace(",", "").replace(" ", ""):
                time.sleep(.1)
                continue
            elapsed = captured - start
            frame.save(str(output) + "-first.png")
            break
        time.sleep(.1)
    result = {"target": str(target), "first_tiles_seconds": elapsed}
    Path(str(output) + ".json").write_text(json.dumps(result))
    print(json.dumps(result), flush=True)
    if elapsed is None:
        raise RuntimeError("No correctly positioned first filenames within 150 seconds")


def main():
    if len(sys.argv) == 3 and sys.argv[1] == "seed":
        seed(sys.argv[2])
        return
    if len(sys.argv) >= 4 and sys.argv[1] == "measure":
        measure(Path(sys.argv[2]), Path(sys.argv[3]), sys.argv[4] if len(sys.argv) > 4 else "")
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path("/tmp/files-grouped-benchmark"))
    parser.add_argument("--layout", choices=MODES, default="grid")
    parser.add_argument("--bin", type=Path, help="Optional baseline build directory")
    parser.add_argument("--tessdata", default=os.environ.get("TESSDATA_PREFIX", ""))
    args = parser.parse_args()
    languages = ["tesseract", "--list-langs"]
    if args.tessdata:
        languages.extend(["--tessdata-dir", args.tessdata])
    if "eng" not in subprocess.run(languages, capture_output=True, text=True, check=True).stdout.splitlines():
        parser.error("Install tesseract English data or pass --tessdata DIR")
    repo = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="files-grouped-benchmark-") as work:
        work = Path(work)
        fixture = work / "seed.py"
        fixture.write_text("#!/usr/bin/env python3\nimport os,sys\nos.execv(" + repr(sys.executable) +
                           ", [" + repr(sys.executable) + ", " + repr(str(Path(__file__).resolve())) +
                           ", 'seed', sys.argv[1]])\n")
        fixture.chmod(0o755)
        argv = [sys.executable, str(Path(__file__).resolve()), "measure", str(output / "timing"),
                str(output / "home/Documents" / args.layout), args.tessdata]
        actions = work / "actions.txt"
        actions.write_text("exec " + "\t".join(argv) + "\nsleep 1\nshot grouped-" + args.layout + "\n")
        environment = dict(os.environ, FILES_SANDBOX_DRIVES=str(repo / "scripts/linux/showcase-drives.txt"),
                           FILES_SANDBOX_SEED=str(fixture), FILES_GROUPED_LAYOUT_TRACE="1")
        environment.pop("FILES_REAL_HOME", None)
        if args.bin:
            environment["FILES_BIN"] = str(args.bin.resolve())
        subprocess.run(["nice", "-n", "19", "timeout", "180s", str(repo / "scripts/linux/headless-run.sh"),
                        "-s", "12", "-o", str(output), "-a", str(actions)], cwd=repo,
                       env=environment, check=True)


if __name__ == "__main__":
    main()
