#!/usr/bin/env bash
# Installs veil-server on a Debian/Ubuntu VPS as a systemd service.
# Run as root: sudo bash install.sh /tmp/veil-server-linux-amd64
set -euo pipefail

BIN="${1:-/tmp/veil-server-linux-amd64}"
[ -f "$BIN" ] || { echo "binary not found: $BIN"; exit 1; }

install -d -m 755 /opt/veil
id -u veil >/dev/null 2>&1 || useradd --system --home /opt/veil --shell /usr/sbin/nologin veil
install -m 755 "$BIN" /opt/veil/veil-server
install -d -o veil -g veil /opt/veil/certs

# Ключи клиентов: владелец root, группа veil, 640. Сервис читает, менять может только root.
# Получатель ключа не получает ни пользователя, ни оболочки, ни sudo — только PSK для туннеля.
[ -f /opt/veil/keys.txt ] || : > /opt/veil/keys.txt
chown root:veil /opt/veil/keys.txt
chmod 640 /opt/veil/keys.txt
usermod -s /usr/sbin/nologin veil 2>/dev/null || true
getent group sudo | grep -qw veil && gpasswd -d veil sudo || true

if [ ! -s /opt/veil/veil.env ]; then
  PSK=$(head -c 48 /dev/urandom | base64 | tr -d '/+=\n' | head -c 40)
  printf 'VEIL_PSK=%s\n' "$PSK" > /opt/veil/veil.env
  chmod 600 /opt/veil/veil.env
  echo "PSK generated: $PSK  (give it to clients)"
else
  echo "PSK: $(grep -o 'VEIL_PSK=.*' /opt/veil/veil.env | cut -d= -f2-)"
fi

cat >/etc/systemd/system/veil.service <<'UNIT'
[Unit]
Description=veil tunnel (TLS camouflage)
After=network-online.target
Wants=network-online.target

[Service]
User=veil
Group=veil
EnvironmentFile=/opt/veil/veil.env
ExecStart=/opt/veil/veil-server -addr :${VEIL_PORT:-8443} \
  -cert /opt/veil/certs/fullchain.pem \
  -key /opt/veil/certs/privkey.pem \
  -psk ${VEIL_PSK} \
  -psks /opt/veil/keys.txt \
  -fallback 127.0.0.1:8080
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

[Install]
WantedBy=multi-user.target
UNIT

if [ ! -s /opt/veil/certs/fullchain.pem ] || [ ! -s /opt/veil/certs/privkey.pem ]; then
  cat >&2 <<'MSG'
!! Certificate missing. Put a REAL certificate here before starting:
     /opt/veil/certs/fullchain.pem
     /opt/veil/certs/privkey.pem
   e.g. certbot certonly --standalone -d veil.example.com
        cp /etc/letsencrypt/live/veil.example.com/fullchain.pem /opt/veil/certs/
        cp /etc/letsencrypt/live/veil.example.com/privkey.pem /opt/veil/certs/
        chown -R veil:veil /opt/veil/certs
   Then: systemctl restart veil
MSG
fi

systemctl daemon-reload
systemctl enable veil >/dev/null 2>&1 || true
systemctl restart veil || true
sleep 1
systemctl --no-pager --lines=8 status veil || true
