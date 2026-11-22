#!/usr/bin/env bash
# Probes the fallback with different client behaviours, showing raw responses.
set -euo pipefail
cd "$(dirname "$0")/.."
python -m http.server 8080 --bind 127.0.0.1 --directory testroot >/dev/null 2>&1 &
P1=$!
./bin/veil-server -addr 127.0.0.1:8443 -cert certs/cert.pem -key certs/key.pem \
  -psk testpsk -fallback 127.0.0.1:8080 >/tmp/vs.log 2>&1 &
P2=$!
trap 'kill $P1 $P2 2>/dev/null || true' EXIT
sleep 1.2

echo "--- a) GET over TLS (openssl) ---"
printf 'GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' |
  openssl s_client -quiet -connect 127.0.0.1:8443 -servername localhost 2>/dev/null |
  head -3 || echo "(empty)"

echo "--- b) random bytes after TLS (looks like a broken client) ---"
printf 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' |
  openssl s_client -quiet -connect 127.0.0.1:8443 -servername localhost 2>/dev/null |
  head -3 || echo "(empty)"

echo "--- c) plain TLS handshake only ---"
echo | openssl s_client -connect 127.0.0.1:8443 -servername localhost 2>/dev/null |
  grep -E "subject=|issuer=|Cipher|ALPN" | head -4

echo "--- d) TLS with SNI that does not match the cert ---"
echo | openssl s_client -connect 127.0.0.1:8443 -servername wrong.example 2>&1 |
  grep -E "verify|alert|subject=" | head -3

echo "--- server log ---"
tail -6 /tmp/vs.log
