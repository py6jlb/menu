#!/usr/bin/env bash
set -euo pipefail

# Деплой на VPS: доставка compose/Caddyfile и рестарт на прибитом теге.
#
#   ./deploy/deploy.sh <git-sha>
#   ./deploy/deploy.sh <предыдущий-sha>   # откат
#
# Переменные (env или deploy/local.conf):
#   VPS_HOST       адрес сервера (обязателен)
#   VPS_USER       пользователь SSH (menu)
#   VPS_SSH_PORT   порт SSH (8822)
#   APP_DIR        каталог на сервере (/opt/menu)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
. "$ROOT/deploy/config.sh"
config_load "$ROOT/deploy/local.conf" local

TAG="${1:-}"
if [ -z "$TAG" ]; then
  echo "Использование: $0 <git-sha>" >&2
  exit 1
fi
config_image_tag "$TAG"

config_require VPS_HOST
VPS_USER="${VPS_USER-menu}"
VPS_SSH_PORT="${VPS_SSH_PORT-8822}"
APP_DIR="${APP_DIR-/opt/menu}"

# SSH передаёт удалённую команду через shell. Эти операционные адреса/пути
# ограничены безопасным алфавитом; секреты и произвольный текст сюда не входят.
if [[ ! "$VPS_HOST" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ||
      ! "$VPS_USER" =~ ^[A-Za-z_][A-Za-z0-9_-]*$ ||
      ! "$VPS_SSH_PORT" =~ ^[0-9]+$ ||
      ! "$APP_DIR" =~ ^/[A-Za-z0-9/._-]+$ ]]; then
  printf 'Конфигурация: некорректные VPS_HOST/VPS_USER/VPS_SSH_PORT/APP_DIR\n' >&2
  exit 1
fi

TARGET="$VPS_USER@$VPS_HOST"

log() { printf '\033[1;32m[deploy]\033[0m %s\n' "$*"; }

log "Доставка конфигов на $TARGET:$APP_DIR"
ssh -p "$VPS_SSH_PORT" "$TARGET" "mkdir -p '$APP_DIR/deploy/systemd'"
scp -P "$VPS_SSH_PORT" docker-compose.prod.yml "$TARGET:$APP_DIR/docker-compose.prod.yml"
scp -P "$VPS_SSH_PORT" deploy/Caddyfile "$TARGET:$APP_DIR/Caddyfile"
scp -P "$VPS_SSH_PORT" deploy/otel-collector.yaml "$TARGET:$APP_DIR/otel-collector.yaml"
scp -P "$VPS_SSH_PORT" deploy/config.sh deploy/compose.sh deploy/remote-deploy.sh \
  deploy/backup.sh deploy/restore-drill.sh "$TARGET:$APP_DIR/deploy/"
scp -P "$VPS_SSH_PORT" deploy/systemd/menu-backup.service deploy/systemd/menu-backup.timer \
  "$TARGET:$APP_DIR/deploy/systemd/"

log "Рестарт на теге $TAG"
ssh -p "$VPS_SSH_PORT" "$TARGET" "bash '$APP_DIR/deploy/remote-deploy.sh' '$TAG'"

log "Готово. Откат: ./deploy/deploy.sh <предыдущий-sha>"
