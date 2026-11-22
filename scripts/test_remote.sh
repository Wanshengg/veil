#!/usr/bin/env bash
# Remote smoke test: tunnel to the deployed server, then fetch through it.
# Usage: bash scripts/test_remote.sh <server:port> <sni> <psk>
set -euo pipefail
cd "$(dirname "$0")/.."
SERVER="${1:?server:port}"
SNI="${2:?sni}"
PSK="${3:?psk}"
PORT=1089

./bin/veil-client -socks "127.0.0.1:$PORT" -server "$SERVER" -sni "$SNI" -psk "$PSK" -fp chrome >/tmp/veilc.log 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null || true' EXIT
sleep 2

echo "== plain HTTPS to the site (should be the fallback page) =="
curl -s --max-time 25 -w "\n[code=%{http_code} ip=%{remote_ip} tls=%{time_appconnect}s]\n" "https://$SNI/" | head -5

echo "== HTTP through the tunnel =="
curl -s --max-time 25 --socks5-hostname "127.0.0.1:$PORT" http://example.com | grep -o "<title>.*</title>"

echo "== HTTPS through the tunnel =="
curl -s --max-time 25 --socks5-hostname "127.0.0.1:$PORT" https://example.com | grep -o "<title>.*</title>"

echo "== speed: 5 MiB through the tunnel =="
curl -s --max-time 60 --socks5-hostname "127.0.0.1:$PORT" -o NUL -w "downloaded=%{size_download}B speed=%{speed_download}B/s total=%{time_total}s\n" \
  "https://speed.cloudflare.com/__down?bytes=5000000"

echo "== client log =="; tail -3 /tmp/veilc.log || true
