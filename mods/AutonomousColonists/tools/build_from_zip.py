#!/usr/bin/env python3
"""Build only Autonomous Colonists with private references; publish only an explicit allowlist."""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
NAMES = ("Assembly-CSharp.dll", "UnityStubs.dll", "Waylith.dll")
MEMBERS = {
    "AutonomousColonists/About/About.xml": ROOT / "About/About.xml",
    "AutonomousColonists/1.7/Assemblies/AutonomousColonists.dll": ROOT / "1.7/Assemblies/AutonomousColonists.dll",
    "AutonomousColonists/LICENCIA.txt": ROOT / "LICENCIA.txt",
    "AutonomousColonists/INSTALAR.md": ROOT / "INSTALAR.md",
    "AutonomousColonists/AUTONOMIA_0.5.md": ROOT / "AUTONOMIA_0.5.md",
    "AutonomousColonists/PRUEBAS_0.5.md": ROOT / "PRUEBAS_0.5.md",
    "AutonomousColonists/RENDIMIENTO_0.5.md": ROOT / "RENDIMIENTO_0.5.md",
    **{"AutonomousColonists/TestResults/" + name: ROOT / "TestResults" / name for name in
       ("core-tests.json", "director-tests.json", "action-tests.json", "packaging-tests.txt", "api-audit.json")},
}


def extract_references(zip_path: Path, directory: Path) -> None:
    with zipfile.ZipFile(zip_path) as archive:
        for required in NAMES:
            candidates = [entry for entry in archive.infolist()
                          if not entry.is_dir() and Path(entry.filename).name == required]
            if len(candidates) != 1:
                raise RuntimeError(f"Se esperaba un único {required}; encontrados: {len(candidates)}")
            member = candidates[0]
            if not 1000 <= member.file_size <= 50_000_000:
                raise RuntimeError(f"Tamaño sospechoso para {required}")
            content = archive.read(member)
            if content[:2] != b"MZ":
                raise RuntimeError(f"{required} no tiene cabecera PE")
            (directory / required).write_bytes(content)


def validate_references(directory: Path) -> list[dict]:
    records = []
    for name in NAMES:
        path = directory / name
        if not path.is_file() or not 1000 <= path.stat().st_size <= 50_000_000:
            raise RuntimeError(f"Falta referencia privada válida: {name}")
        with path.open("rb") as stream:
            if stream.read(2) != b"MZ":
                raise RuntimeError(f"Referencia no PE: {name}")
        records.append({"file": name, "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    return records


def package(destination: Path) -> str:
    destination.parent.mkdir(parents=True, exist_ok=True)
    # Stable timestamps and order; ZIP never recurses into a directory containing private files.
    temporary = destination.with_suffix(".zip.tmp")
    try:
        with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for name, source in sorted(MEMBERS.items()):
                if not source.is_file() or source.is_symlink():
                    raise RuntimeError(f"Falta archivo de distribución: {source.name}")
                item = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
                item.external_attr = 0o100644 << 16
                item.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(item, source.read_bytes(), compresslevel=9)
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip() is not None or set(archive.namelist()) != set(MEMBERS):
                raise RuntimeError("ZIP fuera de la lista permitida")
            if any(Path(name).name in NAMES for name in archive.namelist()):
                raise RuntimeError("Biblioteca propietaria en distribución")
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)
    return hashlib.sha256(destination.read_bytes()).hexdigest()


def build(directory: Path, output: Path, verify_only: bool) -> None:
    ET.parse(ROOT / "About/About.xml")
    references = validate_references(directory)
    if verify_only:
        print("Referencias privadas y About.xml verificados; compilación no ejecutada.")
        return
    if not shutil.which("dotnet"):
        raise RuntimeError("Se necesita SDK .NET 10 ('dotnet' en PATH)")
    sdk = subprocess.check_output(["dotnet", "--version"], text=True).strip()
    if not sdk.startswith("10."):
        raise RuntimeError(f"Se necesita .NET 10; encontrado {sdk}")
    output.mkdir(parents=True, exist_ok=True)
    for name in ("AutonomousColonists-RimWorld-1.7.zip", "SHA256SUMS", "build-report.json", "api-audit.json", "core-tests.json", "director-tests.json", "action-tests.json", "packaging-tests.txt"):
        (output / name).unlink(missing_ok=True)
    env = os.environ.copy()
    env["RIMWORLD_MANAGED_DIR"] = str(directory)
    packaging = subprocess.run([sys.executable, "-m", "unittest", "discover", "-s", "tools", "-p", "test_*.py", "-v"],
                               cwd=ROOT, text=True, capture_output=True)
    (output / "packaging-tests.txt").write_text(packaging.stdout + packaging.stderr, encoding="utf-8")
    print(packaging.stdout + packaging.stderr, end="", flush=True)
    packaging.check_returncode()
    subprocess.run(["dotnet", "run", "--project", str(ROOT / "Tests/AutonomousColonists.Tests.csproj"),
                    "-c", "Release", "--", "--report", str(output / "core-tests.json")], check=True, cwd=ROOT)
    subprocess.run(["dotnet", "run", "--project", str(ROOT / "Tests/Director/AutonomousColonists.Director.Tests.csproj"),
                    "-c", "Release", "--", "--report", str(output / "director-tests.json")], check=True, cwd=ROOT)
    subprocess.run(["dotnet", "run", "--project", str(ROOT / "Tests/Actions/AutonomousColonists.Actions.Tests.csproj"),
                    "-c", "Release", "--", "--report", str(output / "action-tests.json")], check=True, cwd=ROOT)
    subprocess.run(["dotnet", "build", str(ROOT / "Source/AutonomousColonists.csproj"), "-c", "Release",
                    "--nologo", "-p:ContinuousIntegrationBuild=true", "-p:UseSharedCompilation=false"],
                   check=True, cwd=ROOT, env=env)
    dll = ROOT / "1.7/Assemblies/AutonomousColonists.dll"
    subprocess.run(["dotnet", "run", "--project", str(ROOT / "tools/ReferenceAudit/ReferenceAudit.csproj"),
                    "-c", "Release", "--", str(directory), str(dll), str(output / "api-audit.json")], check=True, cwd=ROOT)
    (ROOT / "TestResults").mkdir(exist_ok=True)
    for name in ("core-tests.json", "director-tests.json", "action-tests.json", "packaging-tests.txt", "api-audit.json"):
        source, target = output / name, ROOT / "TestResults" / name
        if source.resolve() != target.resolve():
            shutil.copyfile(source, target)
    destination = output / "AutonomousColonists-RimWorld-1.7.zip"
    sha = package(destination)
    (output / "SHA256SUMS").write_text(f"{sha}  {destination.name}\n", encoding="utf-8")
    try:
        commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, stderr=subprocess.DEVNULL, text=True).strip()
        dirty = bool(subprocess.check_output(["git", "status", "--porcelain", "--", "."], cwd=ROOT, text=True).strip())
    except (OSError, subprocess.CalledProcessError):
        commit, dirty = None, None
    source_files = [path for folder in ("Source", "Tests", "tools") for path in (ROOT / folder).rglob("*")
                    if path.suffix in (".cs", ".csproj", ".py") and not {"bin", "obj", "__pycache__"}.intersection(path.parts)]
    source_files += [ROOT / "About/About.xml", ROOT / "INSTALAR.md", ROOT / "LICENCIA.txt",
                     ROOT / "AUTONOMIA_0.5.md", ROOT / "PRUEBAS_0.5.md", ROOT / "RENDIMIENTO_0.5.md"]
    (output / "build-report.json").write_text(json.dumps({
        "version": "0.5.1-experimental", "sdk": sdk, "references": references,
        "zip": destination.name, "sha256": sha, "members": sorted(MEMBERS),
        "sourceCommit": commit, "sourceTreeDirty": dirty,
        "sourceFiles": {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(source_files)},
        "gameExecuted": False, "androidExecuted": False,
    }, indent=2) + "\n", encoding="utf-8")
    print(f"ZIP: {destination}\nSHA-256: {sha}\nCompilado contra las DLL privadas; ejecución en juego/Android pendiente.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mods_zip", type=Path, nargs="?", help="ZIP PRIVADO, nunca se publica")
    parser.add_argument("--managed-dir", type=Path, help="Carpeta privada del runner con las tres referencias")
    parser.add_argument("--output-dir", type=Path, default=ROOT / "dist")
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()
    if bool(args.mods_zip) == bool(args.managed_dir):
        parser.error("Indica mods_zip O --managed-dir")
    try:
        if args.mods_zip:
            with tempfile.TemporaryDirectory(prefix="rw17-autonomous-") as temp:
                directory = Path(temp)
                extract_references(args.mods_zip.resolve(), directory)
                build(directory, args.output_dir.resolve(), args.verify_only)
        else:
            build(args.managed_dir.resolve(), args.output_dir.resolve(), args.verify_only)
        return 0
    except (OSError, RuntimeError, zipfile.BadZipFile, subprocess.CalledProcessError, ET.ParseError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
