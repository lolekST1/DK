#!/usr/bin/env bash
# Compiles the game scripts against stub Unity types and runs the dig-loop smoke test.
# Needs the Mono C# compiler: apt-get install mono-mcs   (or use `csc` from .NET).
#
# This is a safety net for machines without Unity — it is NOT a replacement for
# pressing Play. Rendering, input and the WebGL build are not covered.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

echo "== type-checking runtime + editor scripts =="
mcs -target:library -langversion:latest \
    -nowarn:0108,0114,0660,0661,0649,0169,0067 \
    -out:"$out/all.dll" \
    "$here/UnityStubs.cs" "$here/UnityEditorStubs.cs" \
    "$root"/Assets/Scripts/*.cs "$root"/Assets/Editor/*.cs

echo "== running dig-loop smoke test =="
mcs -langversion:latest \
    -nowarn:0108,0114,0660,0661,0649,0169,0067 \
    -out:"$out/test.exe" \
    "$here/UnityStubs.cs" "$root"/Assets/Scripts/*.cs "$here/TestHarness.cs"

mono "$out/test.exe"
