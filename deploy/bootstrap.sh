#!/usr/bin/env bash
set -euo pipefail

# Первичная настройка чистого VPS/VDS с Ubuntu 24.04 LTS.
#
#   sudo SSH_PUBLIC_KEY='ssh-ed25519 AAAA... user@host' ./deploy/bootstrap.sh
#   sudo ./deploy/bootstrap.sh --close-legacy-port
#
# Переменные окружения (все необязательные, кроме SSH_PUBLIC_KEY для первого прогона):
#   DEPLOY_USER      пользователь-владелец приложения      (menu)
#   SSH_PORT         новый порт SSH                        (8822)
#   SSH_LEGACY_PORT  старый порт, закрывается вручную      (22)
#   SSH_PUBLIC_KEY   публичный ключ доступа (строка)
#   SSH_PUBLIC_KEY_FILE  файл с публичным ключом (альтернатива SSH_PUBLIC_KEY)
#   APP_DIR          каталог приложения на сервере         (/opt/menu)
#   SWAP_SIZE        размер swap-файла                     (2G)
#   TIMEZONE         часовой пояс                          (Europe/Moscow)

DEPLOY_USER="${DEPLOY_USER:-menu}"
SSH_PORT="${SSH_PORT:-8822}"
SSH_LEGACY_PORT="${SSH_LEGACY_PORT:-22}"
SSH_PUBLIC_KEY="${SSH_PUBLIC_KEY:-}"
SSH_PUBLIC_KEY_FILE="${SSH_PUBLIC_KEY_FILE:-}"
APP_DIR="${APP_DIR:-/opt/menu}"
SWAP_SIZE="${SWAP_SIZE:-2G}"
TIMEZONE="${TIMEZONE:-Europe/Moscow}"

SSHD_MAIN_CONF="/etc/ssh/sshd_config.d/99-menu-hardening.conf"
SSHD_LEGACY_CONF="/etc/ssh/sshd_config.d/98-menu-legacy-port.conf"
LEGACY_MARKER="/etc/ssh/sshd_config.d/.menu-legacy-closed"
UNATTENDED_CONF="/etc/apt/apt.conf.d/52-menu-unattended.conf"
SWAPFILE="/swapfile"

log()  { printf '\033[1;32m[bootstrap]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[bootstrap]\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[bootstrap]\033[0m %s\n' "$*" >&2; exit 1; }

usage() {
  cat <<'EOF'
Использование:
  sudo [SSH_PUBLIC_KEY='...'] ./deploy/bootstrap.sh            # первичная настройка
  sudo ./deploy/bootstrap.sh --close-legacy-port               # закрыть старый порт SSH после проверки

Переменные: DEPLOY_USER, SSH_PORT, SSH_LEGACY_PORT, SSH_PUBLIC_KEY,
SSH_PUBLIC_KEY_FILE, APP_DIR, SWAP_SIZE, TIMEZONE.
EOF
}

require_root() {
  [ "$(id -u)" -eq 0 ] || die "Запусти от root: sudo $0 $*"
}

read_public_key() {
  if [ -n "$SSH_PUBLIC_KEY" ]; then
    printf '%s\n' "$SSH_PUBLIC_KEY"
    return
  fi
  if [ -n "$SSH_PUBLIC_KEY_FILE" ] && [ -r "$SSH_PUBLIC_KEY_FILE" ]; then
    cat "$SSH_PUBLIC_KEY_FILE"
    return
  fi
  die "Задай SSH_PUBLIC_KEY='ssh-ed25519 ...' или SSH_PUBLIC_KEY_FILE=/path/to/key.pub"
}

install_packages() {
  log "Установка пакетов"
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -y
  apt-get install -y \
    ca-certificates curl gnupg sudo ufw fail2ban unattended-upgrades \
    rclone openssh-server
}

configure_user() {
  if id -u "$DEPLOY_USER" >/dev/null 2>&1; then
    log "Пользователь $DEPLOY_USER уже существует"
  else
    log "Создание пользователя $DEPLOY_USER"
    adduser --disabled-password --gecos "" "$DEPLOY_USER"
  fi
  usermod -aG sudo "$DEPLOY_USER"

  log "Установка SSH-ключа для $DEPLOY_USER"
  local home
  home="$(getent passwd "$DEPLOY_USER" | cut -d: -f6)"
  install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$home/.ssh"
  read_public_key > "$home/.ssh/authorized_keys"
  chmod 600 "$home/.ssh/authorized_keys"
  chown "$DEPLOY_USER:$DEPLOY_USER" "$home/.ssh/authorized_keys"
}

configure_sshd() {
  log "Настройка sshd (порт $SSH_PORT, только по ключу)"
  install -d -m 755 /etc/ssh/sshd_config.d
  cat > "$SSHD_MAIN_CONF" <<EOF
# Управляется deploy/bootstrap.sh
Port $SSH_PORT
PubkeyAuthentication yes
PasswordAuthentication no
PermitRootLogin prohibit-password
EOF

  if [ -f "$LEGACY_MARKER" ]; then
    rm -f "$SSHD_LEGACY_CONF"
  else
    cat > "$SSHD_LEGACY_CONF" <<EOF
# Временный старый порт. Закрывается: sudo ./deploy/bootstrap.sh --close-legacy-port
Port $SSH_LEGACY_PORT
EOF
  fi
}

configure_firewall() {
  log "Настройка ufw"
  ufw default deny incoming
  ufw default allow outgoing
  ufw allow "$SSH_PORT"/tcp
  ufw allow 80/tcp
  ufw allow 443/tcp
  if [ ! -f "$LEGACY_MARKER" ]; then
    ufw allow "$SSH_LEGACY_PORT"/tcp
  fi
  ufw --force enable
}

configure_unattended() {
  log "Security-обновления и авто-перезагрузка ночью"
  cat > "$UNATTENDED_CONF" <<'EOF'
Unattended-Upgrade::Allowed-Origins {
    "${distro_id}:${distro_codename}-security";
    "${distro_id}ESMApps:${distro_codename}-apps-security";
    "${distro_id}ESM:${distro_codename}-infra-security";
};
Unattended-Upgrade::Automatic-Reboot "true";
Unattended-Upgrade::Automatic-Reboot-Time "04:00";
EOF
  cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
  systemctl enable --now unattended-upgrades
}

configure_time() {
  log "Часовой пояс $TIMEZONE и NTP"
  timedatectl set-timezone "$TIMEZONE"
  systemctl enable --now systemd-timesyncd
}

configure_swap() {
  if swapon --show=NAME --noheadings | grep -qx "$SWAPFILE"; then
    log "Swap уже активен"
    return
  fi
  log "Создание swap $SWAP_SIZE"
  if [ ! -f "$SWAPFILE" ]; then
    fallocate -l "$SWAP_SIZE" "$SWAPFILE" || dd if=/dev/zero of="$SWAPFILE" bs=1M count="$(numfmt --from=iec "$SWAP_SIZE" | awk '{print $1/1048576}')"
    chmod 600 "$SWAPFILE"
    mkswap "$SWAPFILE"
  fi
  swapon "$SWAPFILE"
  grep -qF "$SWAPFILE" /etc/fstab || printf '%s none swap sw 0 0\n' "$SWAPFILE" >> /etc/fstab
}

install_docker() {
  if command -v docker >/dev/null 2>&1; then
    log "Docker уже установлен"
  else
    log "Установка Docker Engine"
    curl -fsSL https://get.docker.com | sh
  fi
  systemctl enable --now docker
  usermod -aG docker "$DEPLOY_USER"
}

prepare_app_dir() {
  log "Каталог приложения $APP_DIR"
  install -d -m 750 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$APP_DIR"
}

reload_ssh() {
  log "Перезапуск sshd"
  systemctl reload ssh 2>/dev/null || systemctl reload sshd 2>/dev/null || systemctl restart ssh
}

bootstrap() {
  require_root
  install_packages
  configure_user
  configure_sshd
  configure_firewall
  configure_unattended
  configure_time
  configure_swap
  reload_ssh
  install_docker
  prepare_app_dir

  cat <<EOF

Готово. Дальше:
  1. Проверь вход по новому порту в отдельном терминале:
       ssh -p $SSH_PORT $DEPLOY_USER@<IP>
  2. Убедившись, что вход работает, закрой старый порт $SSH_LEGACY_PORT:
       sudo ./deploy/bootstrap.sh --close-legacy-port
EOF
}

close_legacy_port() {
  require_root
  log "Закрытие старого порта $SSH_LEGACY_PORT"
  rm -f "$SSHD_LEGACY_CONF"
  reload_ssh
  ufw delete allow "$SSH_LEGACY_PORT"/tcp || true
  touch "$LEGACY_MARKER"
  log "Старый порт закрыт, открыт только $SSH_PORT"
}

case "${1:-bootstrap}" in
  bootstrap|"")    bootstrap ;;
  --close-legacy-port) close_legacy_port ;;
  -h|--help)       usage ;;
  *)               usage; exit 1 ;;
esac
