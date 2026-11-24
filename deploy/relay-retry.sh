#!/bin/bash
# deploy/relay-retry.sh — релеи на РУ-сервере с повтором апстрима.
# Запуск на РУ: bash relay-retry.sh (нужен nginx с модулем stream)
# Релеи с повтором: если первый апстрим не ответил, nginx пробует второй.
set -e

mkdir -p /etc/nginx/stream.d

cat > /etc/nginx/stream.d/relay.conf <<'CONF'
# релеи: приём на порту и передача без расшифровки
# upstream с двумя адресами одного сервера: nginx повторяет попытку на втором,
# если первый не ответил за proxy_connect_timeout

upstream veil_se {
    server 203.0.113.14:8443 max_fails=0;
    server 203.0.113.14:443  max_fails=0;
}
upstream veil_fi {
    server 203.0.113.11:8443 max_fails=0;
    server 203.0.113.11:443  max_fails=0;
}

server {
    listen 10000;
    proxy_pass veil_se;
    proxy_connect_timeout 5s;
    proxy_timeout 1h;
    proxy_next_upstream on;
    proxy_next_upstream_tries 2;
}

server {
    listen 10001;
    proxy_pass veil_fi;
    proxy_connect_timeout 5s;
    proxy_timeout 1h;
    proxy_next_upstream on;
    proxy_next_upstream_tries 2;
}
CONF

nginx -t
systemctl reload nginx
echo "--- релеи перезаписаны:"
grep -E "listen|proxy_pass|next_upstream" /etc/nginx/stream.d/relay.conf
