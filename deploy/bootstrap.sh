#!/usr/bin/env bash
set -euo pipefail

# Повторяемая настройка VPS/VDS с Ubuntu 24.04 LTS.
#
#   sudo SSH_PUBLIC_KEY='ssh-ed25519 AAAA... user@host' ./deploy/bootstrap.sh
#   sudo ./deploy/bootstrap.sh --close-legacy-port
#
# Скрипт можно запускать повторно: существующие authorized_keys сохраняются,
# новый ключ добавляется без дублей. Порт SSH меняется на новый, старый
# остаётся открытым, пока оператор не подтвердит вход и явно не выполнит
# --close-legacy-port. Конфигурация sshd проверяется до применения, а старый
# порт не закрывается, пока новый реально не слушается.
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
#   SUDO_ALLOWED     команды NOPASSWD для пользователя     (ALL)
#
# Пути к системным файлам вынесены в переменные (SSHD_CONFIG_DIR,
# SYSTEMD_UNIT_DIR, ...), чтобы функции проверялись изолированно под
# подменёнными внешними командами; в проде используются defaults.

DEPLOY_USER="${DEPLOY_USER:-menu}"
SSH_PORT="${SSH_PORT:-8822}"
SSH_LEGACY_PORT="${SSH_LEGACY_PORT:-22}"
SSH_PUBLIC_KEY="${SSH_PUBLIC_KEY:-}"
SSH_PUBLIC_KEY_FILE="${SSH_PUBLIC_KEY_FILE:-}"
APP_DIR="${APP_DIR:-/opt/menu}"
SWAP_SIZE="${SWAP_SIZE:-2G}"
TIMEZONE="${TIMEZONE:-Europe/Moscow}"
SUDO_ALLOWED="${SUDO_ALLOWED:-ALL}"

SSHD_CONFIG_DIR="${SSHD_CONFIG_DIR:-/etc/ssh/sshd_config.d}"
SSHD_MAIN_CONF="${SSHD_MAIN_CONF:-$SSHD_CONFIG_DIR/99-menu-hardening.conf}"
SSHD_LEGACY_CONF="${SSHD_LEGACY_CONF:-$SSHD_CONFIG_DIR/98-menu-legacy-port.conf}"
LEGACY_MARKER="${LEGACY_MARKER:-$SSHD_CONFIG_DIR/.menu-legacy-closed}"
SYSTEMD_UNIT_DIR="${SYSTEMD_UNIT_DIR:-/etc/systemd/system}"
SSH_SOCKET_DROPIN="${SSH_SOCKET_DROPIN:-$SYSTEMD_UNIT_DIR/ssh.socket.d/10-menu-ports.conf}"
SUDOERS_DIR="${SUDOERS_DIR:-/etc/sudoers.d}"
SUDOERS_FILE="${SUDOERS_FILE:-$SUDOERS_DIR/90-menu-$DEPLOY_USER}"
FAIL2BAN_JAIL_DIR="${FAIL2BAN_JAIL_DIR:-/etc/fail2ban/jail.d}"
FAIL2BAN_JAIL_FILE="${FAIL2BAN_JAIL_FILE:-$FAIL2BAN_JAIL_DIR/menu-sshd.local}"
UNATTENDED_CONF="${UNATTENDED_CONF:-/etc/apt/apt.conf.d/52-menu-unattended.conf}"
APT_AUTO_CONF="${APT_AUTO_CONF:-/etc/apt/apt.conf.d/20auto-upgrades}"
SWAPFILE="${SWAPFILE:-/swapfile}"
FSTAB="${FSTAB:-/etc/fstab}"
OS_RELEASE_FILE="${OS_RELEASE_FILE:-/etc/os-release}"
DOCKER_GPG_URL="${DOCKER_GPG_URL:-https://download.docker.com/linux/ubuntu/gpg}"
DOCKER_REPO_URL="${DOCKER_REPO_URL:-https://download.docker.com/linux/ubuntu}"
DOCKER_KEYRING="${DOCKER_KEYRING:-/etc/apt/keyrings/docker.asc}"
DOCKER_APT_LIST="${DOCKER_APT_LIST:-/etc/apt/sources.list.d/docker.list}"
SSH_LISTEN_ATTEMPTS="${SSH_LISTEN_ATTEMPTS:-10}"

log()  { printf '\033[1;32m[bootstrap]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[bootstrap]\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[bootstrap]\033[0m %s\n' "$*" >&2; exit 1; }

usage() {
  cat <<'EOF'
Использование:
  sudo [SSH_PUBLIC_KEY='...'] ./deploy/bootstrap.sh            # первичная/повторная настройка
  sudo ./deploy/bootstrap.sh --close-legacy-port               # закрыть старый порт SSH после проверки входа

Переменные: DEPLOY_USER, SSH_PORT, SSH_LEGACY_PORT, SSH_PUBLIC_KEY,
SSH_PUBLIC_KEY_FILE, APP_DIR, SWAP_SIZE, TIMEZONE, SUDO_ALLOWED.
EOF
}

require_root() {
  [ "$(id -u)" -eq 0 ] || die "Запусти от root: sudo $0"
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

normalize_key() {
  printf '%s' "$1" | tr -d '\r' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'
}

# Ключ уникален по типу и base64-телу; комментарий (user@host) не учитывается,
# поэтому повторная выдача того же ключа с другим комментарием не дублируется.
key_fingerprint() {
  awk '{ if (NF >= 2) printf "%s %s", $1, $2 }' <<<"$1"
}

authorized_keys_has() {
  local file="$1" fp
  fp="$(key_fingerprint "$2")"
  awk -v fp="$fp" '
    /^[[:space:]]*#/ { next }
    NF >= 2 && ($1 " " $2) == fp { found = 1 }
    END { exit(found ? 0 : 1) }
  ' "$file"
}

# Идемпотентное слияние: существующие строки сохраняются, новый ключ
# добавляется один раз. Файл создаётся при отсутствии.
merge_authorized_key() {
  local file="$1" key fp
  [ -n "$file" ] || { warn "merge_authorized_key: не задан путь"; return 1; }
  key="$(normalize_key "$2")"
  [ -n "$key" ] || { warn "Пустой SSH-ключ"; return 1; }
  fp="$(key_fingerprint "$key")"
  if [ -z "$fp" ]; then
    warn "Неверный формат SSH-ключа (ожидается 'тип base64 [комментарий]')"
    return 1
  fi
  install -d -m 700 "$(dirname "$file")"
  if [ -f "$file" ] && authorized_keys_has "$file" "$key"; then
    log "SSH-ключ уже разрешён ($file), дубль не добавлен"
    return 0
  fi
  if [ -s "$file" ] && [ "$(tail -c 1 "$file" | wc -l)" -eq 0 ]; then
    printf '\n' >>"$file"
  fi
  printf '%s\n' "$key" >>"$file"
  chmod 600 "$file"
  log "SSH-ключ добавлен в $file"
}

configure_user() {
  local home key
  if id -u "$DEPLOY_USER" >/dev/null 2>&1; then
    log "Пользователь $DEPLOY_USER уже существует"
  else
    log "Создание пользователя $DEPLOY_USER"
    adduser --disabled-password --gecos "" "$DEPLOY_USER"
  fi
  usermod -aG sudo "$DEPLOY_USER"

  home="$(getent passwd "$DEPLOY_USER" | cut -d: -f6)"
  [ -n "$home" ] || die "Не найден домашний каталог пользователя $DEPLOY_USER"
  install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$home/.ssh"
  local keys
  keys="$(read_public_key)"
  while IFS= read -r key || [ -n "$key" ]; do
    [ -n "$(normalize_key "$key")" ] || continue
    merge_authorized_key "$home/.ssh/authorized_keys" "$key"
  done <<<"$keys"
  chown "$DEPLOY_USER:$DEPLOY_USER" "$home/.ssh/authorized_keys"
  chmod 600 "$home/.ssh/authorized_keys"
  log "SSH-ключ(и) настроены для $DEPLOY_USER"
}

# Аккаунт создаётся без пароля, поэтому интерактивный sudo для него невозможен.
# Настраиваем NOPASSWD-путь и проверяем его фактически (sudo -n), а не только
# членство в группе sudo. Аккаунт уже в группе docker (root-эквивалент),
# поэтому NOPASSWD не расширяет реальные полномочия, но делает
# административные действия воспроизводимыми из неинтерактивного SSH.
configure_admin_access() {
  log "Административный доступ $DEPLOY_USER (NOPASSWD)"
  install -d -m 755 "$SUDOERS_DIR"
  local tmp
  tmp="$(mktemp "$SUDOERS_DIR/.menu-sudoers.XXXXXX")"
  cat >"$tmp" <<EOF
# Управляется deploy/bootstrap.sh.
# Пользователь создан без пароля; NOPASSWD даёт рабочий административный путь.
$DEPLOY_USER ALL=(root) NOPASSWD: $SUDO_ALLOWED
EOF
  chmod 440 "$tmp"
  if ! visudo -cf "$tmp"; then
    rm -f "$tmp"
    die "Некорректный sudoers-файл для $DEPLOY_USER"
  fi
  mv -f "$tmp" "$SUDOERS_FILE"
  verify_admin_path
}

verify_admin_path() {
  log "Проверка административного пути: sudo -n для $DEPLOY_USER"
  runuser -u "$DEPLOY_USER" -- sudo -n true \
    || die "Пользователь $DEPLOY_USER не может выполнять sudo без пароля: административный путь не работает"
  log "Административный путь $DEPLOY_USER подтверждён (sudo -n)"
}

# Пишем конфигурацию, но не применяем её. Применение — отдельный шаг после
# validate_sshd_config и открытия firewall, чтобы рабочий путь не пропал.
configure_sshd_files() {
  log "Файлы sshd (новый порт $SSH_PORT, только по ключу)"
  install -d -m 755 "$SSHD_CONFIG_DIR"
  cat >"$SSHD_MAIN_CONF" <<EOF
# Управляется deploy/bootstrap.sh
Port $SSH_PORT
PubkeyAuthentication yes
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
UsePAM yes
EOF

  if [ -f "$LEGACY_MARKER" ]; then
    rm -f "$SSHD_LEGACY_CONF"
  else
    cat >"$SSHD_LEGACY_CONF" <<EOF
# Временный старый порт. Закрывается: sudo $0 --close-legacy-port
Port $SSH_LEGACY_PORT
EOF
  fi
}

# Проверка синтаксиса (sshd -t) и эффективной конфигурации (sshd -T) до
# применения. Проверяем и новый порт, и (пока старый открыт) старый порт, а
# также что пароль и root-по-паролю действительно запрещены в эффективной
# конфигурации, а не только в нашем файле.
validate_sshd_config() {
  log "Проверка sshd: синтаксис и эффективная конфигурация"
  sshd -t || die "sshd -t: некорректная конфигурация SSH; изменения НЕ применяются"
  local effective
  effective="$(sshd -T)" || die "sshd -T: не удалось получить эффективную конфигурацию"

  grep -qx "port $SSH_PORT" <<<"$effective" \
    || die "Эффективная конфигурация не содержит порт $SSH_PORT"
  if [ ! -f "$LEGACY_MARKER" ]; then
    grep -qx "port $SSH_LEGACY_PORT" <<<"$effective" \
      || die "Эффективная конфигурация не содержит страховочный порт $SSH_LEGACY_PORT"
  fi
  grep -qx "pubkeyauthentication yes" <<<"$effective" \
    || die "Вход по ключу не включён в эффективной конфигурации"
  grep -qx "passwordauthentication no" <<<"$effective" \
    || die "Вход по паролю не отключён в эффективной конфигурации"
  case "$effective" in
    *"permitrootlogin yes"*) die "Вход root по паролю разрешён в эффективной конфигурации" ;;
  esac
  log "Эффективная конфигурация sshd корректна"
}

ssh_socket_activated() {
  systemctl is-enabled ssh.socket >/dev/null 2>&1 && return 0
  systemctl is-active ssh.socket >/dev/null 2>&1 && return 0
  return 1
}

# Ubuntu 24.04 по умолчанию поднимает SSH через systemd socket activation
# (ssh.socket). В этом режиме директива Port из sshd_config не влияет на
# listener — порт задаётся ListenStream. Учитываем оба режима.
apply_sshd() {
  install -d -m 755 "$SYSTEMD_UNIT_DIR"
  if ssh_socket_activated; then
    log "SSH через socket activation: настраиваем ssh.socket ListenStream"
    install -d -m 755 "$(dirname "$SSH_SOCKET_DROPIN")"
    {
      printf '[Socket]\n'
      printf 'ListenStream=\n'
      printf 'ListenStream=%s\n' "$SSH_PORT"
      # Страховочный порт слушаем, пока существует его конфиг: закрытие
      # удаляет конфиг и перезапускает socket, поэтому порт не возвращается.
      if [ -f "$SSHD_LEGACY_CONF" ]; then
        printf 'ListenStream=%s\n' "$SSH_LEGACY_PORT"
      fi
    } >"$SSH_SOCKET_DROPIN"
    systemctl daemon-reload
    systemctl restart ssh.socket
  else
    log "SSH через ssh.service: перезапуск сервиса"
    systemctl daemon-reload
    systemctl restart ssh
  fi
}

# Реальная проверка слушающего сокета на выбранном порту: конфигурация может
# быть валидной, но listener не подняться. Пока это не подтверждено, старый
# порт закрывать нельзя.
verify_ssh_listener() {
  local port="$1" attempt=0
  while [ "$attempt" -lt "$SSH_LISTEN_ATTEMPTS" ]; do
    if ss -H -ltn | awk '{print $4}' | grep -Eq "[:.]${port}$"; then
      log "Порт $port реально слушается"
      return 0
    fi
    attempt=$((attempt + 1))
    sleep 1
  done
  die "SSH не слушает порт $port. Старый порт НЕ закрывай. Аварийный доступ — через консоль провайдера (см. deploy/README.md)."
}

configure_firewall() {
  log "Настройка ufw (SSH $SSH_PORT/$SSH_LEGACY_PORT, 80, 443)"
  ufw default deny incoming
  ufw default allow outgoing
  ufw allow "${SSH_PORT}/tcp"
  if [ ! -f "$LEGACY_MARKER" ]; then
    ufw allow "${SSH_LEGACY_PORT}/tcp"
  fi
  ufw allow 80/tcp
  ufw allow 443/tcp
  ufw --force enable
}

# Проверяем не факт установки fail2ban, а реальное действие защиты на выбранном
# порту: jail активен, а бан и разбан пробного адреса из TEST-NET-1 (RFC 5737)
# фактически проходят через механизм действий.
verify_fail2ban_action() {
  log "Проверка реального действия fail2ban на порту $SSH_PORT"
  fail2ban-client -t >/dev/null 2>&1 || die "fail2ban: конфигурация некорректна"
  fail2ban-client status sshd | grep -qi 'jail' \
    || die "fail2ban: jail sshd не активен"
  local probe="192.0.2.1"
  fail2ban-client set sshd banip "$probe" >/dev/null \
    || die "fail2ban: не удалось применить бан на порту $SSH_PORT"
  if ! fail2ban-client status sshd | grep -q "$probe"; then
    fail2ban-client set sshd unbanip "$probe" >/dev/null 2>&1 || true
    die "fail2ban: бан не появился в списке — защита не действует"
  fi
  fail2ban-client set sshd unbanip "$probe" >/dev/null \
    || die "fail2ban: не удалось снять пробный бан"
  log "fail2ban реально блокирует на порту $SSH_PORT"
}

configure_fail2ban() {
  log "fail2ban: jail sshd на порту $SSH_PORT"
  install -d -m 755 "$FAIL2BAN_JAIL_DIR"
  cat >"$FAIL2BAN_JAIL_FILE" <<EOF
[sshd]
enabled = true
port = $SSH_PORT
backend = systemd
maxretry = 5
findtime = 10m
bantime = 1h
EOF
  systemctl enable --now fail2ban
  systemctl restart fail2ban
  verify_fail2ban_action
}

install_packages() {
  log "Установка пакетов"
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -y
  apt-get install -y \
    ca-certificates curl gnupg sudo ufw fail2ban unattended-upgrades \
    rclone openssh-server
}

configure_unattended() {
  log "Security-обновления и авто-перезагрузка ночью"
  cat >"$UNATTENDED_CONF" <<'EOF'
Unattended-Upgrade::Allowed-Origins {
    "${distro_id}:${distro_codename}-security";
    "${distro_id}ESMApps:${distro_codename}-apps-security";
    "${distro_id}ESM:${distro_codename}-infra-security";
};
Unattended-Upgrade::Automatic-Reboot "true";
# 04:30: не пересекается с окном backup 02:30–02:40 (menu-backup.timer).
Unattended-Upgrade::Automatic-Reboot-Time "04:30";
EOF
  cat >"$APT_AUTO_CONF" <<'EOF'
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
    fallocate -l "$SWAP_SIZE" "$SWAPFILE" \
      || dd if=/dev/zero of="$SWAPFILE" bs=1M count="$(numfmt --from=iec "$SWAP_SIZE" | awk '{print $1/1048576}')"
    chmod 600 "$SWAPFILE"
    mkswap "$SWAPFILE"
  fi
  swapon "$SWAPFILE"
  grep -qF "$SWAPFILE" "$FSTAB" || printf '%s none swap sw 0 0\n' "$SWAPFILE" >>"$FSTAB"
}

# Docker ставится из официального apt-репозитория с проверяемым GPG-ключом
# (signed-by), а не из curl|sh. Compose v2 (плагин) проверяется явно.
install_docker_repo() {
  log "Официальный Docker apt-репозиторий (download.docker.com)"
  install -d -m 0755 "$(dirname "$DOCKER_KEYRING")"
  curl -fsSL "$DOCKER_GPG_URL" -o "$DOCKER_KEYRING"
  chmod a+r "$DOCKER_KEYRING"
  local codename arch
  # shellcheck source=/dev/null
  codename="$(. "$OS_RELEASE_FILE" && printf '%s' "${VERSION_CODENAME:-}")"
  [ -n "$codename" ] || die "Не удалось определить кодовое имя Ubuntu из $OS_RELEASE_FILE"
  arch="$(dpkg --print-architecture)"
  printf 'deb [arch=%s signed-by=%s] %s %s stable\n' \
    "$arch" "$DOCKER_KEYRING" "$DOCKER_REPO_URL" "$codename" >"$DOCKER_APT_LIST"
  apt-get update -y
  apt-get install -y docker-ce docker-ce-cli containerd.io \
    docker-buildx-plugin docker-compose-plugin
}

verify_docker() {
  log "Явная проверка Docker, Compose v2 и нужных инструментов"
  command -v docker >/dev/null 2>&1 || die "docker не установлен"
  command -v rclone >/dev/null 2>&1 || die "rclone не установлен"
  command -v ufw >/dev/null 2>&1 || die "ufw не установлен"
  command -v fail2ban-client >/dev/null 2>&1 || die "fail2ban-client не установлен"
  command -v ss >/dev/null 2>&1 || die "iproute2 (ss) не установлен"
  local compose_version
  compose_version="$(docker compose version 2>/dev/null || true)"
  printf '%s' "$compose_version" | grep -Eq 'Docker Compose version v?2\.[0-9]' \
    || die "Нужен Docker Compose v2 (плагин), получено: '${compose_version}'"
  docker --version
  log "$compose_version"
}

install_docker() {
  if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    log "Docker и Compose v2 уже установлены"
  else
    install_docker_repo
  fi
  systemctl enable --now docker
  usermod -aG docker "$DEPLOY_USER"
  verify_docker
}

prepare_app_dir() {
  log "Каталог приложения $APP_DIR"
  install -d -m 750 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "$APP_DIR"
}

bootstrap() {
  require_root
  install_packages
  configure_user
  configure_admin_access
  configure_sshd_files
  validate_sshd_config
  configure_firewall
  apply_sshd
  verify_ssh_listener "$SSH_PORT"
  configure_fail2ban
  configure_unattended
  configure_time
  configure_swap
  install_docker
  prepare_app_dir

  cat <<EOF

Готово. Дальше:
  1. Проверь вход по новому порту в ОТДЕЛЬНОМ сеансе:
       ssh -p $SSH_PORT $DEPLOY_USER@<IP>
     и административный путь: sudo -n true
  2. Только убедившись, что вход работает, закрой старый порт $SSH_LEGACY_PORT:
       sudo ./deploy/bootstrap.sh --close-legacy-port

Если что-то пошло не так, старый порт ещё открыт. Аварийный доступ —
через консоль провайдера (deploy/README.md, раздел «Аварийный доступ»).
EOF
}

# Отдельный явный шаг. Не закрывает старый порт, пока новый не слушается и
# конфигурация sshd не прошла проверку. При любой ошибке старый путь остаётся.
close_legacy_port() {
  log "Закрытие старого порта $SSH_LEGACY_PORT"
  verify_ssh_listener "$SSH_PORT"
  validate_sshd_config
  ufw delete allow "${SSH_LEGACY_PORT}/tcp" || true
  rm -f "$SSHD_LEGACY_CONF"
  apply_sshd
  verify_ssh_listener "$SSH_PORT"
  touch "$LEGACY_MARKER"
  log "Старый порт закрыт, открыт только $SSH_PORT"
}

main() {
  case "${1:-bootstrap}" in
    bootstrap|"")        bootstrap ;;
    --close-legacy-port) require_root; close_legacy_port ;;
    -h|--help)           usage ;;
    *)                   usage; exit 1 ;;
  esac
}

if [ "${BASH_SOURCE[0]:-}" = "${0:-}" ]; then
  main "$@"
fi
