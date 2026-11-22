#!/usr/bin/env bash
set -u
cd "$(dirname "$0")/.."
./bin/veil-client -config "$LOCALAPPDATA/veil/veil.ini" >/tmp/dbg.log 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null' EXIT
sleep 2
echo "--- plain HTTP through our proxy (verbose) ---"
curl -sv --max-time 15 -x http://127.0.0.1:8081 http://example.com/ 2>&1 |
  grep -aE "^> |^< |curl:|Connected|error" | head -14
echo "--- https through our proxy ---"
curl -s -o NUL -w "https: %{http_code}\n" --max-time 20 -x http://127.0.0.1:8081 https://example.com/
echo "--- client log ---"
tail -8 /tmp/dbg.log
