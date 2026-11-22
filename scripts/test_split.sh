#!/usr/bin/env bash
# Split-routing check: Russian hosts must go direct, the rest through the tunnel.
set -euo pipefail
cd "$(dirname "$0")/.."
SERVER="${1:?server:port}"
SNI="${2:?sni}"
PSK="${3:?psk}"

./bin/veil-client -socks 127.0.0.1:1090 -server "$SERVER" -sni "$SNI" -psk "$PSK" -fp chrome >/tmp/veilr.log 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null || true' EXIT
sleep 2

echo "== RU host (must be direct) =="
curl -s -o NUL -w "yandex.ru   http=%{http_code} ip=%{remote_ip}\n" --max-time 20 \
  --socks5-hostname 127.0.0.1:1090 http://yandex.ru/ || true
echo "== RU host by TLD (must be direct) =="
curl -s -o NUL -w "gosuslugi.ru http=%{http_code} ip=%{remote_ip}\n" --max-time 20 \
  --socks5-hostname 127.0.0.1:1090 https://www.gosuslugi.ru/ || true
echo "== foreign host (must be tunnel) =="
curl -s -o NUL -w "example.com  http=%{http_code} ip=%{remote_ip}\n" --max-time 25 \
  --socks5-hostname 127.0.0.1:1090 http://example.com/ || true
echo "== what the client decided =="
grep -E "direct ->|tunnel ->" /tmp/veilr.log | tail -6
