# Copyright (c) Files Community. Licensed under the MIT License.
"""Exercise pruning and the actual merge step in throwaway Git repositories."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "scripts/linux/prune-windows.sh"
WORKFLOW = (ROOT / ".github/workflows/sync-upstream.yml").read_text()
MERGE = WORKFLOW.split("    - name: Merge upstream\n", 1)[1].split("      run: |\n", 1)[1]
MERGE = "\n".join(line[8:] for line in MERGE.split("    - name: Update or open the sync PR", 1)[0].splitlines())


class PruneWindowsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="files-prune-test-")
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git("init", "-b", "main")
        self.git("config", "user.name", "Prune test")
        self.git("config", "user.email", "prune@example.invalid")
        self.write("scripts/linux/prune-windows.sh", SCRIPT.read_bytes())
        (self.repo / "scripts/linux/prune-windows.sh").chmod(0o755)
        self.write("scripts/linux/windows-paths.txt", b"# test\r\nwin/\r\nexact.txt\r\n")
        for path in ("win/space name.txt", "win/content.txt", "windows/keep.txt", "exact.txt", "shared.txt"):
            self.write(path, b"base\n")
        self.commit("base")

    def run_command(self, *args, check=True, env=None):
        return subprocess.run(args, cwd=self.repo, check=check, capture_output=True, text=True, env=env)

    def git(self, *args, check=True):
        return self.run_command("git", *args, check=check)

    def write(self, path, content):
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content)

    def commit(self, message):
        self.git("add", ".")
        self.git("commit", "-m", message)

    def prune(self, *args, check=True):
        # Invoke the executable directly to verify its CRLF bootstrap.
        script = self.repo / "scripts/linux/prune-windows.sh"
        script.chmod(0o755)
        return self.run_command(str(script), *args, check=check)

    def test_dry_run_apply_and_idempotence(self):
        before = self.git("status", "--porcelain").stdout
        self.prune()
        self.assertEqual(before, self.git("status", "--porcelain").stdout)
        self.assertTrue((self.repo / "win/space name.txt").exists())
        self.prune("--apply")
        self.assertFalse((self.repo / "win").exists())
        self.assertFalse((self.repo / "exact.txt").exists())
        self.assertTrue((self.repo / "windows/keep.txt").exists())
        self.prune("--apply")

    def test_modified_files_block_all_deletions(self):
        self.write("exact.txt", b"local edit\n")
        self.assertNotEqual(0, self.prune("--apply", check=False).returncode)
        self.assertTrue((self.repo / "win/space name.txt").exists())
        self.assertEqual("", self.git("diff", "--cached", "--name-only").stdout)

    def test_reject_unsafe_lists(self):
        for entry in ("../shared.txt", "/tmp/file", "win/*", ".git/", "win/../shared.txt", "./win/", "."):
            with self.subTest(entry=entry):
                self.write("scripts/linux/windows-paths.txt", (entry + "\r\n").encode())
                self.assertNotEqual(0, self.prune("--apply", check=False).returncode)
                self.assertTrue((self.repo / "shared.txt").exists())

    def test_workflow_merge_cases(self):
        cases = ("clean", "allowed", "mixed", "outside", "reverse", "content", "add-add", "policy")
        # Each case needs its own independent repository and merge state.
        for case in cases:
            with self.subTest(case=case):
                fixture = PruneWindowsTests()
                fixture.setUp()
                try:
                    fixture.check_merge(case)
                finally:
                    fixture.doCleanups()

    def check_merge(self, case):
        self.git("switch", "-c", "upstream/main")
        if case == "reverse":
            self.git("rm", "win/content.txt")
        elif case == "clean":
            self.write("new.txt", b"upstream\n")
        else:
            self.write("win/content.txt", b"upstream\n")
        if case in ("mixed", "outside", "content"):
            self.write("shared.txt", b"upstream\n")
        if case == "add-add":
            self.write("win/new.txt", b"upstream\n")
        if case == "policy":
            self.write("scripts/linux/windows-paths.txt", b"shared.txt\n")
        self.commit("upstream")
        self.git("switch", "main")
        if case in ("allowed", "mixed", "policy"):
            self.git("rm", "win/content.txt")
        if case in ("mixed", "outside"):
            self.git("rm", "shared.txt")
        if case in ("reverse", "content"):
            self.write("win/content.txt", b"ours\n")
        if case == "content":
            self.write("shared.txt", b"ours\n")
        if case == "add-add":
            self.write("win/new.txt", b"ours\n")
        if case != "clean":
            self.commit("ours")
        ours = self.git("rev-parse", "HEAD").stdout.strip()
        runner = self.repo / "runner"
        runner.mkdir()
        self.git("update-ref", "refs/remotes/origin/main", "HEAD")
        # The workflow asks gh for an open sync PR; none exists in the fixture.
        fake = runner / "bin"
        fake.mkdir()
        (fake / "gh").write_text("#!/bin/sh\nexit 0\n")
        (fake / "gh").chmod(0o755)
        env_file = runner / "env"
        env = dict(os.environ, RUNNER_TEMP=str(runner), GITHUB_ENV=str(env_file),
                   PATH=f"{fake}:{os.environ['PATH']}")
        self.run_command("bash", "-e", "-o", "pipefail", "-c", MERGE, env=env)
        success = case in ("clean", "allowed", "policy")
        self.assertIn("CONFLICTS=" + str(not success).lower(), env_file.read_text())
        self.assertEqual("", self.git("diff", "--name-only", "--diff-filter=U").stdout)
        self.assertNotEqual(0, self.git("rev-parse", "-q", "--verify", "MERGE_HEAD", check=False).returncode)
        if success:
            self.assertEqual(2 if case == "clean" else 3,
                             len(self.git("rev-list", "--parents", "-n", "1", "HEAD").stdout.split()))
            if case != "clean":
                self.assertFalse((self.repo / "win/content.txt").exists())
        else:
            self.assertEqual(ours, self.git("rev-parse", "HEAD").stdout.strip())
            conflicts = (runner / "conflicts.txt").read_text()
            self.assertIn("shared.txt" if case in ("mixed", "outside", "content") else "win/", conflicts)
            if case == "mixed":
                self.assertNotIn("win/content.txt", conflicts)


if __name__ == "__main__":
    unittest.main()
