#!/usr/bin/env bash
# Local end-to-end check: tunnel works, fallback serves a real page,
# binary integrity survives a 1 MiB transfer.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH=/c/Users/RS34/tools/go/bin:$PATH
TMP="${TMPDIR:-/tmp}"
PSK="${PSK:-testpsk}"

go build -o bin/veil-server ./cmd/veil-server
go build -o bin/veil-client ./cmd/veil-client
go build -o bin/gencert ./cmd/gencert
./bin/gencert -out certs -dns localhost,veil.test -ips 127.0.0.1 >/dev/null

mkdir -p testroot
echo '<h1>fallback origin ok</h1>' > testroot/index.html
head -c 1048576 /dev/urandom > testroot/big.bin

python -m http.server 8080 --bind 127.0.0.1 --directory testroot >"$TMP/origin.log" 2>&1 &
P1=$!
./bin/veil-server -addr 127.0.0.1:8443 -cert certs/cert.pem -key certs/key.pem \
  -psk "$PSK" -fallback 127.0.0.1:8080 >"$TMP/veil-server.log" 2>&1 &
P2=$!
./bin/veil-client -socks 127.0.0.1:1080 -server 127.0.0.1:8443 -sni localhost \
  -psk "$PSK" -insecure >"$TMP/veil-client.log" 2>&1 &
P3=$!
trap 'kill $P1 $P2 $P3 2>/dev/null || true' EXIT
sleep 2

echo "=== 1. HTTP through the tunnel (SOCKS5) ==="
curl -s --max-time 10 --socks5-hostname 127.0.0.1:1080 http://127.0.0.1:8080/ | head -2

echo "=== 2. Fallback: ordinary HTTPS client sees the real site ==="
curl -sk --max-time 10 https://127.0.0.1:8443/ | head -2

echo "=== 3. Fallback: raw garbage probe ==="
printf 'GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' |
  { command -v openssl >/dev/null && openssl s_client -quiet -connect 127.0.0.1:8443 -servername localhost 2>/dev/null || cat; } | head -2

echo "=== 4. 1 MiB integrity through the tunnel ==="
local_hash=$(sha256sum testroot/big.bin | cut -c1-16)
remote_hash=$(curl -s --max-time 30 --socks5-hostname 127.0.0.1:1080 http://127.0.0.1:8080/big.bin | sha256sum | cut -c1-16)
echo "local: $local_hash  remote: $remote_hash"

echo "=== 5. Real internet through the tunnel ==="
curl -s --max-time 20 --socks5-hostname 127.0.0.1:1080 http://example.com | grep -o '<title>.*</title>' || echo "(no external net)"

echo "=== server log ==="
tail -6 "$TMP/veil-server.log"
