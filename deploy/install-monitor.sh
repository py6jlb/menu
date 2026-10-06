#!/usr/bin/env bash
set -euo pipefail

# Установка и обновление периодической проверки доступности и ресурсов.
#
#   sudo /opt/menu/deploy/install-monitor.sh
#   sudo MONITOR_TEST=1 /opt/menu/deploy/install-monitor.sh   # проверить доставку
#
# Ставит systemd units из /opt/menu/deploy/systemd, включает timer и проверяет
# доступность внешних инструментов. Идемпотентно: повторный запуск обновляет
# units и повторно включает timer. MONITOR_TEST=1 отправляет проверочное
# уведомление и требует успешной доставки (без реальной аварии).
#
# Переменные (env; ALERT_WEBHOOK_URL также из /opt/menu/server.conf):
#   ALERT_WEBHOOK_URL  канал оповещения (обязателен)
#   DEPLOY_USER        пользователь службы (menu)
#   RCLONE_BIN         исполняемый файл rclone (rclone)
#   CURL_BIN           исполняемый файл curl (curl)
#   SYSTEMD_UNIT_DIR   каталог units (/etc/systemd/system; override — тестовый шов)
#
# DEPLOY_USER, RCLONE_BIN, CURL_BIN и SYSTEMD_UNIT_DIR — окружение процесса, не
# ключи server.conf.

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"

# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/alert-lib.sh"
config_load "$APP_DIR/server.conf" server || exit 1

DEPLOY_USER="${DEPLOY_USER:-menu}"
RCLONE_BIN="${RCLONE_BIN:-rclone}"
CURL_BIN="${CURL_BIN:-curl}"
UNIT_DIR="${SYSTEMD_UNIT_DIR:-/etc/systemd/system}"
SERVICE="menu-monitor.service"
TIMER="menu-monitor.timer"

log() { printf '\033[1;32m[install-monitor]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[install-monitor]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[install-monitor]\033[0m %s\n' "$*" >&2; exit 1; }

if [ "$UNIT_DIR" = "/etc/systemd/system" ]; then
  [ "$(id -u)" -eq 0 ] || die "Запусти установку через sudo"
fi

if [[ ! "$DEPLOY_USER" =~ ^[A-Za-z_][A-Za-z0-9_-]*$ ]]; then
  die "Некорректный DEPLOY_USER"
fi

config_require ALERT_WEBHOOK_URL
alert_url_valid "$ALERT_WEBHOOK_URL" || die "Некорректный ALERT_WEBHOOK_URL (ожидается http(s) URL)"
if [ -n "${ALERT_HEARTBEAT_URL-}" ]; then
  alert_url_valid "$ALERT_HEARTBEAT_URL" || die "Некорректный ALERT_HEARTBEAT_URL"
fi

command -v "$CURL_BIN" >/dev/null 2>&1 || die "curl не установлен: apt-get install -y curl"
command -v "$RCLONE_BIN" >/dev/null 2>&1 || die "rclone не установлен: apt-get install -y rclone"
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

# Служба должна читать библиотеки и запускать alert.sh от своего имени.
for file in "$APP_DIR/deploy/config.sh" "$APP_DIR/deploy/backup-lib.sh" \
            "$APP_DIR/deploy/alert-lib.sh" "$APP_DIR/deploy/release.sh" \
            "$APP_DIR/deploy/alert.sh" "$APP_DIR/server.conf"; do
  [ -e "$file" ] || continue
  run_as_user test -r "$file" || die "Пользователь $DEPLOY_USER не читает $file"
done
run_as_user test -x "$APP_DIR/deploy/alert.sh" \
  || die "Пользователь $DEPLOY_USER не запускает $APP_DIR/deploy/alert.sh"

# remote бэкапа нужен для проверки возраста копии.
config_require BACKUP_REMOTE
remote="${BACKUP_REMOTE%%:*}"
[ -n "$remote" ] || die "BACKUP_REMOTE должен быть вида remote:bucket"
remotes="$(run_as_user "$RCLONE_BIN" listremotes 2>/dev/null || true)"
if ! printf '%s\n' "$remotes" | grep -qx "$remote:"; then
  warn "rclone remote '$remote' не настроен для $DEPLOY_USER — проверка возраста копии будет падать"
fi

for file in "$SERVICE" "$TIMER"; do
  [ -r "$APP_DIR/deploy/systemd/$file" ] || die "Не найден шаблон units: $APP_DIR/deploy/systemd/$file"
done

# Units рендерятся под фактического пользователя и каталог приложения.
render_unit() {
  sed -e "s|^User=.*|User=$DEPLOY_USER|" \
      -e "s|^WorkingDirectory=.*|WorkingDirectory=$APP_DIR|" \
      -e "s|^ExecStart=.*|ExecStart=$APP_DIR/deploy/alert.sh|" \
      "$1" > "$2"
  chmod 644 "$2"
}

install -d -m 755 "$UNIT_DIR"
render_unit "$APP_DIR/deploy/systemd/$SERVICE" "$UNIT_DIR/$SERVICE"
install -m 644 "$APP_DIR/deploy/systemd/$TIMER" "$UNIT_DIR/$TIMER"

systemctl daemon-reload
systemctl enable --now "$TIMER"

if [ "${MONITOR_TEST:-0}" = "1" ]; then
  run_as_user "$APP_DIR/deploy/alert.sh" --test \
    || die "Проверочное уведомление не доставлено — проверь ALERT_WEBHOOK_URL"
fi

log "Готово. Проверь:"
log "  sudo systemctl start $SERVICE"
log "  sudo journalctl -u $SERVICE -n 50 --no-pager"
log "  sudo MONITOR_TEST=1 $APP_DIR/deploy/install-monitor.sh"
log "  systemctl list-timers $TIMER --no-pager"
