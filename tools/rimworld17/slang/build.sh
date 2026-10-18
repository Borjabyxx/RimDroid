#!/usr/bin/env bash
# Build unmodified, pinned upstream Slang. No game files or downloaded binaries.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../../.." && pwd)
work=${SLANG_WORK_DIR:-$root/_slang-build}
src=${SLANG_SOURCE_DIR:-$work/source}
ndk=${ANDROID_NDK_HOME:-${ANDROID_HOME:?Set ANDROID_HOME}/ndk/27.0.12077973}
cmake=${CMAKE:-${ANDROID_HOME}/cmake/3.22.1/bin/cmake}
ninja=${NINJA:-${ANDROID_HOME}/cmake/3.22.1/bin/ninja}
jobs=${BUILD_JOBS:-4}
commit=0fb6b7587a94fa33d88141ec9930766eedc6f59a
mkdir -p "$work"
if [[ ! -d "$src/.git" ]]; then
    git clone --branch v2026.19 --depth 1 https://github.com/shader-slang/slang.git "$src"
fi
[[ $(git -C "$src" rev-parse HEAD) == "$commit" ]] || { echo 'Wrong Slang commit' >&2; exit 1; }
git -C "$src" diff --quiet HEAD -- || { echo 'Slang source has local changes' >&2; exit 1; }
git -C "$src" submodule update --init --depth 1 \
    external/glslang external/spirv-tools external/spirv-headers external/miniz \
    external/lz4 external/unordered_dense external/vulkan external/lua external/cmark \
    external/fast_float external/nlohmann-json
common=(-G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" -DCMAKE_BUILD_TYPE=Release
    -DSLANG_SLANG_LLVM_FLAVOR=DISABLE -DSLANG_ENABLE_GFX=OFF -DSLANG_ENABLE_SLANG_RHI=OFF
    -DSLANG_ENABLE_TESTS=OFF -DSLANG_ENABLE_EXAMPLES=OFF -DSLANG_ENABLE_SLANGD=OFF
    -DSLANG_ENABLE_REPLAYER=OFF -DSLANG_ENABLE_SLANGI=OFF -DSLANG_ENABLE_SLANGRT=OFF
    -DSLANG_ENABLE_RELEASE_DEBUG_INFO=OFF -DSLANG_ENABLE_XLIB=OFF -DSLANG_ENABLE_CUDA=OFF
    -DSLANG_ENABLE_OPTIX=OFF -DSLANG_ENABLE_NVAPI=OFF -DSLANG_ENABLE_AFTERMATH=OFF
    -DSLANG_ENABLE_DXIL=OFF)
"$cmake" -S "$src" -B "$work/host" "${common[@]}"
"$cmake" --build "$work/host" --parallel "$jobs" --target all-generators slangc slang-glslang slang-glsl-module spirv-val
"$cmake" --install "$work/host" --config Release --prefix "$work/generators" --component generators
"${CXX:-c++}" -std=c++17 "$here/api_smoke.cpp" -I"$src/include" \
    -L"$work/host/Release/lib" -Wl,-rpath,"$work/host/Release/lib" -lslang-compiler \
    -o "$work/api-smoke-host"
"$work/api-smoke-host"
"$cmake" -S "$here" -B "$work/android" "${common[@]}" \
    "-DSLANG_SOURCE_DIR=$src" "-DSLANG_GENERATORS_PATH=$work/generators/bin" \
    "-DCMAKE_TOOLCHAIN_FILE=$ndk/build/cmake/android.toolchain.cmake" \
    -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-30 -DANDROID_STL=c++_shared \
    -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON -DSLANG_ENABLE_SLANG_PROXY=OFF \
    -DCMAKE_SHARED_LINKER_FLAGS=-Wl,-z,max-page-size=16384 \
    -DCMAKE_MODULE_LINKER_FLAGS=-Wl,-z,max-page-size=16384
"$cmake" --build "$work/android" --parallel "$jobs" --target slang slang-glslang slang-glsl-module rimdroid-slang-smoke
dest="$root/app/build/runtime-slang"
mkdir -p "$dest/jniLibs/arm64-v8a" "$dest/assets/runtime-slang/licenses"
for name in libslang.so libslang-glslang-2026.19.so libslang-glsl-module-2026.19.so; do
    install -m 644 "$work/android/upstream/Release/lib/$name" "$dest/jniLibs/arm64-v8a/$name"
done
# Retain upstream legal notices in the APK, alongside provenance. Dependencies
# remain pinned by Slang's gitlinks; no third-party source is changed or vendored.
cp "$src/LICENSE" "$dest/assets/runtime-slang/licenses/Slang.txt"
python3 - "$src" "$dest" "$commit" <<'PY'
import hashlib, json, pathlib, shutil, subprocess, sys
src, dest = map(pathlib.Path, sys.argv[1:3])
licenses = dest/'assets/runtime-slang/licenses'
shutil.copytree(src/'LICENSES', licenses/'Slang-LICENSES', dirs_exist_ok=True)
shutil.copy2(src/'external/lz4/lib/LICENSE', licenses/'lz4-lib-LICENSE')
lua = (src/'external/lua/lua.h').read_text()
(licenses/'lua-LICENSE').write_text(lua[lua.rindex('/******************************************************************************'):lua.rindex('#endif')])
for dep in ('glslang','spirv-tools','spirv-headers','miniz','lz4','unordered_dense','lua','cmark','fast_float','nlohmann-json'):
    for p in (src/'external'/dep).iterdir():
        if p.is_file() and p.name.lower().startswith(('license','copying','copyright')):
            shutil.copy2(p, licenses/(dep+'-'+p.name))
info = {'version':'2026.19', 'commit':sys.argv[3], 'android_api':30,
        'validation':'Experimental: Waylith managed ABI and device execution not verified',
        'submodules':subprocess.check_output(['git','-C',str(src),'submodule','status']).decode().splitlines(),
        'libraries':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (dest/'jniLibs/arm64-v8a').glob('*.so')}}
(dest/'assets/runtime-slang/provenance.json').write_text(json.dumps(info, indent=2)+'\n')
PY
echo "Prepared real Slang libraries in $dest; Android API smoke binary: $work/android/Release/bin/rimdroid-slang-smoke"
