#!/usr/bin/env python3
"""Verify packaged ARM64 libraries, real Slang exports and dependency closure."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile

MODULES = ('libslang.so', 'libslang-glslang-2026.19.so', 'libslang-glsl-module-2026.19.so')
SYSTEM_LIBS = {'libc.so', 'libm.so', 'libdl.so', 'liblog.so', 'libandroid.so',
               'libz.so', 'libEGL.so', 'libGLESv2.so', 'libGLESv3.so', 'libvulkan.so',
               'libjnigraphics.so', 'libmediandk.so', 'libaaudio.so', 'libOpenSLES.so'}


def inspect(path, readelf):
    def read(*args):
        return subprocess.check_output([str(readelf), *args, str(path)], text=True)
    header, dynamic, symbols, segments = read('-h'), read('-d'), read('--dyn-syms', '-W'), read('-l', '-W')
    if 'AArch64' not in header or 'ELF64' not in header:
        raise ValueError(f'{path.name}: expected ELF64 AArch64')
    exports = set()
    for line in symbols.splitlines():
        fields = line.split()
        if len(fields) >= 8 and fields[4] in ('GLOBAL', 'WEAK') and fields[5] == 'DEFAULT' and fields[6] != 'UND':
            exports.add(fields[7].split('@')[0])
    return {'needed': re.findall(r'\(NEEDED\).*?\[(.*?)\]', dynamic),
            'soname': re.findall(r'\(SONAME\).*?\[(.*?)\]', dynamic),
            'exports': exports,
            'load_alignment': [int(line.split()[-1], 16) for line in segments.splitlines()
                               if line.strip().startswith('LOAD ')]}


def verify(apk, readelf, runtime):
    with zipfile.ZipFile(apk) as archive, tempfile.TemporaryDirectory() as tmp:
        names = archive.namelist()
        if len(names) != len(set(names)) or archive.testzip():
            raise ValueError('Duplicate or corrupt APK entries')
        required = {'AndroidManifest.xml', 'classes.dex', 'lib/arm64-v8a/libcoreclr.so',
                    'assets/dotnet/framework/System.Private.CoreLib.dll',
                    'assets/dotnet/hooks/RimDroidClrHooks.dll'}
        if not required.issubset(names):
            raise ValueError(f'Missing .NET APK entries: {required - set(names)}')
        packaged = {Path(n).name for n in names if n.startswith('lib/arm64-v8a/') and n.endswith('.so')}
        info = {}
        for name in names:
            if name.startswith('lib/') and name.endswith('.so'):
                if not name.startswith('lib/arm64-v8a/'):
                    raise ValueError(f'Unexpected ABI: {name}')
                path = Path(tmp) / Path(name).name
                path.write_bytes(archive.read(name))
                info[path.name] = inspect(path, readelf)
        # Existing Mono is preloaded as libmonobdwgc-2.0.so but publishes the
        # SONAME libmonoboehm-2.0.so. Record such aliases; this is dependency
        # inventory, not a claim that every possible load order works.
        sonames = {s for item in info.values() for s in item['soname']}
        for name, item in info.items():
            missing = set(item['needed']) - packaged - sonames - SYSTEM_LIBS
            if missing:
                raise ValueError(f'{name}: unpackaged dependencies {missing}')
        slang = info['libslang.so']
        if 'spGetBuildTagString' not in slang['exports']:
            raise ValueError('Slang build-tag export missing')
        if 'wa_sound_resident_bytes' not in info['libwaylithaudio.so']['exports']:
            raise ValueError('Audio resident-bytes export missing')
        if runtime:
            expected = {'slang_createGlobalSession2', 'slang_createGlobalSession', 'spCreateSession',
                        'spCreateCompileRequest', 'spCompile', 'slang_createBlob'}
            if not expected.issubset(slang['exports']):
                raise ValueError(f'Real Slang API missing: {expected - slang["exports"]}')
            for name in MODULES:
                lib = info[name]
                if lib['soname'] != [name] or not lib['load_alignment'] or min(lib['load_alignment']) < 16384:
                    raise ValueError(f'{name}: SONAME or 16-KB page alignment incorrect')
            if 'glslang_compile' not in info[MODULES[1]]['exports']:
                raise ValueError('glslang backend export missing')
            embedded = {'slang_getEmbeddedModule', 'slang_getEmbeddedModuleBuildTagString'}
            if not embedded.issubset(info[MODULES[2]]['exports']):
                raise ValueError('Embedded GLSL module exports missing')
            provenance = json.loads(archive.read('assets/runtime-slang/provenance.json'))
            if provenance['commit'] != '0fb6b7587a94fa33d88141ec9930766eedc6f59a':
                raise ValueError('Unexpected Slang source provenance')
        elif 'slang_createGlobalSession2' in slang['exports'] or any(n in info for n in MODULES[1:]):
            raise ValueError('Default build unexpectedly contains runtime Slang')
        return {name: {**item, 'exports': sorted(item['exports'])} for name, item in info.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('apk', type=Path)
    parser.add_argument('--readelf', type=Path, required=True)
    parser.add_argument('--runtime-slang', action='store_true')
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    info = verify(args.apk, args.readelf, args.runtime_slang)
    args.report.write_text(json.dumps(info, indent=2) + '\n')
    print(f'PASS: {len(info)} ARM64 libraries; .NET, Slang mode, dependencies and audio export checked')


if __name__ == '__main__':
    main()
