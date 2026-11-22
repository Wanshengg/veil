#!/usr/bin/env bash
# Reproduces the "connected but nothing opens" symptom.
set -u
cd "$(dirname "$0")/.."
INI="$LOCALAPPDATA/veil/veil.ini"
echo "--- ini ---"; sed 's/psk.*/psk = ***/' "$INI"

./bin/veil-client -config "$INI" >/tmp/repro.log 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null' EXIT
sleep 2
echo "--- listening? ---"; netstat -ano | grep -a "LISTENING" | grep -a ":1080 " | head -2
echo "--- alive? ---"; kill -0 $PID 2>/dev/null && echo yes || echo "dead"
echo "--- socks5 (correct) ---"
curl -s -o NUL -w "  code=%{http_code}\n" --max-time 20 --socks5-hostname 127.0.0.1:1080 http://example.com/ || true
echo "--- socks4 (what a browser may use when proxy is 'socks=') ---"
curl -s -o NUL -w "  code=%{http_code}\n" --max-time 12 --socks4 127.0.0.1:1080 http://example.com/ || true
echo "--- socks4a ---"
curl -s -o NUL -w "  code=%{http_code}\n" --max-time 12 --socks4a 127.0.0.1:1080 http://example.com/ || true
echo "--- http proxy on 1080 (browsers sometimes treat socks= as http) ---"
curl -s -o NUL -w "  code=%{http_code}\n" --max-time 12 -x http://127.0.0.1:1080 http://example.com/ || true
echo "--- client log ---"; tail -6 /tmp/repro.log
