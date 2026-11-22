#!/usr/bin/env bash
# Verifies the survival probe against a local server (2 cycles, fast).
set -euo pipefail
cd "$(dirname "$0")/.."
python -m http.server 8080 --bind 127.0.0.1 --directory testroot >/dev/null 2>&1 &
P1=$!
./bin/veil-server -addr 127.0.0.1:8443 -cert certs/cert.pem -key certs/key.pem \
  -psk testpsk -fallback 127.0.0.1:8080 >/dev/null 2>&1 &
P2=$!
trap 'kill $P1 $P2 2>/dev/null || true' EXIT
sleep 1.5
python tools/probe.py -server 127.0.0.1:8443 -sni localhost -psk testpsk \
  -interval 2 -rounds 2 -log probe.jsonl -url 127.0.0.1:8080 -insecure
echo "--- probe.jsonl ---"
cat probe.jsonl
