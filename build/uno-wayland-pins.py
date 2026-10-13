"""Read the repository's trusted Uno package pins; never trust release digests."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
IDS = ('Uno.WinUI', 'Uno.WinRT', 'Uno.Foundation', 'Uno.Foundation.Logging',
       'Uno.UI.Adapter.Microsoft.Extensions.Logging', 'Uno.WinUI.Runtime.Skia',
       'Uno.WinUI.Runtime.Skia.X11', 'Uno.WinUI.Runtime.Skia.Wayland')


def read_pins(version):
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+-wayland\.[0-9]+', version):
        raise ValueError('Expected X.Y.Z-wayland.N version')
    expected = {f'{name}.{version}.nupkg' for name in IDS}
    pins = {}
    for number, line in enumerate((ROOT / 'build/uno-wayland-packages.sha256').read_text().splitlines(), 1):
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        fields = line.split()
        if len(fields) != 3:
            raise ValueError(f'Invalid pin row {number}: expected VERSION SHA256 FILENAME')
        pinned_version, digest, name = fields
        if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+-wayland\.[0-9]+', pinned_version) or not re.fullmatch(r'[0-9a-f]{64}', digest) or name not in {f'{id}.{pinned_version}.nupkg' for id in IDS}:
            raise ValueError(f'Invalid pin row {number}')
        if pinned_version != version:
            continue
        if name in pins:
            raise ValueError(f'Duplicate pin: {name}')
        pins[name] = digest
    if set(pins) != expected:
        raise ValueError(f'No complete committed pins for Uno {version}. Build the eight fork packages, compute SHA-256, commit build/uno-wayland-packages.sha256, then publish those exact bytes. See docs/linux-port/wayland-packages.md. Fetch/import is disabled until all eight pins exist.')
    return pins


if __name__ == '__main__':
    try:
        if len(sys.argv) != 2:
            raise ValueError('Usage: python3 build/uno-wayland-pins.py VERSION')
        for name, digest in read_pins(sys.argv[1]).items():
            print(f'{digest}  {name}')
    except (ValueError, OSError) as error:
        sys.exit(str(error))
