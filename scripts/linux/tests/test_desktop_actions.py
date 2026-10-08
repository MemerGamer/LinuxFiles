# Copyright (c) Files Community. Licensed under the MIT License.
"""Exercise the actual portable packaging filters without building or installing packages."""
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[3]
APP_ID = "io.github.memergamer.LinuxFiles"


class DesktopActionTests(unittest.TestCase):
    def test_portable_filters_preserve_following_groups(self):
        desktop = (REPO / f"packaging/linux/{APP_ID}.desktop").read_text()
        before_root = desktop.split("[Desktop Action root-mode]", 1)[0]
        following = "[Desktop Action later]\nName=Later action\nExec=files --new-window %U\n"
        for script in ("packaging/linux/appimage/build-appimage.sh",
                       f"packaging/linux/flatpak/{APP_ID}.yml"):
            expression = re.search(r"sed -i '([^']+)'", (REPO / script).read_text()).group(1)
            for suffix in ("", following):
                fixture = desktop + "\n" + suffix
                expected = before_root.replace("root-mode;", "") + suffix
                if suffix:
                    fixture = fixture.replace("Actions=new-window;root-mode;", "Actions=new-window;root-mode;later;")
                    expected = expected.replace("Actions=new-window;", "Actions=new-window;later;")
                for newline in ("\n", "\r\n"):
                    with self.subTest(script=script, following_group=bool(suffix), newline=repr(newline)):
                        result = subprocess.run(["sed", expression], input=fixture.replace("\n", newline),
                                                text=True, capture_output=True, check=True)
                        self.assertEqual(expected.strip(), result.stdout.strip())
                        validator = shutil.which("desktop-file-validate")
                        if validator:
                            with tempfile.TemporaryDirectory(prefix="files-desktop-test-") as stage:
                                output = Path(stage) / f"{APP_ID}.desktop"
                                output.write_text(result.stdout)
                                subprocess.run([validator, str(output)], check=True, capture_output=True)


if __name__ == "__main__":
    unittest.main()
