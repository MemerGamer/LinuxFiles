# Copyright (c) Files Community. Licensed under the MIT License.
import importlib.util
import io
from pathlib import Path
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("compare_publish", Path(__file__).resolve().parents[1] / "compare-publish.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PublishManifestTests(unittest.TestCase):
    def test_tarball_matches_directory_and_never_follows_symlinks(self):
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            publish = root / "linux-x64"
            publish.mkdir()
            (publish / "Files.dll").write_bytes(b"assembly")
            (publish / "outside").symlink_to(root, target_is_directory=True)
            tarball = root / "publish.tar.gz"
            with tarfile.open(tarball, "w:gz") as archive:
                archive.add(publish, arcname="linux-x64")
            directory = module.manifest(publish)
            packed = module.manifest(tarball)
            self.assertEqual(directory["entries"], packed["entries"])
            self.assertEqual(8, packed["content_bytes"])
            self.assertEqual(2, packed["file_count"])

    def test_size_differences_and_escaped_names(self):
        with tempfile.TemporaryDirectory() as root:
            before, after = Path(root) / "before", Path(root) / "after"
            before.mkdir()
            after.mkdir()
            (before / "removed\nasset").write_bytes(b"123")
            (before / "Files.dll").write_bytes(b"12345")
            (after / "Files.dll").write_bytes(b"1234")
            (after / "new").write_bytes(b"1")
            result = module.compare(module.manifest(before), module.manifest(after))
            self.assertEqual(3, result["content_bytes_saved"])
            self.assertEqual(["removed\nasset"], result["removed"])
            self.assertEqual(["Files.dll"], result["changed"])
            self.assertEqual(["new"], result["added"])

    def test_rejects_traversal_absolute_and_duplicate_tar_names(self):
        for names in (["../escape"], ["/escape"], ["linux-x64/a", "linux-x64/a"]):
            with self.subTest(names=names), tempfile.TemporaryDirectory() as root:
                tarball = Path(root) / "bad.tar"
                with tarfile.open(tarball, "w") as archive:
                    for name in names:
                        info = tarfile.TarInfo(name)
                        info.size = 1
                        archive.addfile(info, io.BytesIO(b"x"))
                with self.assertRaises(ValueError):
                    module.manifest(tarball)


if __name__ == "__main__":
    unittest.main()
