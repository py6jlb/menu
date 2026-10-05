#!/usr/bin/env bash
set -euo pipefail

# Полное проверочное восстановление полного (complete) backup-набора.
# Продовую базу, контейнеры, volumes и образы не трогает.
#
#   /opt/menu/deploy/restore-drill.sh                 # последний complete-набор
#   /opt/menu/deploy/restore-drill.sh <set-id>        # конкретный complete-набор
#
# Набор берётся только целиком: manifest, БД и архив фото одного <id> с маркером
# complete. Проверяются контрольные суммы, восстанавливается БД и фото в
# изолированную сеть, сверяются история миграций, контрольные рецепты/планы и
# каждый путь фото, затем запускается закреплённый в manifest релиз.
#
# Переменные (env или /opt/menu/server.conf):
#   BACKUP_REMOTE     rclone-remote (обязателен)
#   DOCKERHUB_USER    владелец образов релиза (обязателен)
#   POSTGRES_DB       имя БД, как в наборе (menu_planner)
#   POSTGRES_USER     роль-владелец объектов, как в наборе (menu)
#   DRILL_PASSWORD    пароль временной роли (drill-only; продовый секрет не используется)
#   DRILL_JWT_SECRET  секрет временного релиза (drill-only)
#   DRILL_CONTAINER   префикс имён временных контейнеров (menu-restore-drill)
#   BACKUP_STATE_FILE файл состояния (APP_DIR/backup-state)

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"

# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/backup-lib.sh"

log() { printf '\033[1;32m[restore-drill]\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m[restore-drill]\033[0m %s\n' "$*" >&2; exit 1; }

config_load "$APP_DIR/server.conf" server || exit 1
config_require BACKUP_REMOTE DOCKERHUB_USER

POSTGRES_DB="${POSTGRES_DB-menu_planner}"
POSTGRES_USER="${POSTGRES_USER-menu}"
DRILL_PASSWORD="${DRILL_PASSWORD:-drill-only}"
DRILL_JWT_SECRET="${DRILL_JWT_SECRET:-drill-only-jwt}"
BACKUP_STATE_FILE="${BACKUP_STATE_FILE-$APP_DIR/backup-state}"
DRILL_CONTAINER="${DRILL_CONTAINER-menu-restore-drill}-$$-$(date +%s%N 2>/dev/null || date +%s)"
DRILL_NET="${DRILL_CONTAINER}-net"
RELEASE_CONTAINER="${DRILL_CONTAINER}-release"
STARTED_AT="$(date +%s)"
SUCCESS=0
SET_ID="${1-}"

TMP="$(mktemp -d)"
cleanup() {
  local result="fail" finished elapsed
  [ "$SUCCESS" -eq 1 ] && result="ok"
  finished="$(backup_utc_now)"
  elapsed=$(( $(date +%s) - STARTED_AT ))
  backup_state_set "$BACKUP_STATE_FILE" last_drill_set "${SET_ID:--}" 2>/dev/null || true
  backup_state_set "$BACKUP_STATE_FILE" last_drill_at "$finished" 2>/dev/null || true
  backup_state_set "$BACKUP_STATE_FILE" last_drill_result "$result" 2>/dev/null || true
  backup_state_set "$BACKUP_STATE_FILE" last_drill_seconds "$elapsed" 2>/dev/null || true
  # Последняя успешно проверенная точка хранится отдельно, чтобы провалившийся
  # drill не снял защиту с прежнего проверенного набора.
  if [ "$result" = "ok" ]; then
    backup_state_set "$BACKUP_STATE_FILE" last_drill_ok_set "$SET_ID" 2>/dev/null || true
    backup_state_set "$BACKUP_STATE_FILE" last_drill_ok_at "$finished" 2>/dev/null || true
  fi
  docker rm -f -v "$DRILL_CONTAINER" "$RELEASE_CONTAINER" >/dev/null 2>&1 || true
  docker network rm "$DRILL_NET" >/dev/null 2>&1 || true
  rm -rf "$TMP"
}
trap cleanup EXIT

if [ -z "$SET_ID" ]; then
  log "Поиск последнего complete-набора в $BACKUP_REMOTE"
  SET_ID="$(backup_latest_set "$BACKUP_REMOTE")" || die "Не удалось прочитать список наборов"
  [ -n "$SET_ID" ] || die "В $BACKUP_REMOTE нет complete-наборов"
fi
backup_set_valid "$SET_ID" || die "Некорректный идентификатор набора: $SET_ID"
log "Набор: $SET_ID"

rclone copyto "$BACKUP_REMOTE/manifests/$SET_ID.json" "$TMP/manifest.json" 2>/dev/null \
  || die "Не найден manifest набора $SET_ID"

DB_NAME="$(backup_json_string "$TMP/manifest.json" dbName)"
PHOTOS_NAME="$(backup_json_string "$TMP/manifest.json" photosName)"
DB_SHA="$(backup_json_string "$TMP/manifest.json" dbSha256)"
PHOTOS_SHA="$(backup_json_string "$TMP/manifest.json" photosSha256)"
SCHEMA="$(backup_json_string "$TMP/manifest.json" schema)"
RELEASE="$(backup_json_string "$TMP/manifest.json" release)"
RECIPES="$(backup_json_number "$TMP/manifest.json" recipes)"
WEEK_PLANS="$(backup_json_number "$TMP/manifest.json" weekPlans)"
PLAN_ENTRIES="$(backup_json_number "$TMP/manifest.json" planEntries)"

# Значения из хранилища подставляются в пути и контейнеры: проверяем алфавит.
backup_name_valid "$DB_NAME" || die "Некорректное имя дампа в наборе"
backup_name_valid "$PHOTOS_NAME" || die "Некорректное имя архива фото в наборе"
backup_sha_valid "$DB_SHA" || die "Некорректная контрольная сумма дампа в наборе"
backup_sha_valid "$PHOTOS_SHA" || die "Некорректная контрольная сумма архива фото в наборе"
[[ "$SCHEMA" =~ ^[A-Za-z0-9_]+$ ]] || die "Некорректная схема в наборе"
for value in "$RECIPES" "$WEEK_PLANS" "$PLAN_ENTRIES"; do
  backup_number_valid "$value" || die "Некорректные контрольные объёмы в наборе"
done
config_image_tag "$RELEASE" || die "Некорректный release в наборе"

rclone copyto "$BACKUP_REMOTE/db/$DB_NAME" "$TMP/db.gz" 2>/dev/null \
  || die "Не найден дамп БД набора $SET_ID"
rclone copyto "$BACKUP_REMOTE/photos/$PHOTOS_NAME" "$TMP/photos.tar.gz" 2>/dev/null \
  || die "Не найден архив фото — набор $SET_ID неполный"
[ -s "$TMP/db.gz" ] || die "Дамп БД пуст"
[ -s "$TMP/photos.tar.gz" ] || die "Архив фото пуст"

# Контрольные суммы сверяются до запуска контейнеров: повреждённый набор не
# доходит до восстановления.
[ "$(backup_sha256 "$TMP/db.gz")" = "$DB_SHA" ] || die "Контрольная сумма дампа не совпадает"
[ "$(backup_sha256 "$TMP/photos.tar.gz")" = "$PHOTOS_SHA" ] || die "Контрольная сумма архива фото не совпадает"
gunzip -t "$TMP/db.gz" || die "Дамп повреждён (не gzip)"
tar tzf "$TMP/photos.tar.gz" >/dev/null || die "Архив фото нечитаем"

mkdir -p "$TMP/photos"
tar xzf "$TMP/photos.tar.gz" -C "$TMP/photos"
photos_listing="$(tar tzf "$TMP/photos.tar.gz" | sed -e 's|^\./||' -e 's|/$||' | grep -v '^$' || true)"

log "Изолированная сеть $DRILL_NET и временная Postgres $DRILL_CONTAINER"
docker network create --internal "$DRILL_NET" >/dev/null
docker run -d --name "$DRILL_CONTAINER" --network "$DRILL_NET" \
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
if ! gunzip -c "$TMP/db.gz" | docker exec -i "$DRILL_CONTAINER" \
    psql -v ON_ERROR_STOP=1 --single-transaction -q -U "$POSTGRES_USER" -d "$POSTGRES_DB"; then
  die "Восстановление SQL завершилось ошибкой"
fi

# История миграций: набор должен соответствовать схеме, которая в нём записана.
restored_schema="$(docker exec "$DRILL_CONTAINER" psql -tA -v ON_ERROR_STOP=1 \
  -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c 'select max("MigrationId") from "__EFMigrationsHistory";')"
restored_schema="$(printf '%s' "$restored_schema" | tr -d '\r' | tail -n1)"
[ -n "$restored_schema" ] || die "История миграций восстановленной БД пуста"
[ "$restored_schema" = "$SCHEMA" ] \
  || die "Схема восстановленной БД ($restored_schema) не совпадает с набором ($SCHEMA)"

# Контрольные рецепты/планы и ссылочная целостность.
counts="$(docker exec "$DRILL_CONTAINER" psql -tA -F'|' -v ON_ERROR_STOP=1 \
  -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  -c 'select (select count(*) from "Recipes"), (select count(*) from "WeekPlans"), (select count(*) from "PlanEntries");')"
counts="$(printf '%s' "$counts" | tr -d '\r' | tail -n1)"
IFS='|' read -r restored_recipes restored_plans restored_entries <<< "$counts"
[ "$restored_recipes" = "$RECIPES" ] && [ "$restored_plans" = "$WEEK_PLANS" ] \
  && [ "$restored_entries" = "$PLAN_ENTRIES" ] \
  || die "Контрольные рецепты/планы восстановленной БД не совпадают с набором"

orphans="$(docker exec "$DRILL_CONTAINER" psql -tA -v ON_ERROR_STOP=1 \
  -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  -c 'select count(*) from "PlanEntries" pe left join "Recipes" r on pe."RecipeId" = r."Id" where r."Id" is null;')"
orphans="$(printf '%s' "$orphans" | tr -d '\r' | tail -n1)"
[ "$orphans" = "0" ] || die "Записи плана ссылаются на отсутствующие рецепты"

# Каждый путь фото из восстановленной БД должен разрешаться в архиве.
photo_paths="$(docker exec "$DRILL_CONTAINER" psql -tA -v ON_ERROR_STOP=1 \
  -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  -c 'select "PhotoPath" from "Recipes" where "PhotoPath" is not null;')"
while IFS= read -r path; do
  path="$(printf '%s' "$path" | tr -d '\r')"
  [ -n "$path" ] || continue
  name="$(basename "$path")"
  printf '%s\n' "$photos_listing" | grep -qx "$name" \
    || die "Фото '$name' из восстановленной БД отсутствует в архиве"
done <<< "$photo_paths"

# Закреплённый релиз запускается против восстановленной БД: startup-миграции
# должны быть no-op, приложение должно ответить на health.
log "Запуск закреплённого релиза $RELEASE"
docker run -d --name "$RELEASE_CONTAINER" --network "$DRILL_NET" \
  -v "$TMP/photos":/app/photos:ro \
  -e DB_HOST="$DRILL_CONTAINER" \
  -e DB_PORT=5432 \
  -e DB_NAME="$POSTGRES_DB" \
  -e DB_USER="$POSTGRES_USER" \
  -e DB_PASSWORD="$DRILL_PASSWORD" \
  -e JWT_SECRET="$DRILL_JWT_SECRET" \
  -e PHOTOS_DIR=/app/photos \
  "$DOCKERHUB_USER/menu-backend:$RELEASE" >/dev/null

for _ in $(seq 1 30); do
  if docker exec "$RELEASE_CONTAINER" bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080' >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
docker exec "$RELEASE_CONTAINER" bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080' >/dev/null 2>&1 \
  || die "Закреплённый релиз $RELEASE не ответил на health"

SUCCESS=1
elapsed=$(( $(date +%s) - STARTED_AT ))
log "Drill успешен: набор $SET_ID, схема $restored_schema, рецептов $restored_recipes, релиз $RELEASE"
log "Время восстановления: ${elapsed}s (сохранено в $BACKUP_STATE_FILE)"
