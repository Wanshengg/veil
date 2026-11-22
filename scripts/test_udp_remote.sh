#!/usr/bin/env bash
# UDP-проверка против боевого сервера: DNS через туннель.
set -u
cd "$(dirname "$0")/.."
PSK="${VEIL_PSK:-$(grep -oE '^psk[[:space:]]*=[[:space:]]*.*' "$LOCALAPPDATA/veil/veil.ini" | cut -d= -f2 | tr -d ' \r')}"
./bin/veil-client -socks 127.0.0.1:1092 -http "" -server se.example.com:443 \
  -sni se.example.com -psk "$PSK" >/tmp/udpreal.log 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null' EXIT
sleep 2
echo "== UDP DNS 8.8.8.8 через боевой сервер =="
python tools/test_udp.py 127.0.0.1 1092 8.8.8.8 53 || echo "FAIL"
echo "== UDP DNS 1.1.1.1 =="
python tools/test_udp.py 127.0.0.1 1092 1.1.1.1 53 || echo "FAIL"
echo "== TCP контроль =="
curl -s -o NUL -w "https example: %{http_code}\n" --max-time 25 --socks5-hostname 127.0.0.1:1092 https://example.com/
echo "== лог клиента =="; grep -E "udp|tunnel" /tmp/udpreal.log | tail -5
