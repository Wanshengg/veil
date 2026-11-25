#!/usr/bin/env bash
# Дельта-патчи для автообновления: от прошлых релизов к текущему exe.
# Использование: tools/make_delta.sh v0.6.9 v0.6.8 v0.6.7
# Печатает готовые строки манифеста "patch_<версия>": "<url>".
set -euo pipefail
cd "$(dirname "$0")/.."

NEW=${NEW:-dist/veil.exe}
ZSTD=${ZSTD:-third_party/zstd.exe}
TAG=${TAG:-}
OUT=dist/release/patches
mkdir -p "$OUT"

TO=$(grep -o 'AppVersion = "[^"]*"' client/win/VeilApp.cs | head -1 | cut -d'"' -f2)
[ -n "$TAG" ] || TAG="v$TO"

for v in "$@"; do
  short=${v#v}
  tmp=$(mktemp -d)
  gh release download "$v" --pattern veil.exe --dir "$tmp" --clobber >/dev/null 2>&1 || {
    echo "  пропуск $v — нет ассета"; rm -rf "$tmp"; continue; }
  out="$OUT/veil-patch-$TO-from-$short.zst"
  "$ZSTD" -q -19 --patch-from="$tmp/veil.exe" "$NEW" -o "$out" -f
  size=$(stat -c %s "$out" 2>/dev/null || stat -f %z "$out")
  awk -v a="$short" -v b="$TO" -v s="$size" 'BEGIN { printf "  %-9s → %s   %6.2f МБ\n", a, b, s/1048576 }' 
  echo "  \"patch_$short\": \"https://github.com/Wanshengg/veil/releases/download/$TAG/$(basename "$out")\","
  rm -rf "$tmp"
done
