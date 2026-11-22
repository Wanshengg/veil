#!/usr/bin/env bash
# A/B: does the same server tunnel fine with a different ClientHello?
set -u
cd "$(dirname "$0")/.."
INI="$LOCALAPPDATA/veil/veil.ini"
run() {
  fp=$1; port=$2
  ./bin/veil-client -config "$INI" -socks "127.0.0.1:$port" -http "" -fp "$fp" >"/tmp/fp_$fp.log" 2>&1 &
  pid=$!
  sleep 2
  code=$(curl -s -o NUL -w '%{http_code}' --max-time 20 --socks5-hostname "127.0.0.1:$port" http://example.com/)
  printf '%-8s -> %s\n' "$fp" "$code"
  kill $pid 2>/dev/null
  sleep 1
}
run chrome 1101
run firefox 1102
run safari 1103
run edge 1104
echo "--- errors ---"
grep -hE "tls:|veil:|timeout|reset" /tmp/fp_*.log | tail -6
