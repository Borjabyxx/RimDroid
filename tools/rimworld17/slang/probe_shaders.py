#!/usr/bin/env python3
"""Inventory a local IsoRim checkout; optionally compile its GLSL, without modifying it.

Standalone SPIR-V is NOT a Waylith cache entry. No cache keys/layouts are guessed.
"""
import argparse
import hashlib
import itertools
import json
from pathlib import Path
import re
import struct
import subprocess
import xml.etree.ElementTree as ET


def inventory(mod):
    mod = Path(mod)
    definitions = []
    for xml in sorted((mod / 'Defs').rglob('*.xml')):
        for node in ET.parse(xml).getroot().findall('ShaderDef'):
            program = node.find('shaderProgram')
            if program is None:
                continue
            stages = {}
            for stage in ('vertex', 'fragment'):
                name = program.findtext(stage + 'ShaderFileName')
                stages[stage] = {
                    'source': name,
                    'resolution': ('engine-default' if name is None else
                                   'mod' if (mod / 'Shaders' / name).is_file() else
                                   'external-to-mod'),
                }
            definitions.append({'def': node.findtext('defName'), 'stages': stages,
                                'properties': {p.tag: p.text for p in program}})
    sources = []
    for path in sorted((mod / 'Shaders').glob('*.glsl')):
        text = path.read_text()
        # Explore only the feature macros actually used by these sources. Engine
        # defaults and its exact set of defines still need Waylith.dll validation.
        macros = sorted(set(re.findall(r'^\s*#\s*ifdef\s+(INSTANCED|USE_TEXTURE)\b', text, re.M)))
        variants = [dict(zip(macros, values)) for values in itertools.product((False, True), repeat=len(macros))]
        sources.append({'file': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                        'stage': 'vertex' if path.name.endswith('.vert.glsl') else 'fragment',
                        'variants': variants})
    return {'definitions': definitions, 'sources': sources,
            'cache_integration': 'not verified: requires matching Waylith.dll and Assets.zip',
            'missing_cache_entries': None}


def compile_sources(mod, report, slangc, validator, output):
    output.mkdir(parents=True, exist_ok=True)
    results = []
    for source in report['sources']:
        for defines in source['variants']:
            suffix = '-'.join(k for k, v in defines.items() if v) or 'base'
            dest = output / (source['file'] + '.' + suffix + '.spv')
            # Remove only our named generated output so a failed compile cannot
            # be mistaken for a successful result left over from an earlier run.
            dest.unlink(missing_ok=True)
            command = [str(slangc), '-lang', 'glsl', str(mod / 'Shaders' / source['file']),
                       '-target', 'spirv', '-profile', 'spirv_1_3', '-stage', source['stage'],
                       '-entry', 'main', '-fvk-use-entrypoint-name', '-o', str(dest)]
            command.extend('-D' + k + '=1' for k, v in defines.items() if v)
            proc = subprocess.run(command, capture_output=True, text=True)
            ok = proc.returncode == 0 and dest.is_file()
            if ok:
                data = dest.read_bytes()
                ok = len(data) >= 20 and len(data) % 4 == 0 and struct.unpack('<I', data[:4])[0] == 0x07230203
            validation = None
            if ok:
                check = subprocess.run([str(validator), '--target-env', 'vulkan1.1', str(dest)],
                                       capture_output=True, text=True)
                validation = {'exit': check.returncode, 'diagnostics': check.stdout + check.stderr}
                ok = check.returncode == 0
            results.append({'source': source['file'], 'defines': defines, 'passed': ok,
                            'exit': proc.returncode, 'diagnostics': proc.stdout + proc.stderr,
                            'validation': validation})
    report['standalone_compilation'] = results
    return bool(results) and all(r['passed'] for r in results)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mod', type=Path)
    parser.add_argument('--slangc', type=Path)
    parser.add_argument('--spirv-val', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if bool(args.slangc) != bool(args.spirv_val):
        parser.error('--slangc and --spirv-val must be supplied together')
    report = inventory(args.mod)
    if not report['sources'] or not report['definitions']:
        parser.error('No GLSL sources or ShaderDefs found')
    args.output.mkdir(parents=True, exist_ok=True)
    ok = not args.slangc or compile_sources(args.mod, report, args.slangc, args.spirv_val, args.output)
    (args.output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(f"{len(report['definitions'])} ShaderDefs, {len(report['sources'])} GLSL sources; report: {args.output / 'report.json'}")
    return 0 if ok else 1


if __name__ == '__main__':
    raise SystemExit(main())
