#!/usr/bin/env bash
# veil bootstrap — готовит сервер к работе из коробки, поверх уже загруженного
# бинарника /tmp/veil-server. Идемпотентно: повторный запуск не ломает установку.
#
#   bash bootstrap.sh --domain veil.example.com      # LE-сертификат (лучшая маскировка)
#   bash bootstrap.sh --no-domain                    # самоподписанный + pin
#   опции: --ports 443,8443,2083,9443  --name default  --fallback http://127.0.0.1:8080
#
# В конце печатает одну строку VEIL_KEY=<ключ> — её приложение превращает в профиль.
set -euo pipefail

DOMAIN=""
PORTS="443,8443,2083,9443"
KEY_NAME="default"
FALLBACK="127.0.0.1:8080"
BIN_SRC="/tmp/veil-server"

while [ $# -gt 0 ]; do
  case "$1" in
    --domain) DOMAIN="${2:-}"; shift 2 ;;
    --no-domain) DOMAIN=""; shift ;;
    --ports) PORTS="${2:-}"; shift 2 ;;
    --name) KEY_NAME="${2:-default}"; shift 2 ;;
    --fallback) FALLBACK="${2:-127.0.0.1:8080}"; shift 2 ;;
    --bin) BIN_SRC="${2:-/tmp/veil-server}"; shift 2 ;;
    *) echo "неизвестный аргумент: $1" >&2; exit 2 ;;
  esac
done

say() { echo "== $*"; }

[ "$(id -u)" = "0" ] || { echo "нужен root" >&2; exit 1; }
[ -f "$BIN_SRC" ] || { echo "нет бинарника $BIN_SRC" >&2; exit 1; }

say "пакеты"
export DEBIAN_FRONTEND=noninteractive
command -v openssl >/dev/null || apt-get install -y -qq openssl >/dev/null
command -v curl    >/dev/null || apt-get install -y -qq curl >/dev/null

say "пользователь и каталоги"
id veil >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin veil
install -d -o veil -g veil -m 0750 /opt/veil /opt/veil/certs
install -o root -g root -m 0755 "$BIN_SRC" /opt/veil/veil-server

say "ключи"
if [ ! -f /opt/veil/keys.txt ]; then
  # персональный ключ: 32 символа из base62
  PSK="$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 32)"
  printf '%s:%s\n' "$KEY_NAME" "$PSK" > /opt/veil/keys.txt
  chmod 0600 /opt/veil/keys.txt
else
  # сервер уже настроен — берём существующий ключ, чтобы не перевыпускать
  PSK="$(head -1 /opt/veil/keys.txt | cut -d: -f2-)"
fi
[ -n "$PSK" ] || { echo "пустой PSK" >&2; exit 1; }

say "сертификат"
IDENT="${DOMAIN:-$(hostname -f 2>/dev/null || echo veil.local)}"
if [ -n "$DOMAIN" ]; then
  if [ ! -s /opt/veil/certs/fullchain.pem ] || ! openssl x509 -checkend 604800 -noout -in /opt/veil/certs/fullchain.pem 2>/dev/null; then
    command -v certbot >/dev/null || apt-get install -y -qq certbot >/dev/null
    if ! certbot certonly --standalone -n --agree-tos --register-unsafely-without-email -d "$DOMAIN" >/tmp/certbot.log 2>&1; then
      echo "certbot не смог выдать сертификат (DNS должен указывать на этот сервер):" >&2
      tail -5 /tmp/certbot.log >&2
      exit 3
    fi
    install -m 0644 "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" /opt/veil/certs/fullchain.pem
    install -m 0600 "/etc/letsencrypt/live/$DOMAIN/privkey.pem"  /opt/veil/certs/privkey.pem
  fi
else
  if [ ! -s /opt/veil/certs/fullchain.pem ]; then
    openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=$IDENT" \
      -addext "subjectAltName=DNS:$IDENT,IP:$(curl -fsS --max-time 5 https://api.ipify.org 2>/dev/null || echo 127.0.0.1)" \
      -keyout /opt/veil/certs/privkey.pem -out /opt/veil/certs/fullchain.pem >/dev/null 2>&1
  fi
fi
chown -R veil:veil /opt/veil/certs

# ключи клиентов: root:veil 640 — сервис читает, менять может только root
[ -f /opt/veil/keys.txt ] || : > /opt/veil/keys.txt
chown root:veil /opt/veil/keys.txt
chmod 640 /opt/veil/keys.txt
usermod -s /usr/sbin/nologin veil 2>/dev/null || true

say "unit"
cat >/etc/systemd/system/veil.service <<UNIT
[Unit]
Description=veil tunnel server
After=network-online.target
Wants=network-online.target

[Service]
User=veil
Group=veil
ExecStart=/opt/veil/veil-server -addr :$(echo "$PORTS" | sed 's/,/,:/g; s/^/:/') \\
  -cert /opt/veil/certs/fullchain.pem \\
  -key /opt/veil/certs/privkey.pem \\
  -psks /opt/veil/keys.txt \\
  -fallback $FALLBACK
ExecReload=/bin/kill -HUP $MAINPID
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE
NoNewPrivileges=yes
ProtectSystem=strict
ProtectHome=yes
PrivateTmp=yes
PrivateDevices=yes
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectControlGroups=yes
RestrictSUIDSGID=yes
LockPersonality=yes
ReadOnlyPaths=/opt/veil
Restart=always
RestartSec=3
ExecReload=/bin/kill -HUP \$MAINPID

[Install]
WantedBy=multi-user.target
UNIT

say "firewall"
if command -v ufw >/dev/null; then
  for p in ${PORTS//,/ }; do ufw allow "$p/tcp" >/dev/null 2>&1 || true; done
  ufw allow 80/tcp >/dev/null 2>&1 || true    # для certbot --standalone
fi

say "заглушка для fallback"
if [ -f /etc/nginx/sites-enabled/default ] || command -v nginx >/dev/null; then
  mkdir -p /var/www/veil-fallback
  [ -f /var/www/veil-fallback/index.html ] || cat >/var/www/veil-fallback/index.html <<'HTML'
<!doctype html><meta charset="utf-8"><title>Welcome</title>
<h1>It works!</h1><p>This is the default page for this server.</p>
HTML
  cat >/etc/nginx/sites-available/veil-fallback <<'NGX'
server {
  listen 127.0.0.1:8080;
  root /var/www/veil-fallback;
  index index.html;
  location / { try_files $uri $uri/ =404; }
}
NGX
  ln -sf /etc/nginx/sites-available/veil-fallback /etc/nginx/sites-enabled/veil-fallback
  nginx -t >/dev/null 2>&1 && systemctl reload nginx 2>/dev/null || true
fi

say "запуск"
systemctl daemon-reload
systemctl enable veil >/dev/null 2>&1 || true
systemctl restart veil
sleep 2
systemctl is-active --quiet veil || { echo "служба не поднялась: journalctl -u veil -n 30" >&2; exit 4; }

say "проверка"
FIRST_PORT="${PORTS%%,*}"
if timeout 10 openssl s_client -connect "127.0.0.1:$FIRST_PORT" -servername "$IDENT" </dev/null >/dev/null 2>&1; then
  echo "TLS отвечает на :$FIRST_PORT"
else
  echo "TLS не ответил на :$FIRST_PORT" >&2
fi

# ключ для клиента: server:port|sni|fp|psk
KEY="veil://$(printf '%s:%s|%s|chrome|%s' "$IDENT" "$FIRST_PORT" "$IDENT" "$PSK" | base64 -w0 | tr '+/' '-_' | tr -d '=')"
echo "VEIL_KEY=$KEY"
echo "VEIL_PORT=$FIRST_PORT"
echo "VEIL_NAME=$IDENT"
