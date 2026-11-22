#!/usr/bin/env bash
set -u
cd "$(dirname "$0")/.."
echo "--- что решил клиент для chrome-запроса ---"
grep -E "fp |tunnel ->|direct ->" /tmp/fp_chrome.log | tail -5
echo "--- speedtest и прочее через туннель ---"
./bin/veil-client -config "$LOCALAPPDATA/veil/veil.ini" -socks 127.0.0.1:1201 -http "" >/tmp/st.log 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null' EXIT
sleep 2
curl -s -o NUL -w "speedtest.net: %{http_code} tls=%{time_appconnect}s\n" --max-time 35 --socks5-hostname 127.0.0.1:1201 https://www.speedtest.net/
curl -s -o NUL -w "fast.com:      %{http_code}\n" --max-time 35 --socks5-hostname 127.0.0.1:1201 https://fast.com/
curl -s -o NUL -w "youtube:       %{http_code}\n" --max-time 35 --socks5-hostname 127.0.0.1:1201 https://www.youtube.com/
echo "--- log ---"
tail -6 /tmp/st.log
