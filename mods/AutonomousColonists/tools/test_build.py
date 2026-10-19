"""Packaging/input validation tests. Synthetic bytes only; no simulated game API."""
import importlib.util
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("builder", Path(__file__).with_name("build_from_zip.py"))
builder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)


class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.refs = self.root / "refs"
        self.refs.mkdir()

    def tearDown(self):
        self.temp.cleanup()

    def archive(self, names=None, payload=None):
        path = self.root / "private.zip"
        with zipfile.ZipFile(path, "w") as archive:
            for name in names or ["mods/" + n for n in builder.NAMES]:
                archive.writestr(name, payload if payload is not None else b"MZ" + b"0" * 1000)
        return path

    def test_extract_exact_three_not_extra_files(self):
        path = self.archive(["mods/" + n for n in builder.NAMES] + ["extra/secret.txt"])
        builder.extract_references(path, self.refs)
        self.assertEqual(set(p.name for p in self.refs.iterdir()), set(builder.NAMES))
        self.assertEqual(len(builder.validate_references(self.refs)), 3)

    def test_reject_missing(self):
        with self.assertRaises(RuntimeError):
            builder.extract_references(self.archive(["mods/Assembly-CSharp.dll"]), self.refs)

    def test_reject_duplicate_basename(self):
        path = self.archive(["mods/" + n for n in builder.NAMES] + ["duplicate/Waylith.dll"])
        with self.assertRaises(RuntimeError):
            builder.extract_references(path, self.refs)

    def test_no_zip_path_traversal(self):
        path = self.archive(["../../" + n for n in builder.NAMES])
        builder.extract_references(path, self.refs)
        self.assertFalse((self.root.parent / "Assembly-CSharp.dll").exists())
        self.assertEqual(len(list(self.refs.iterdir())), 3)

    def test_reject_non_pe(self):
        with self.assertRaises(RuntimeError):
            builder.extract_references(self.archive(payload=b"notPE" * 300), self.refs)

    def test_reject_small_file(self):
        with self.assertRaises(RuntimeError):
            builder.extract_references(self.archive(payload=b"MZ"), self.refs)

    def test_package_closed_allowlist_deterministic(self):
        content = self.root / "own.dll"
        content.write_bytes(b"own mod test bytes")
        (self.root / "Assembly-CSharp.dll").write_bytes(b"private must not leak")
        members = {"AutonomousColonists/1.7/Assemblies/AutonomousColonists.dll": content}
        with patch.object(builder, "MEMBERS", members):
            first = builder.package(self.root / "first.zip")
            second = builder.package(self.root / "second.zip")
        self.assertEqual(first, second)
        with zipfile.ZipFile(self.root / "first.zip") as archive:
            self.assertEqual(archive.namelist(), list(members))
            self.assertNotIn(b"private must not leak", b"".join(archive.read(n) for n in archive.namelist()))

    def test_reject_missing_member(self):
        with patch.object(builder, "MEMBERS", {"AutonomousColonists/missing": self.root / "missing"}):
            with self.assertRaises(RuntimeError):
                builder.package(self.root / "out.zip")
        self.assertFalse((self.root / "out.zip").exists())

    def test_reject_proprietary_member_even_if_allowlisted(self):
        content = self.root / "file"
        content.write_bytes(b"private")
        with patch.object(builder, "MEMBERS", {"AutonomousColonists/Waylith.dll": content}):
            with self.assertRaises(RuntimeError):
                builder.package(self.root / "out.zip")
        self.assertFalse((self.root / "out.zip").exists())

    def test_reject_symlink_distribution_member(self):
        content = self.root / "file"
        content.write_bytes(b"private")
        link = self.root / "link"
        link.symlink_to(content)
        with patch.object(builder, "MEMBERS", {"AutonomousColonists/file": link}):
            with self.assertRaises(RuntimeError):
                builder.package(self.root / "out.zip")


if __name__ == "__main__":
    unittest.main(verbosity=2)
