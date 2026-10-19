#!/usr/bin/env python3
"""Optional real Verse.Scribe smoke. All copied game references live in a temporary directory."""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("managed_dir", type=Path, help="Private original game DLL directory; runtime dependencies may also be needed")
    parser.add_argument("--report", type=Path, default=ROOT / "dist/scribe-smoke.json")
    args = parser.parse_args()
    args.report = args.report.resolve()
    args.report.parent.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    env["RIMWORLD_MANAGED_DIR"] = str(args.managed_dir.resolve())
    with tempfile.TemporaryDirectory(prefix="rw17-scribe-") as private:
        output = Path(private)
        subprocess.run(["dotnet", "build", str(ROOT / "tools/ScribeSmoke/ScribeSmoke.csproj"), "-c", "Release", "--nologo",
                        "-o", str(output / "bin"), "-p:SmokeBuildDir=" + private,
                        "-p:BaseIntermediateOutputPath=" + str(output / "obj") + "/"], cwd=ROOT, env=env, check=True)
        process = subprocess.run(["dotnet", str(output / "bin/ScribeSmoke.dll"), env["RIMWORLD_MANAGED_DIR"],
                                  str(output / "synthetic-state.xml"), str(args.report)], cwd=ROOT, env=env)
    raise SystemExit(process.returncode)
