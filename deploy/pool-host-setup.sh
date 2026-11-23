#!/usr/bin/env bash
# Раздатчик пула + потоковый релей на чистом Ubuntu/Debian VPS.
# Запускать на самом сервере от root:
#
#   DOMAIN=pool.example.ru RELAY_PORT=10000 RELAY_TARGET=1.2.3.4:8443 bash pool-host-setup.sh
#
# Что делает:
#   1. nginx + certbot + модуль stream;
#   2. Let's Encrypt на DOMAIN, HTTPS, http→https;
#   3. /static/data/ — каталог под блоб пула (без логов, без листинга, noindex);
#   4. релей RELAY_PORT → RELAY_TARGET (без расшифровки, долгие сессии).
set -euo pipefail

DOMAIN="${DOMAIN:?нужен DOMAIN}"
RELAY_PORT="${RELAY_PORT:-10000}"
RELAY_TARGET="${RELAY_TARGET:-}"
WWW="${WWW:-/var/www/api}"

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq nginx certbot python3-certbot-nginx libnginx-mod-stream

# ── статика под блоб пула ───────────────────────────────────────────────
install -d -m 755 "$WWW/static/data"
printf '<!doctype html><meta charset="utf-8"><title>ok</title><h1>ok</h1>\n' > "$WWW/index.html"
chown -R www-data:www-data "$WWW"

cat >/etc/nginx/sites-available/api <<CONF
server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN;
    root $WWW;
    index index.html;

    location / { try_files \$uri \$uri/ =404; }

    location /static/data/ {
        alias $WWW/static/data/;
        autoindex off;
        access_log off;
        default_type application/octet-stream;
        add_header X-Robots-Tag "noindex" always;
        add_header Cache-Control "public, max-age=3600" always;
    }
}
CONF
ln -sf /etc/nginx/sites-available/api /etc/nginx/sites-enabled/api
rm -f /etc/nginx/sites-enabled/default

# ── сертификат ──────────────────────────────────────────────────────────
if [ ! -s "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" ]; then
  systemctl stop nginx || true
  certbot certonly --standalone -d "$DOMAIN" --agree-tos --register-unsafely-without-email --non-interactive
  systemctl start nginx || true
fi

# ── HTTPS на том же конфиге ─────────────────────────────────────────────
certbot --nginx -d "$DOMAIN" --agree-tos --register-unsafely-without-email --redirect --non-interactive 2>/dev/null || true

# ── релей ───────────────────────────────────────────────────────────────
if [ -n "$RELAY_TARGET" ]; then
  mkdir -p /etc/nginx/stream.d
  cat >/etc/nginx/stream.d/relay.conf <<CONF
# релей: приём на $RELAY_PORT и передача в $RELAY_TARGET без расшифровки
server {
    listen $RELAY_PORT;
    proxy_pass $RELAY_TARGET;
    proxy_connect_timeout 10s;
    proxy_timeout 1h;
}
CONF
  if ! grep -q "stream.d" /etc/nginx/nginx.conf; then
    python3 - <<'PY'
p = "/etc/nginx/nginx.conf"
s = open(p).read()
if "stream {" not in s:
    s = s.replace("http {", "stream {\n    include /etc/nginx/stream.d/*.conf;\n}\n\nhttp {", 1)
    open(p, "w").write(s)
PY
  fi
fi

nginx -t
systemctl enable nginx >/dev/null 2>&1 || true
systemctl reload nginx

echo "---"
echo "каталог блоба : $WWW/static/data"
echo "сайт          : https://$DOMAIN/"
[ -n "$RELAY_TARGET" ] && echo "релей         : :$RELAY_PORT → $RELAY_TARGET"
echo "проверка      : curl -s -o /dev/null -w '%{http_code}\\n' https://$DOMAIN/"
