# Copyright (c) Files Community. Licensed under the MIT License.
"""Verify companion installation argv without running sudo or changing the host."""
import importlib.util
from pathlib import Path
import subprocess
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[1] / "install-host-root-helper.py"
spec = importlib.util.spec_from_file_location("host_helper_installer", SCRIPT)
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)


class HostInstallerTests(unittest.TestCase):
    def test_live_install_requires_explicit_digest_and_uses_literal_argv(self):
        source = Path("/tmp/release ; $HOME")
        digest = "a" * 64
        with mock.patch.object(installer.subprocess, "run") as run:
            installer.main(["--from", str(source), "--sha256", digest])
        run.assert_called_once_with([
            "/usr/bin/sudo", "/usr/bin/python3", str(SCRIPT.resolve().with_name("install-root-helper.py")),
            "--from", str(source / "elevation-helper"), "--sha256", digest], check=True)

    def test_staging_never_invokes_sudo(self):
        with mock.patch.object(installer.subprocess, "run") as run:
            installer.main(["--from", "/tmp/release", "--sha256", "a" * 64, "--destdir", "/tmp/staging"])
        command = run.call_args.args[0]
        self.assertEqual("/usr/bin/python3", command[0])
        self.assertEqual(["--destdir", "/tmp/staging"], command[-2:])
        self.assertNotIn("shell", run.call_args.kwargs)

    def test_invalid_digest_and_root_staging_never_invoke_installer(self):
        for arguments in (["--sha256", "not-a-hash"], ["--sha256", "a" * 64, "--destdir", "/"]):
            with mock.patch.object(installer.subprocess, "run") as run, self.assertRaises(SystemExit):
                installer.main(["--from", "/tmp/release"] + arguments)
            run.assert_not_called()

    def test_installation_failure_is_not_reported_as_success(self):
        with mock.patch.object(installer.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "installer")):
            with self.assertRaises(subprocess.CalledProcessError):
                installer.main(["--from", "/tmp/release", "--sha256", "a" * 64])
