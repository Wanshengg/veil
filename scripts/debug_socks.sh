#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
python -m http.server 8080 --bind 127.0.0.1 --directory testroot >/dev/null 2>&1 &
P1=$!
./bin/veil-server -addr 127.0.0.1:8443 -cert certs/cert.pem -key certs/key.pem \
  -psk testpsk -fallback 127.0.0.1:8080 >/tmp/vs.log 2>&1 &
P2=$!
./bin/veil-client -socks 127.0.0.1:1081 -server 127.0.0.1:8443 -sni localhost \
  -psk testpsk -insecure >/tmp/vc.log 2>&1 &
P3=$!
trap 'kill $P1 $P2 $P3 2>/dev/null || true' EXIT
sleep 1.5
python tools/debug_socks.py 1081 127.0.0.1:8080 || true
echo "--- client log ---"; cat /tmp/vc.log
echo "--- server log ---"; cat /tmp/vs.log
