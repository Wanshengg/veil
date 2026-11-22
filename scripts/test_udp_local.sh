#!/usr/bin/env bash
# Локальная проверка UDP: сервер + клиент на localhost, DNS через SOCKS5 UDP.
set -euo pipefail
cd "$(dirname "$0")/.."
./bin/veil-server -addr 127.0.0.1:8443 -cert certs/cert.pem -key certs/key.pem \
  -psk testpsk -fallback 127.0.0.1:8080 >/tmp/udpsrv.log 2>&1 &
P1=$!
./bin/veil-client -socks 127.0.0.1:1085 -http "" -server 127.0.0.1:8443 \
  -sni localhost -psk testpsk -insecure >/tmp/udpcli.log 2>&1 &
P2=$!
trap 'kill $P1 $P2 2>/dev/null || true' EXIT
sleep 2

echo "== UDP DNS через туннель =="
python tools/test_udp.py 127.0.0.1 1085 8.8.8.8 53 || echo "FAIL"
echo "== TCP контроль =="
curl -s -o NUL -w "https example: %{http_code}\n" --max-time 20 --socks5-hostname 127.0.0.1:1085 https://example.com/
echo "== лог сервера =="
grep -E "udp|tunnel" /tmp/udpsrv.log | tail -4
