#!/usr/bin/env bash
# Cross-builds veil-server for Linux and installs it on the VPS.
# Usage: bash deploy/push.sh user@host
set -euo pipefail
cd "$(dirname "$0")/.."
HOST="${1:?usage: deploy/push.sh user@host}"
[ -x /c/Users/RS34/tools/go/bin/go.exe ] && PATH=/c/Users/RS34/tools/go/bin:$PATH
mkdir -p dist

GOOS=linux GOARCH=amd64 CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" \
  -o dist/veil-server-linux-amd64 ./cmd/veil-server
tar czf dist/veil-deploy.tar.gz -C dist veil-server-linux-amd64 -C "$PWD" deploy/install.sh

scp -q dist/veil-deploy.tar.gz "$HOST:/tmp/"
ssh "$HOST" 'set -e; tar xzf /tmp/veil-deploy.tar.gz -C /tmp; sudo bash /tmp/deploy/install.sh /tmp/veil-server-linux-amd64'
