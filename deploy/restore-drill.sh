#!/usr/bin/env bash
set -euo pipefail

# Проверочное восстановление последнего дневного бэкапа в отдельную БД.
# Продовую базу, контейнеры и volumes не трогает.
#
#   /opt/menu/deploy/restore-drill.sh              # последний полный набор из BACKUP_REMOTE
#   /opt/menu/deploy/restore-drill.sh <dump.sql.gz>  # конкретный дамп, фото — последние из remote
#
# Без аргумента скачивает последний дневной дамп из BACKUP_REMOTE. Дамп и архив
# фото обязательны: неполный набор не считается успешным drill.
#
# Переменные (env или /opt/menu/server.conf):
#   BACKUP_REMOTE     rclone-remote (обязателен)
#   POSTGRES_DB       имя БД, как в дампе (menu_planner)
#   POSTGRES_USER     роль-владелец объектов, как в дампе (menu)
#   DRILL_PASSWORD    пароль временной роли (drill-only; продовый секрет не используется)
#   DRILL_CONTAINER   префикс имени временного контейнера (menu-restore-drill)

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"

# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
config_load "$APP_DIR/server.conf" server || exit 1
config_require BACKUP_REMOTE

# Те же значения, что у backup.sh: настройки роли/БД совпадают с дампом.
# Пароль временной роли не берём из прода: pg_dump plain не содержит паролей ролей.
POSTGRES_DB="${POSTGRES_DB-menu_planner}"
POSTGRES_USER="${POSTGRES_USER-menu}"
DRILL_PASSWORD="${DRILL_PASSWORD:-drill-only}"

log() { printf '\033[1;32m[restore-drill]\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m[restore-drill]\033[0m %s\n' "$*" >&2; exit 1; }

# Уникальное имя: параллельный drill или остатки прошлого запуска не столкнутся
# с этим контейнером и его данными.
DRILL_CONTAINER="${DRILL_CONTAINER-menu-restore-drill}-$$-$(date +%s%N 2>/dev/null || date +%s)"

TMP="$(mktemp -d)"
cleanup() {
  # -v снимает и временный volume PGDATA, чтобы после drill не оставалось мусора.
  docker rm -f -v "$DRILL_CONTAINER" >/dev/null 2>&1 || true
  rm -rf "$TMP"
}
trap cleanup EXIT

dump="${1-}"
if [ -z "$dump" ]; then
  log "Поиск последнего дампа в $BACKUP_REMOTE/db/"
  latest="$(rclone lsf "$BACKUP_REMOTE/db/" --files-only 2>/dev/null | sort | tail -n1 || true)"
  [ -n "$latest" ] || die "В $BACKUP_REMOTE/db/ нет дампов"
  dump="$TMP/$latest"
  rclone copyto "$BACKUP_REMOTE/db/$latest" "$dump"
fi
[ -s "$dump" ] || die "Дамп не найден или пуст: $dump"
gunzip -t "$dump" || die "Дамп повреждён (не gzip): $dump"

log "Запуск временной Postgres в изолированном контейнере $DRILL_CONTAINER"
docker run -d --name "$DRILL_CONTAINER" --network none \
  -e POSTGRES_DB="$POSTGRES_DB" \
  -e POSTGRES_USER="$POSTGRES_USER" \
  -e POSTGRES_PASSWORD="$DRILL_PASSWORD" \
  postgres:16 >/dev/null

for _ in $(seq 1 30); do
  if docker exec "$DRILL_CONTAINER" pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
docker exec "$DRILL_CONTAINER" pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null 2>&1 \
  || die "Postgres не поднялся"

log "Восстановление дампа"
if ! gunzip -c "$dump" | docker exec -i "$DRILL_CONTAINER" \
    psql -v ON_ERROR_STOP=1 --single-transaction -q -U "$POSTGRES_USER" -d "$POSTGRES_DB"; then
  die "Восстановление SQL завершилось ошибкой"
fi

tables="$(docker exec "$DRILL_CONTAINER" psql -tA -v ON_ERROR_STOP=1 \
  -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  -c "select count(*) from information_schema.tables where table_schema = 'public';")"
[[ "$tables" =~ ^[0-9]+$ ]] || die "Не удалось прочитать число таблиц после восстановления"
[ "$tables" -gt 0 ] || die "После восстановления нет таблиц"
log "Таблиц в восстановленной БД: $tables"

photos_latest="$(rclone lsf "$BACKUP_REMOTE/photos/" --files-only 2>/dev/null | sort | tail -n1 || true)"
[ -n "$photos_latest" ] || die "В $BACKUP_REMOTE/photos/ нет архива фото — набор неполный"
rclone copyto "$BACKUP_REMOTE/photos/$photos_latest" "$TMP/photos.tar.gz"
tar tzf "$TMP/photos.tar.gz" >/dev/null || die "Архив фото нечитаем: $photos_latest"
count="$(tar tzf "$TMP/photos.tar.gz" | wc -l)"
log "Файлов в последнем архиве фото: $count"

log "Drill успешен"
