"""Offline behavioral checks for the public release downloader (no restore/build)."""
import hashlib
import os
import shutil
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'build/fetch-uno-wayland.sh'
IDS = ('Uno.WinUI', 'Uno.WinRT', 'Uno.Foundation', 'Uno.Foundation.Logging',
       'Uno.UI.Adapter.Microsoft.Extensions.Logging', 'Uno.WinUI.Runtime.Skia',
       'Uno.WinUI.Runtime.Skia.X11', 'Uno.WinUI.Runtime.Skia.Wayland')


class FetchTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='uno-fetch-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        # Fixture pins live in a separate checkout; production trust cannot be overridden.
        self.repo = self.root / 'repo'
        (self.repo / 'build').mkdir(parents=True)
        (self.repo / 'scripts/linux').mkdir(parents=True)
        (self.repo / 'packaging/linux/aur/linuxfiles').mkdir(parents=True)
        for path in ('build/fetch-uno-wayland.sh', 'build/uno-wayland-pins.py',
                     'build/clear-uno-wayland-cache.sh', 'scripts/linux/gen-aur.sh',
                     'packaging/linux/aur/linuxfiles/PKGBUILD', 'Directory.Packages.props'):
            shutil.copy2(ROOT / path, self.repo / path)
        self.script = self.repo / 'build/fetch-uno-wayland.sh'
        self.source = self.root / 'release assets'
        self.feed = self.root / 'local feed'
        self.source.mkdir()
        version_node = ET.parse(ROOT / 'Directory.Packages.props').find('.//UnoWaylandVersion')
        self.version = version_node.text
        self.names = [f'{name}.{self.version}.nupkg' for name in IDS]
        # The downloader verifies opaque package bytes; publisher metadata is checked separately.
        self.manifest = ''
        for name in self.names:
            data = f'Fixture payload for {name}\n'.encode()
            (self.source / name).write_bytes(data)
            self.manifest += f'{hashlib.sha256(data).hexdigest()}  {name}\n'
        (self.source / 'SHA256SUMS').write_text(self.manifest)
        self.pinfile = self.repo / 'build/uno-wayland-packages.sha256'
        self.pins = ''.join(f'{self.version} {line}\n' for line in self.manifest.splitlines())
        self.pinfile.write_text(self.pins)
        bindir = self.root / 'bin'
        bindir.mkdir()
        # Replay HTTPS downloads locally, preserving curl's output/URL argument contract.
        curl = bindir / 'curl'
        curl.write_text('''#!/usr/bin/env python3
import os, pathlib, shutil, sys
args = sys.argv[1:]
url = args[-1]
expected = 'https://github.com/MemerGamer/uno/releases/download/uno-wayland-' + os.environ['REPLAY_VERSION'] + '/'
if not url.startswith(expected):
    sys.exit(2)
asset = url[len(expected):]
with open(os.environ['REPLAY_LOG'], 'a') as log:
    log.write(asset + '\\n')
if os.environ.get('REPLAY_DENY'):
    sys.exit(22)
shutil.copyfile(pathlib.Path(os.environ['REPLAY_SOURCE']) / asset, args[args.index('--output') + 1])
''')
        curl.chmod(0o755)
        self.log = self.root / 'requests'
        self.env = dict(os.environ, PATH=f'{bindir}:{os.environ["PATH"]}',
                        REPLAY_VERSION=self.version, REPLAY_SOURCE=str(self.source), REPLAY_LOG=str(self.log))

    def fetch(self, *args, offline=True, success=True):
        command = [str(self.script), '--feed', str(self.feed)]
        if offline:
            command += ['--from', str(self.source)]
        result = subprocess.run(command + list(args), env=self.env, capture_output=True, text=True, timeout=15)
        if success:
            self.assertEqual(result.returncode, 0, result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertEqual([path for path in self.feed.glob('.fetch.*') if path.is_dir()], [], 'Download staging was not cleaned')
        return result

    def test_import_and_offline_cache_recheck(self):
        self.fetch()
        for name in self.names:
            self.assertEqual((self.feed / name).read_bytes(), (self.source / name).read_bytes())
        self.env['REPLAY_DENY'] = '1'
        self.fetch(offline=False)
        self.assertFalse(self.log.exists(), 'Verified cache should not need network')

    def test_download_uses_pinned_public_asset_contract(self):
        self.fetch(offline=False)
        self.assertEqual(set(self.log.read_text().splitlines()), {'SHA256SUMS', *self.names})

    def test_corrupt_cache_is_redownloaded(self):
        self.fetch()
        damaged = self.feed / self.names[-1]
        damaged.write_bytes(b'corrupt cache')
        self.fetch(offline=False)
        self.assertEqual(damaged.read_bytes(), (self.source / damaged.name).read_bytes())
        self.assertEqual(self.log.read_text().splitlines(), ['SHA256SUMS', damaged.name])

    def test_bad_checksum_exposes_no_new_packages(self):
        (self.source / self.names[-1]).write_bytes(b'corrupt download')
        result = self.fetch(success=False)
        self.assertIn('Checksum mismatch', result.stderr)
        self.assertEqual(list(self.feed.glob('*.nupkg')), [])
        self.assertEqual(list(self.feed.glob('.sha256-*')), [])

    def test_incomplete_and_untrusted_manifests_are_rejected(self):
        lines = self.manifest.splitlines(keepends=True)
        cases = ('', ''.join(lines[:-1]), self.manifest + lines[0],
                 self.manifest.replace(self.names[0], '../outside.nupkg'),
                 self.manifest + '0' * 64 + '  extra.nupkg\n',
                 self.manifest.replace(lines[0][:64], 'not-a-hash'),
                 self.manifest + '\n', '0' * 64 + '\n')
        for manifest in cases:
            with self.subTest(manifest=manifest[:90]):
                (self.source / 'SHA256SUMS').write_text(manifest)
                self.fetch(success=False)
                self.assertEqual(list(self.feed.glob('*.nupkg')), [])

    def test_missing_asset_and_failed_download_leave_no_packages(self):
        (self.source / self.names[-1]).unlink()
        self.fetch(success=False)
        self.assertEqual(list(self.feed.glob('*.nupkg')), [])
        self.env['REPLAY_DENY'] = '1'
        self.fetch(offline=False, success=False)
        self.assertEqual(list(self.feed.glob('*.nupkg')), [])

    def test_local_developer_import_needs_no_release_manifest(self):
        (self.source / 'SHA256SUMS').unlink()
        self.fetch('--import', str(self.source), offline=False)
        self.assertEqual(len(list(self.feed.glob('*.nupkg'))), 8)
        self.assertFalse(self.log.exists())

    def test_import_checks_source_even_when_cache_is_valid(self):
        self.fetch()
        (self.source / self.names[0]).write_bytes(b'replaced local build')
        self.fetch('--import', str(self.source), success=False)
        self.assertEqual((self.feed / self.names[0]).read_bytes(), f'Fixture payload for {self.names[0]}\n'.encode())

    def test_replacing_packages_and_release_or_cache_manifest_cannot_change_pins(self):
        self.fetch()
        name = self.names[0]
        payload = b'replacement bytes'
        replacement = self.manifest.replace(self.manifest[:64], hashlib.sha256(payload).hexdigest())
        (self.source / name).write_bytes(payload)
        (self.source / 'SHA256SUMS').write_text(replacement)
        self.fetch(success=False)
        (self.feed / name).write_bytes(payload)
        (self.feed / f'.sha256-{self.version}').write_text(replacement)
        self.fetch(offline=False, success=False)
        self.assertFalse(self.log.exists())
        # Remote manifest tampering is equally insufficient in an empty feed.
        shutil.rmtree(self.feed)
        self.fetch(offline=False, success=False)
        self.assertEqual(self.log.read_text().splitlines(), ['SHA256SUMS'])
        self.assertEqual(list(self.feed.glob('*.nupkg')), [])

    def test_unpinned_versions_and_incomplete_or_invalid_pins_fail_before_fetch(self):
        for pins in ('# TODO: packages not yet built\n', self.pins.rsplit('\n', 2)[0] + '\n',
                     self.pins + self.pins.splitlines()[0] + '\n',
                     self.pins.replace(self.names[0], '../outside.nupkg')):
            with self.subTest(pins=pins[:90]):
                self.pinfile.write_text(pins)
                result = self.fetch(offline=False, success=False)
                self.assertFalse(self.feed.exists())
                self.assertFalse(self.log.exists())
        self.pinfile.write_text(self.pins)
        result = self.fetch('--version', '6.7.135-wayland.999', offline=False, success=False)
        self.assertIn('No complete committed pins', result.stderr)
        self.assertFalse(self.feed.exists())

    def test_global_cache_cleanup_removes_only_the_eight_fork_versions(self):
        cache = self.root / 'nuget cache'
        for id in (*IDS, 'Other.Package'):
            for version in (self.version, '6.7.135'):
                path = cache / id.lower() / version
                path.mkdir(parents=True)
                (path / 'sentinel').write_bytes(b'cached')
        result = subprocess.run([str(self.repo / 'build/clear-uno-wayland-cache.sh')],
                                env=dict(self.env, NUGET_PACKAGES=str(cache)), capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        for id in IDS:
            self.assertFalse((cache / id.lower() / self.version).exists())
            self.assertTrue((cache / id.lower() / '6.7.135/sentinel').exists())
        self.assertTrue((cache / 'other.package' / self.version / 'sentinel').exists())

    def test_source_aur_declares_pinned_inputs_and_imports_offline(self):
        version = '0.1.0-alpha9'
        archive = self.root / f'linuxfiles-{version}.tar.gz'
        archive.write_bytes(b'application source fixture')
        output = self.root / 'aur'
        command = [str(self.repo / 'scripts/linux/gen-aur.sh'), version, str(self.root), str(output), 'source']
        result = subprocess.run(command, env=dict(self.env, AUR_SRCINFO_FALLBACK='1'), capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        info = (output / '.SRCINFO').read_text()
        sources = [line.split(' = ', 1)[1] for line in info.splitlines() if line.startswith('\tsource = ')]
        noextract = [line.split(' = ', 1)[1] for line in info.splitlines() if line.startswith('\tnoextract = ')]
        sums = [line.split(' = ', 1)[1] for line in info.splitlines() if line.startswith('\tsha256sums = ')]
        self.assertEqual(len(sources), 9)
        self.assertEqual(set(noextract), set(self.names))
        for url, digest in zip(sources[1:], sums[1:]):
            self.assertEqual(digest, hashlib.sha256((self.source / url.rsplit('/', 1)[1]).read_bytes()).hexdigest())
        recipe = (output / 'PKGBUILD').read_text()
        self.assertIn('build/fetch-uno-wayland.sh --from "$srcdir"', recipe)
        self.assertNotIn('UNGENERATED', ' '.join(sums))
        self.pinfile.write_text('# TODO\n')
        self.assertNotEqual(subprocess.run(command, env=self.env, capture_output=True).returncode, 0)

    def test_version_and_argument_validation(self):
        for args in (('--version', '../bad'), ('--version',), ('--unknown',)):
            with self.subTest(args=args):
                result = subprocess.run([str(self.script), *args], env=self.env, capture_output=True, timeout=15)
                self.assertEqual(result.returncode, 2, result.stderr)


if __name__ == '__main__':
    unittest.main()
