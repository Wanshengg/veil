#!/usr/bin/env bash
# Публикация пула серверов: подписать, зашифровать, выложить на РФ-хост.
#
#   bash deploy/pool-push.sh
#
# Переменные (можно задать в окружении):
#   POOL_HOST   — ssh-хост раздатчика        (по умолчанию 203.0.113.13)
#   POOL_KEY    — ssh-ключ для него          (по умолчанию ~/.ssh/pool_host)
#   POOL_WWW    — каталог на сервере         (по умолчанию /var/www/api/static/data)
#   POOL_PROXY  — ProxyCommand, если сервер доступен только через туннель
#   SIGN_KEY    — приватный ключ подписи     (по умолчанию ~/.veil-keys/pool_ed25519.key)
#   VEIL_PSK    — PSK пула; если не задан, берётся из %LOCALAPPDATA%/veil/veil.ini
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# нативные пути для exe/scp (MSYS иначе превращает /y/... в Y:\y\...)
ROOTM="$(cygpath -m "$ROOT" 2>/dev/null || echo "$ROOT")"
HOMEM="$(cygpath -m "$HOME" 2>/dev/null || echo "$HOME")"
HOST="${POOL_HOST:-203.0.113.13}"
KEY="${POOL_KEY:-$HOMEM/.ssh/pool_host}"
WWW="${POOL_WWW:-/var/www/api/static/data}"
SIGN="${SIGN_KEY:-$HOMEM/.veil-keys/pool_ed25519.key}"
TOOL="$ROOTM/dist/pooltool.exe"
JSON="$ROOTM/pool/pool.json"

[ -f "$TOOL" ] || { echo "нет $TOOL — собери: tools/build_pooltool.sh"; exit 1; }
[ -f "$JSON" ] || { echo "нет $JSON"; exit 1; }
[ -f "$SIGN" ] || { echo "нет ключа подписи: $SIGN"; exit 1; }

if [ -z "${VEIL_PSK:-}" ]; then
  INI="$(cygpath -m "$LOCALAPPDATA" 2>/dev/null || echo "$LOCALAPPDATA")/veil/veil.ini"
  VEIL_PSK="$(sed -n 's/^[[:space:]]*psk[[:space:]]*=[[:space:]]*//p' "$INI" | head -1)"
fi
[ -n "${VEIL_PSK:-}" ] || { echo "не нашёл PSK (VEIL_PSK или veil.ini)"; exit 1; }

NAME="$(openssl rand -hex 16 2>/dev/null || python -c 'import secrets;print(secrets.token_hex(16))')"
BLOB="$ROOTM/dist/pool.bin"

"$TOOL" pack "$JSON" "$BLOB" "$SIGN" "$VEIL_PSK" >/dev/null
echo "собран блоб: $(wc -c <"$BLOB") байт"

SSH_OPTS=(-F /dev/null -o StrictHostKeyChecking=accept-new -o ConnectTimeout=25 -i "$KEY")
[ -n "${POOL_PROXY:-}" ] && SSH_OPTS+=(-o "ProxyCommand=$POOL_PROXY")

ssh "${SSH_OPTS[@]}" "root@$HOST" "mkdir -p $WWW"
scp "${SSH_OPTS[@]}" "$BLOB" "root@$HOST:$WWW/$NAME" >/dev/null
ssh "${SSH_OPTS[@]}" "root@$HOST" \
  "chown www-data:www-data '$WWW/$NAME' && chmod 644 '$WWW/$NAME' && ls -1t '$WWW' | tail -n +3 | xargs -r -I{} rm -f '$WWW/{}'"

URL="https://pool.example.com/static/data/$NAME"
printf '%s\n' "$URL" > "$ROOT/pool/CURRENT"
echo "$URL"
echo "проверка:"
curl -s -o NUL -w "  HTTP %{http_code} · %{size_download} байт · %{content_type}\n" --max-time 15 "$URL"
