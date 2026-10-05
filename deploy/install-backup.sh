#!/usr/bin/env bash
set -euo pipefail

# Установка и обновление резервного копирования на сервере.
#
#   sudo /opt/menu/deploy/install-backup.sh
#
# Ставит systemd units из /opt/menu/deploy/systemd, включает timer и проверяет,
# что rclone настроен для пользователя службы. Идемпотентно: повторный запуск
# обновляет units и повторно включает timer.
#
# Переменные (env; BACKUP_REMOTE также из /opt/menu/server.conf):
#   BACKUP_REMOTE      rclone-remote (обязателен)
#   DEPLOY_USER        пользователь службы (menu)
#   RCLONE_BIN         исполняемый файл rclone (rclone)
#   SYSTEMD_UNIT_DIR   каталог units (/etc/systemd/system; override — тестовый шов)
#
# DEPLOY_USER, RCLONE_BIN и SYSTEMD_UNIT_DIR — окружение процесса, не ключи
# server.conf: whitelist серверных ключей их не принимает.

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"

# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
config_load "$APP_DIR/server.conf" server || exit 1

DEPLOY_USER="${DEPLOY_USER:-menu}"
RCLONE_BIN="${RCLONE_BIN:-rclone}"
UNIT_DIR="${SYSTEMD_UNIT_DIR:-/etc/systemd/system}"
SERVICE="menu-backup.service"
TIMER="menu-backup.timer"

log() { printf '\033[1;32m[install-backup]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[install-backup]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[install-backup]\033[0m %s\n' "$*" >&2; exit 1; }

if [ "$UNIT_DIR" = "/etc/systemd/system" ]; then
  [ "$(id -u)" -eq 0 ] || die "Запусти установку через sudo"
fi

if [[ ! "$DEPLOY_USER" =~ ^[A-Za-z_][A-Za-z0-9_-]*$ ]]; then
  die "Некорректный DEPLOY_USER"
fi

config_require BACKUP_REMOTE

command -v "$RCLONE_BIN" >/dev/null 2>&1 || die "rclone не установлен: apt-get install -y rclone"
command -v docker >/dev/null 2>&1 || die "docker не установлен"
id -u "$DEPLOY_USER" >/dev/null 2>&1 || die "Пользователь $DEPLOY_USER не существует"

# server.conf содержит секреты; служба должна его читать.
if [ -f "$APP_DIR/server.conf" ]; then
  chown "$DEPLOY_USER:$DEPLOY_USER" "$APP_DIR/server.conf" \
    || die "Не удалось передать $APP_DIR/server.conf пользователю $DEPLOY_USER"
  chmod 600 "$APP_DIR/server.conf"
fi

run_as_user() {
  if [ "$(id -un)" = "$DEPLOY_USER" ]; then
    "$@"
  elif [ "$(id -u)" -eq 0 ]; then
    runuser -u "$DEPLOY_USER" -H -- "$@"
  else
    sudo -u "$DEPLOY_USER" -H -- "$@"
  fi
}

# Служба должна читать библиотеку и запускать backup.sh от своего имени.
for file in "$APP_DIR/deploy/config.sh" "$APP_DIR/deploy/backup-lib.sh" \
            "$APP_DIR/deploy/backup.sh" "$APP_DIR/server.conf"; do
  [ -e "$file" ] || continue
  run_as_user test -r "$file" || die "Пользователь $DEPLOY_USER не читает $file"
done
run_as_user test -x "$APP_DIR/deploy/backup.sh" \
  || die "Пользователь $DEPLOY_USER не запускает $APP_DIR/deploy/backup.sh"

if ! id -nG "$DEPLOY_USER" | tr ' ' '\n' | grep -qx docker; then
  warn "Пользователь $DEPLOY_USER не в группе docker — служба не сможет управлять контейнерами"
fi

# Имя remote — до первого ':'; BACKUP_REMOTE=remote:bucket/prefix.
remote="${BACKUP_REMOTE%%:*}"
[ -n "$remote" ] || die "BACKUP_REMOTE должен быть вида remote:bucket"

# Доступ к remote проверяется от имени службы: root-настройки rclone службе не видны.
remotes="$(run_as_user "$RCLONE_BIN" listremotes 2>/dev/null || true)"
if ! printf '%s\n' "$remotes" | grep -qx "$remote:"; then
  die "rclone remote '$remote' не настроен для $DEPLOY_USER: sudo -u $DEPLOY_USER -H rclone config"
fi

for file in "$SERVICE" "$TIMER"; do
  [ -r "$APP_DIR/deploy/systemd/$file" ] || die "Не найден шаблон units: $APP_DIR/deploy/systemd/$file"
done

# Units рендерятся под фактического пользователя и каталог приложения: доставленный
# шаблон не требует checkout на сервере и не расходится с конфигурацией.
render_unit() {
  sed -e "s|^User=.*|User=$DEPLOY_USER|" \
      -e "s|^WorkingDirectory=.*|WorkingDirectory=$APP_DIR|" \
      -e "s|^ExecStart=.*|ExecStart=$APP_DIR/deploy/backup.sh|" \
      "$1" > "$2"
  chmod 644 "$2"
}

install -d -m 755 "$UNIT_DIR"
render_unit "$APP_DIR/deploy/systemd/$SERVICE" "$UNIT_DIR/$SERVICE"
install -m 644 "$APP_DIR/deploy/systemd/$TIMER" "$UNIT_DIR/$TIMER"

systemctl daemon-reload
systemctl enable --now "$TIMER"

log "Готово. Проверь:"
log "  sudo systemctl start $SERVICE"
log "  sudo journalctl -u $SERVICE -n 50 --no-pager"
log "  $APP_DIR/deploy/restore-drill.sh"
log "  systemctl list-timers $TIMER --no-pager"
