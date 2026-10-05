#!/usr/bin/env bash
set -euo pipefail

# Проверочное восстановление последнего дневного бэкапа в отдельную БД.
# Продовую базу не трогает.
#
#   /opt/menu/deploy/restore-drill.sh [путь-к-дампу.sql.gz]
#
# Без аргумента скачивает последний дневной дамп из BACKUP_REMOTE.

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"

# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
config_load "$APP_DIR/server.conf" server
config_require BACKUP_REMOTE

DRILL_CONTAINER="${DRILL_CONTAINER-menu-restore-drill}"
DRILL_PASSWORD="drill-only"

log() { printf '\033[1;32m[restore-drill]\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m[restore-drill]\033[0m %s\n' "$*" >&2; exit 1; }

TMP="$(mktemp -d)"
cleanup() {
  docker rm -f "$DRILL_CONTAINER" >/dev/null 2>&1 || true
  rm -rf "$TMP"
}
trap cleanup EXIT

dump="$1"
if [ -z "$dump" ]; then
  log "Поиск последнего дампа в $BACKUP_REMOTE/db/"
  latest="$(rclone lsf "$BACKUP_REMOTE/db/" --files-only | sort | tail -n1 || true)"
  [ -n "$latest" ] || die "В $BACKUP_REMOTE/db/ нет дампов"
  dump="$TMP/$latest"
  rclone copyto "$BACKUP_REMOTE/db/$latest" "$dump"
fi
[ -s "$dump" ] || die "Дамп не найден или пуст: $dump"

log "Запуск временной Postgres"
docker run -d --name "$DRILL_CONTAINER" \
  -e POSTGRES_DB=menu_planner \
  -e POSTGRES_USER=menu \
  -e POSTGRES_PASSWORD="$DRILL_PASSWORD" \
  postgres:16 >/dev/null

for _ in $(seq 1 30); do
  if docker exec "$DRILL_CONTAINER" pg_isready -U menu -d menu_planner >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
docker exec "$DRILL_CONTAINER" pg_isready -U menu -d menu_planner >/dev/null 2>&1 \
  || die "Postgres не поднялся"

log "Восстановление дампа"
gunzip -c "$dump" | docker exec -i "$DRILL_CONTAINER" psql -q -U menu -d menu_planner >/dev/null

tables="$(docker exec "$DRILL_CONTAINER" psql -tA -U menu -d menu_planner \
  -c "select count(*) from information_schema.tables where table_schema = 'public';")"
[ "$tables" -gt 0 ] || die "После восстановления нет таблиц"
log "Таблиц в восстановленной БД: $tables"

if [ -n "${BACKUP_REMOTE:-}" ]; then
  photos_latest="$(rclone lsf "$BACKUP_REMOTE/photos/" --files-only | sort | tail -n1 || true)"
  if [ -n "$photos_latest" ]; then
    rclone copyto "$BACKUP_REMOTE/photos/$photos_latest" "$TMP/photos.tar.gz"
    count="$(tar tzf "$TMP/photos.tar.gz" | wc -l)"
    log "Файлов в последнем архиве фото: $count"
  fi
fi

log "Drill успешен"
