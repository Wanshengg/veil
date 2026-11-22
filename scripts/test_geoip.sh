#!/usr/bin/env bash
# Проверка geoip-маршрутизации по «голому» IP — как это видит TUN.
set -u
cd "$(dirname "$0")/.."
PSK="${VEIL_PSK:-$(grep -oE '^psk[[:space:]]*=[[:space:]]*.*' "$LOCALAPPDATA/veil/veil.ini" | cut -d= -f2 | tr -d ' \r')}"

./bin/veil-client.exe -socks 127.0.0.1:1095 -http "" -server se.example.com:443 \
  -sni se.example.com -psk "$PSK" >/tmp/geo2.log 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null' EXIT
sleep 3

echo "== RU IP напрямую (yandex 77.88.55.242) =="
curl -s -o NUL -w "  http=%{http_code} ip=%{remote_ip}\n" --max-time 20 --socks5-hostname 127.0.0.1:1095 http://77.88.55.242/ || true

echo "== зарубежный IP (1.1.1.1) =="
curl -s -o NUL -w "  http=%{http_code} ip=%{remote_ip}\n" --max-time 20 --socks5-hostname 127.0.0.1:1095 http://1.1.1.1/ || true

echo "== что решил клиент =="
grep -aE "direct ->|tunnel ->" /tmp/geo2.log | tail -4
echo "== стартовая строка =="
grep -a "ru-prefixes" /tmp/geo2.log | head -1
