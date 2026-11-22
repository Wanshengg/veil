#!/usr/bin/env bash
# Verifies all local front doors: SOCKS5, SOCKS4, SOCKS4a, HTTP CONNECT, plain HTTP.
set -u
cd "$(dirname "$0")/.."
INI="$LOCALAPPDATA/veil/veil.ini"
./bin/veil-client -config "$INI" >/tmp/doors.log 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null' EXIT
sleep 2

echo "socks5  http : $(curl -s -o NUL -w '%{http_code}' --max-time 20 --socks5-hostname 127.0.0.1:1080 http://example.com/)"
echo "socks5  https: $(curl -s -o NUL -w '%{http_code}' --max-time 20 --socks5-hostname 127.0.0.1:1080 https://example.com/)"
echo "socks4  http : $(curl -s -o NUL -w '%{http_code}' --max-time 20 --socks4 127.0.0.1:1080 http://example.com/)"
echo "socks4a http : $(curl -s -o NUL -w '%{http_code}' --max-time 20 --socks4a 127.0.0.1:1080 https://example.com/)"
echo "http    http : $(curl -s -o NUL -w '%{http_code}' --max-time 20 -x http://127.0.0.1:8081 http://example.com/)"
echo "http    https: $(curl -s -o NUL -w '%{http_code}' --max-time 20 -x http://127.0.0.1:8081 https://example.com/)"
echo "http  ru direct: $(curl -s -o NUL -w '%{http_code}' --max-time 20 -x http://127.0.0.1:8081 https://www.gosuslugi.ru/)"
echo "--- log ---"; grep -E "direct ->|tunnel ->|http|socks" /tmp/doors.log | tail -8
