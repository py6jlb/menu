#!/usr/bin/env bash
set -euo pipefail

# Согласованный backup-набор БД и фото в объектное хранилище через rclone.
# Запускается на сервере.
#
#   /opt/menu/deploy/backup.sh
#
# Каждый запуск создаёт уникальный набор <id>, upload всех частей проверяется, и
# только затем публикуется маркер complete/<id>. На время снятия набора backend
# кратко останавливается: записи БД и удаление/замена фото не пересекаются, поэтому
# БД и архив фото относятся к одной точке (клиент на это окно получает 5xx и повтор).
#
# Переменные (env или /opt/menu/server.conf):
#   BACKUP_REMOTE     rclone-remote, например "myremote:bucket/menu" (обязателен)
#   BACKUP_KEEP_DAILY число дневных наборов (7)
#   BACKUP_KEEP_WEEKLY число недельных наборов (4)
#   COMPOSE_FILE      путь к compose (docker-compose.prod.yml)
#   BACKUP_STATE_FILE файл состояния (APP_DIR/backup-state)

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"

# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/backup-lib.sh"

log() { printf '\033[1;32m[backup]\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m[backup]\033[0m %s\n' "$*" >&2; exit 1; }

config_server
config_require BACKUP_REMOTE

BACKUP_KEEP_DAILY="${BACKUP_KEEP_DAILY-7}"
BACKUP_KEEP_WEEKLY="${BACKUP_KEEP_WEEKLY-4}"
# Ресурсный бюджет вспомогательной операции: место под архивы/журналы и память
# контейнера архивации. Не задаётся в server.conf — окружение процесса.
BACKUP_MIN_FREE_MB="${BACKUP_MIN_FREE_MB-1024}"
BACKUP_MEMORY_LIMIT="${BACKUP_MEMORY_LIMIT-512m}"
POSTGRES_DB="${POSTGRES_DB-menu_planner}"
POSTGRES_USER="${POSTGRES_USER-menu}"
BACKUP_STATE_FILE="${BACKUP_STATE_FILE-$APP_DIR/backup-state}"
RELEASE="${IMAGE_TAG:-latest}"
config_image_tag "$RELEASE" || die "Некорректный IMAGE_TAG"

# Значения участвуют в bash-арифметике: произвольный текст там небезопасен.
for key in BACKUP_KEEP_DAILY BACKUP_KEEP_WEEKLY; do
  if [[ ! "${!key}" =~ ^[1-9][0-9]{0,3}$ ]]; then
    printf 'Конфигурация: %s должен быть целым от 1 до 9999 без ведущих нулей\n' "$key" >&2
    exit 1
  fi
done
backup_positive_int_valid "$BACKUP_MIN_FREE_MB" \
  || die "BACKUP_MIN_FREE_MB должен быть целым > 0"
backup_size_valid "$BACKUP_MEMORY_LIMIT" \
  || die "BACKUP_MEMORY_LIMIT должен быть размером вида 512m или 1g"

SET_ID="$(backup_set_id)"
CREATED_AT="$(backup_utc_now)"
WEEKDAY="$(date +%u)"

TMP="$(mktemp -d)"
# Проверка места до остановки backend: нет места — нет вспомогательной операции
# и нет окна запрета изменений.
backup_require_free_space "$TMP" "$BACKUP_MIN_FREE_MB" \
  || die "Недостаточно места для backup-набора"
QUIESCED=0

resume_backend() {
  if [ "$QUIESCED" -eq 1 ]; then
    menu_compose start backend >/dev/null 2>&1 || true
    QUIESCED=0
  fi
}

cleanup() {
  # Backend не должен остаться остановленным даже при аварийном выходе.
  resume_backend
  rm -rf "$TMP"
}
trap cleanup EXIT

# Ищем volume фото у остановленного backend: compose ps -q -a видит и
# остановленный контейнер, поэтому имя volume доступно и после quiesce.
resolve_photos_volume() {
  local backend_id volume
  backend_id="$(menu_compose ps -q -a backend)"
  [ -n "$backend_id" ] || die "Контейнер backend не запущен"
  volume="$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/app/photos"}}{{.Name}}{{end}}{{end}}' "$backend_id")"
  [ -n "$volume" ] || die "Не найден volume с фото"
  printf '%s' "$volume"
}

resolve_documents_volume() {
  local backend_id volume
  backend_id="$(menu_compose ps -q -a backend)"
  [ -n "$backend_id" ] || die "Контейнер backend не запущен"
  volume="$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/app/documents"}}{{.Name}}{{end}}{{end}}' "$backend_id")"
  [ -n "$volume" ] || die "Не найден volume с документами"
  printf '%s' "$volume"
}

dump_database() {
  log "Дамп БД $POSTGRES_DB"
  menu_compose exec -T db \
    pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" | gzip > "$TMP/db.gz"
  [ -s "$TMP/db.gz" ] || die "Дамп пуст"
}

archive_photos() {
  log "Архив фото"
  docker run --rm --memory "$BACKUP_MEMORY_LIMIT" \
    -v "$PHOTOS_VOLUME":/data:ro -v "$TMP":/backup alpine:3.20 \
    tar czf /backup/photos.tar.gz -C /data .
  [ -s "$TMP/photos.tar.gz" ] || die "Архив фото пуст"
}

archive_documents() {
  log "Архив PDF-документов"
  docker run --rm --memory "$BACKUP_MEMORY_LIMIT" \
    -v "$DOCUMENTS_VOLUME":/data:ro -v "$TMP":/backup alpine:3.20 \
    tar czf /backup/documents.tar.gz -C /data .
  [ -s "$TMP/documents.tar.gz" ] || die "Архив документов пуст"
}

# Согласованное снятие: backend (единственный писатель БД и фото) остановлен, БД
# и архив относятся к одной точке. Короткое окно запрета изменений: пользователь
# получает 5xx и может повторить запрос, данные не теряются.
snapshot() {
  log "Остановка backend на время согласованного снятия набора"
  menu_compose stop backend >/dev/null
  QUIESCED=1
  PHOTOS_VOLUME="$(resolve_photos_volume)"
  DOCUMENTS_VOLUME="$(resolve_documents_volume)"
  dump_database
  archive_photos
  archive_documents
  # Схема и контрольные объёмы читаются в том же окне, что и дамп: manifest
  # описывает ровно то состояние, которое попало в набор.
  read_db_state
  resume_backend
}

# Сведения о схеме и контрольные объёмы: drill сверяет их с восстановленной БД.
read_db_state() {
  local sql out line
  sql='select (select max("MigrationId") from "__EFMigrationsHistory"), (select count(*) from "Recipes"), (select count(*) from "WeekPlans"), (select count(*) from "PlanEntries");'
  out="$(menu_compose exec -T db psql -tA -F'|' -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "$sql")"
  line="$(printf '%s' "$out" | tr -d '\r' | tail -n1)"
  IFS='|' read -r SCHEMA RECIPES WEEK_PLANS PLAN_ENTRIES <<< "$line"
  [ -n "$SCHEMA" ] || die "Не удалось прочитать схему БД (история миграций пуста)"
  [[ "$SCHEMA" =~ ^[A-Za-z0-9_]+$ ]] || die "Некорректная схема БД"
  for value in "$RECIPES" "$WEEK_PLANS" "$PLAN_ENTRIES"; do
    backup_number_valid "$value" || die "Не удалось прочитать контрольные объёмы БД"
  done
}

upload() {
  log "Выгрузка набора $SET_ID в $BACKUP_REMOTE"
  rclone copyto "$TMP/db.gz" "$BACKUP_REMOTE/db/$SET_ID.sql.gz"
  rclone copyto "$TMP/photos.tar.gz" "$BACKUP_REMOTE/photos/$SET_ID.tar.gz"
  rclone copyto "$TMP/documents.tar.gz" "$BACKUP_REMOTE/documents/$SET_ID.tar.gz"
  rclone copyto "$TMP/manifest.json" "$BACKUP_REMOTE/manifests/$SET_ID.json"
}

# Независимое подтверждение доставки: copyto мог завершиться успешно без объекта.
verify_objects() {
  local spec dir file
  for spec in "db:$SET_ID.sql.gz" "photos:$SET_ID.tar.gz" "documents:$SET_ID.tar.gz" "manifests:$SET_ID.json"; do
    dir="${spec%%:*}"
    file="${spec#*:}"
    if ! rclone lsf "$BACKUP_REMOTE/$dir/" --files-only 2>/dev/null | grep -qx "$file"; then
      die "Проверка выгрузки: $dir/$file не найден в $BACKUP_REMOTE"
    fi
  done
}

# Маркер публикуется последним: до него набор не считается пригодным.
publish_complete() {
  printf '%s\n' "$SET_ID" > "$TMP/complete"
  rclone copyto "$TMP/complete" "$BACKUP_REMOTE/complete/$SET_ID"
  if ! rclone lsf "$BACKUP_REMOTE/complete/" --files-only 2>/dev/null | grep -qx "$SET_ID"; then
    die "complete-отметка не опубликована для $SET_ID"
  fi
}

copy_weekly() {
  log "Недельный набор $SET_ID"
  rclone copyto "$BACKUP_REMOTE/db/$SET_ID.sql.gz" "$BACKUP_REMOTE/weekly/db/$SET_ID.sql.gz"
  rclone copyto "$BACKUP_REMOTE/photos/$SET_ID.tar.gz" "$BACKUP_REMOTE/weekly/photos/$SET_ID.tar.gz"
  rclone copyto "$BACKUP_REMOTE/documents/$SET_ID.tar.gz" "$BACKUP_REMOTE/weekly/documents/$SET_ID.tar.gz"
  rclone copyto "$BACKUP_REMOTE/manifests/$SET_ID.json" "$BACKUP_REMOTE/weekly/manifests/$SET_ID.json"
  rclone copyto "$BACKUP_REMOTE/complete/$SET_ID" "$BACKUP_REMOTE/weekly/complete/$SET_ID"
}

PHOTOS_VOLUME=""
DOCUMENTS_VOLUME=""
snapshot

DB_SHA="$(backup_sha256 "$TMP/db.gz")"
PHOTOS_SHA="$(backup_sha256 "$TMP/photos.tar.gz")"
DOCUMENTS_SHA="$(backup_sha256 "$TMP/documents.tar.gz")"

backup_manifest_write "$TMP/manifest.json" "$SET_ID" "$CREATED_AT" "$RELEASE" "$SCHEMA" \
  "$SET_ID.sql.gz" "$DB_SHA" "$SET_ID.tar.gz" "$PHOTOS_SHA" \
  "$SET_ID.tar.gz" "$DOCUMENTS_SHA" \
  "$RECIPES" "$WEEK_PLANS" "$PLAN_ENTRIES"

upload
verify_objects
publish_complete
if [ "$WEEKDAY" -eq 7 ]; then
  copy_weekly
fi

# Ротация целых наборов. Последняя успешно проверенная drill-точка не удаляется,
# даже если по дате попала в чистку; провал более позднего drill её не снимает.
PROTECT="$(backup_state_get "$BACKUP_STATE_FILE" last_drill_ok_set)"
backup_prune "$BACKUP_REMOTE" "" "$BACKUP_KEEP_DAILY" "$PROTECT" \
  || die "Чистка дневных наборов завершилась ошибкой"
backup_prune "$BACKUP_REMOTE" "/weekly" "$((BACKUP_KEEP_WEEKLY * 7))" "$PROTECT" \
  || die "Чистка недельных наборов завершилась ошибкой"

backup_state_set "$BACKUP_STATE_FILE" last_full_backup "$SET_ID"
backup_state_set "$BACKUP_STATE_FILE" last_full_backup_at "$CREATED_AT"
# Схема последнего набора нужна откату, чтобы привязать точку к обновлению.
backup_state_set "$BACKUP_STATE_FILE" last_full_backup_schema "$SCHEMA"

log "Готово: набор $SET_ID (release $RELEASE, схема $SCHEMA, рецептов $RECIPES)"
