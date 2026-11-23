#!/usr/bin/env bash
# Сборка утилиты пула (подпись + шифрование блобов).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
T="$LOCALAPPDATA/Temp/poolbuild"
CSC="/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
[ -x "$CSC" ] || CSC="C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"

rm -rf "$T"; mkdir -p "$T"
cp "$ROOT/client/win/PoolCodec.cs" "$ROOT/tools/pooltool/PoolTool.cs" "$T/"
cp "$ROOT/client/win/sshnet/BouncyCastle.Cryptography.dll" "$T/"

(cd "$T" && "$CSC" /nologo /target:exe /platform:x64 /codepage:65001 \
  /out:pooltool.exe PoolCodec.cs PoolTool.cs /r:BouncyCastle.Cryptography.dll >/dev/null)

mkdir -p "$ROOT/dist"
cp "$T/pooltool.exe" "$ROOT/dist/pooltool.exe"
echo "готово: dist/pooltool.exe"
