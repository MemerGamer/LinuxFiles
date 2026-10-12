import hashlib
import os
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]


class SecurityPackagingTests(unittest.TestCase):
    def test_templates_fail_closed(self):
        for name in ('linuxfiles', 'linuxfiles-bin'):
            recipe = ROOT / 'packaging/linux/aur' / name / 'PKGBUILD'
            result = subprocess.run(['bash', '-c', 'source "$1"', 'test', str(recipe)], capture_output=True)
            self.assertNotEqual(0, result.returncode)

    def test_generates_pinned_source_and_binary(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            for name in ('files-linux-x64.tar.gz', 'files-packaging.tar.gz', 'linuxfiles-1.2.3.tar.gz'):
                (directory / name).write_bytes(name.encode())
            for kind in ('source', 'binary'):
                out = directory / kind
                subprocess.run(['bash', str(ROOT / 'scripts/linux/gen-aur.sh'), '1.2.3', str(directory), str(out), kind], check=True, capture_output=True, env={**os.environ, 'AUR_SRCINFO_FALLBACK': '1'})
                recipe = (out / 'PKGBUILD').read_text()
                self.assertNotIn("'SKIP'", recipe)
                names = ['linuxfiles-1.2.3.tar.gz'] if kind == 'source' else ['files-linux-x64.tar.gz', 'files-packaging.tar.gz']
                for name in names:
                    checksum = hashlib.sha256((directory / name).read_bytes()).hexdigest()
                    self.assertIn(checksum, recipe)
                    self.assertIn(checksum, (out / '.SRCINFO').read_text())
                subprocess.run(['bash', '-c', 'source "$1"', 'test', str(out / 'PKGBUILD')], check=True)

    def test_install_exec_round_trip(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            source = directory / 'publish'
            source.mkdir()
            (source / 'Files.dll').touch()
            tools = directory / 'tools'
            tools.mkdir()
            for name in ('update-desktop-database', 'gtk-update-icon-cache'):
                (tools / name).write_text('#!/bin/sh\nexit 0\n')
                (tools / name).chmod(0o755)
            for suffix in ('space prefix', 'amp&ersand', 'pipe|prefix', 'quote"prefix', 'percent%f', 'back\\slash', 'dollar$`prefix'):
                prefix = directory / suffix
                subprocess.run(['bash', str(ROOT / 'scripts/linux/install-local.sh'), '--from', str(source), '--prefix', os.path.relpath(prefix)], check=True, capture_output=True, env={**os.environ, 'PATH': str(tools) + ':' + os.environ['PATH']})
                desktop = (prefix / 'share/applications/io.github.memergamer.LinuxFiles.desktop').read_text()
                for line in desktop.splitlines():
                    if line.startswith('Exec='):
                        value = line[5:]
                        # Undo desktop-entry escaping, then Exec argument quoting and literal percent codes.
                        value = value.replace('\\\\', '\\').replace('\\t', '\t')
                        executable = shlex.split(value)[0].replace('\\$', '$').replace('\\`', '`').replace('%%', '%')
                        self.assertEqual(str(prefix / 'bin/files'), executable)
