#!/usr/bin/env python3
"""Regression gate for both real build outputs, without an Android device.

Usage: python3 test_apks.py BASELINE.apk EXPERIMENTAL.apk /path/to/llvm-readelf
These tests cannot establish Waylith ABI compatibility or GPU behavior.
"""
from pathlib import Path
import sys
import unittest
from verify_apk import verify


class PackagedCompilerRegression(unittest.TestCase):
    def test_default_dotnet_build_keeps_cache_only_compiler(self):
        verify(baseline, readelf, False)

    def test_experimental_dotnet_build_contains_real_compiler(self):
        verify(experimental, readelf, True)

    def test_stub_cannot_pass_as_runtime_compiler(self):
        with self.assertRaisesRegex(ValueError, 'Real Slang API missing'):
            verify(baseline, readelf, True)

    def test_experimental_libraries_cannot_leak_into_default_build(self):
        with self.assertRaisesRegex(ValueError, 'Default build unexpectedly'):
            verify(experimental, readelf, False)


if __name__ == '__main__':
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    baseline, experimental, readelf = map(Path, sys.argv[1:])
    unittest.main(argv=[sys.argv[0]], verbosity=2)
