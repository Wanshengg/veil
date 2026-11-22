#!/usr/bin/env bash
# Runs the packaged Windows client with veil.ini and checks the tunnel.
set -euo pipefail
cd "$(dirname "$0")/.."
PKG=dist/veil-windows
"$PKG/veil-client.exe" -config "$PKG/veil.ini" >/tmp/veilpkg.log 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null || true' EXIT
sleep 2
echo "== through the tunnel (config-driven) =="
curl -s --max-time 20 --socks5-hostname 127.0.0.1:1080 https://example.com | grep -o "<title>.*</title>" || echo "FAIL"
echo "== exit ip as seen by the server =="
curl -s --max-time 20 --socks5-hostname 127.0.0.1:1080 https://api.ipify.org; echo
echo "== client log =="; tail -3 /tmp/veilpkg.log
