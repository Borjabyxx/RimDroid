#!/bin/sh
# Build RimDroidClrHooks.dll, the startup hook of RimWorld 1.7 on .NET (clrhooks/StartupHook.cs),
# against the .NET 10 Android runtime pack that the dotnetEngine build unpacks (fetchDotnetRuntime).
# The result is kept in the repository (app/src/dotnet/assets/dotnet/hooks), so ordinary builds need
# no C# compiler.
# usage: build_clrhooks.sh [framework-dir]
# CSC: compiler executable; for Roslyn hosted by dotnet set CSC_DLL and DOTNET (Unity's Editor has
# both: Data/DotNetSdkRoslyn/csc.dll and Data/NetCoreRuntime/dotnet).
set -eu
HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/../.." && pwd)
FW=${1:-$ROOT/app/build/dotnet/assets/dotnet/framework}
OUT=$ROOT/app/src/dotnet/assets/dotnet/hooks
test -f "$FW/System.Private.CoreLib.dll" || { echo "no .NET framework in $FW (build once with dotnetEngine=true)" >&2; exit 1; }
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
# A Windows compiler under Git Bash / MSYS wants Windows paths.
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }
REFS="$WORK/refs.rsp"
for f in "$FW"/*.dll; do printf '%s\n' "-r:\"$(native "$f")\"" >> "$REFS"; done
mkdir -p "$OUT"
set -- -nologo -noconfig -nostdlib+ -target:library -optimize+ -deterministic+ \
    "-out:$(native "$OUT/RimDroidClrHooks.dll")" "@$(native "$REFS")"
for f in "$HERE"/clrhooks/*.cs; do set -- "$@" "$(native "$f")"; done
if [ -n "${CSC_DLL:-}" ]; then
    "${DOTNET:-dotnet}" "$CSC_DLL" "$@"
else
    "${CSC:-csc}" "$@"
fi
echo "built $OUT/RimDroidClrHooks.dll"
