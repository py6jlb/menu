#!/usr/bin/env bash
set -euo pipefail

# Деплой на VPS: доставка конфигураций выпускаемого коммита и рестарт на прибитом теге.
#
#   ./deploy/deploy.sh <git-sha>
#   ./deploy/deploy.sh <предыдущий-sha>   # откат
#
# Тег — реальный commit: конфигурации берутся из выпускаемого среза (чистый
# checkout на этом коммите), а не из произвольного рабочего дерева.
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
# shellcheck disable=SC1091
. "$ROOT/deploy/release.sh"
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

COMMIT="$(release_resolve_commit "$ROOT" "$TAG")"
release_require_clean_tree "$ROOT"
HEAD_COMMIT="$(release_resolve_commit "$ROOT" HEAD)"
if [ "$HEAD_COMMIT" != "$COMMIT" ]; then
  printf 'Релиз: checkout стоит не на коммите %s — переключись на выпускаемый срез\n' "$TAG" >&2
  exit 1
fi

MANIFEST="$ROOT/deploy/release/$TAG.json"
HASHES=""
if [ -f "$MANIFEST" ]; then
  HASHES="$(mktemp)"
  release_config_hashes "$ROOT" > "$HASHES"
  if ! release_verify_config "$MANIFEST" "$HASHES"; then
    rm -f "$HASHES"
    printf 'Релиз: конфигурации checkout не совпадают с manifest %s\n' "$TAG" >&2
    exit 1
  fi
else
  printf 'Релиз: manifest %s не найден — хэши не сверяются, checkout прибит к коммиту\n' \
    "$TAG" >&2
fi

TARGET="$VPS_USER@$VPS_HOST"

log() { printf '\033[1;32m[deploy]\033[0m %s\n' "$*"; }

log "Доставка конфигов на $TARGET:$APP_DIR"
ssh -p "$VPS_SSH_PORT" "$TARGET" "mkdir -p '$APP_DIR/deploy/systemd'"
scp -P "$VPS_SSH_PORT" docker-compose.prod.yml "$TARGET:$APP_DIR/docker-compose.prod.yml"
scp -P "$VPS_SSH_PORT" deploy/Caddyfile "$TARGET:$APP_DIR/Caddyfile"
scp -P "$VPS_SSH_PORT" deploy/otel-collector.yaml "$TARGET:$APP_DIR/otel-collector.yaml"
scp -P "$VPS_SSH_PORT" deploy/config.sh deploy/compose.sh deploy/release.sh \
  deploy/remote-deploy.sh deploy/backup.sh deploy/restore-drill.sh deploy/smoke.sh \
  "$TARGET:$APP_DIR/deploy/"
scp -P "$VPS_SSH_PORT" deploy/systemd/menu-backup.service deploy/systemd/menu-backup.timer \
  "$TARGET:$APP_DIR/deploy/systemd/"
if [ -f "$MANIFEST" ]; then
  scp -P "$VPS_SSH_PORT" "$MANIFEST" "$TARGET:$APP_DIR/deploy/release.json"
fi
[ -z "$HASHES" ] || rm -f "$HASHES"

log "Рестарт на теге $TAG ($COMMIT)"
ssh -p "$VPS_SSH_PORT" "$TARGET" "bash '$APP_DIR/deploy/remote-deploy.sh' '$TAG' '$COMMIT'"

log "Готово. Откат: ./deploy/deploy.sh <предыдущий-sha>"
