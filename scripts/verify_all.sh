#!/usr/bin/env bash
# Full independent check of the deployed tunnel (run without a VPN).
set -u
cd "$(dirname "$0")/.."
HOST=se.example.com
PSK='REDACTED-PSK'

echo "== 1. DNS =="
python -c "import socket;print(socket.gethostbyname('$HOST'))" 2>&1

echo "== 2. TLS from this host (RU, direct) =="
echo | timeout 15 openssl s_client -connect "$HOST:443" -servername "$HOST" 2>/dev/null |
  grep -E "subject=|issuer=|Protocol *:|Cipher *:|ALPN" | head -6

echo "== 3. HTTP (fallback page) =="
curl -s -o NUL -w "code=%{http_code} tls=%{time_appconnect}s total=%{time_total}s ip=%{remote_ip}\n" --max-time 20 "https://$HOST/"

echo "== 4. garbage probe inside TLS (should still look like a site) =="
printf 'GET / HTTP/1.1\r\nHost: %s\r\nConnection: close\r\n\r\n' "$HOST" |
  timeout 10 openssl s_client -quiet -connect "$HOST:443" -servername "$HOST" 2>/dev/null | head -2

echo "== 5. public CT logs (is the cert really trusted & public) =="
curl -s --max-time 20 "https://crt.sh/?q=$HOST&output=json" | head -c 300; echo

echo "== 6. tunnel: exit ip / country =="
./bin/veil-client -socks 127.0.0.1:1091 -server "$HOST:443" -sni "$HOST" -psk "$PSK" >/tmp/verify.log 2>&1 &
CLI=$!
trap 'kill $CLI 2>/dev/null' EXIT
sleep 2
curl -s --max-time 25 --socks5-hostname 127.0.0.1:1091 "http://ip-api.com/json/?fields=status,country,countryCode,query"; echo

echo "== 7. split routing =="
curl -s -o NUL -w "gosuslugi.ru %{http_code}\n" --max-time 20 --socks5-hostname 127.0.0.1:1091 https://www.gosuslugi.ru/
curl -s -o NUL -w "example.com  %{http_code}\n" --max-time 25 --socks5-hostname 127.0.0.1:1091 http://example.com/
grep -E "direct ->|tunnel ->" /tmp/verify.log | tail -3

echo "== 8. throughput =="
curl -s -o NUL --max-time 60 -w "5 MiB: %{speed_download} B/s, %{time_total}s\n" \
  --socks5-hostname 127.0.0.1:1091 "https://speed.cloudflare.com/__down?bytes=5000000"

echo "== 9. survival probe so far =="
echo "ok=$(grep -c OK probe.out 2>/dev/null) fail=$(grep -c FAIL probe.out 2>/dev/null)"
