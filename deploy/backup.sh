#!/usr/bin/env bash
set -euo pipefail

# Бэкап БД и фото в объектное хранилище через rclone. Запускается на сервере.
#
#   /opt/menu/deploy/backup.sh
#
# Переменные (env или /opt/menu/server.conf):
#   BACKUP_REMOTE     rclone-remote, например "myremote:bucket/menu" (обязателен)
#   BACKUP_KEEP_DAILY число дневных копий (7)
#   BACKUP_KEEP_WEEKLY число недельных копий (4)
#   COMPOSE_FILE      путь к compose (docker-compose.prod.yml)

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"

# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
config_server
config_require BACKUP_REMOTE

BACKUP_KEEP_DAILY="${BACKUP_KEEP_DAILY-7}"
BACKUP_KEEP_WEEKLY="${BACKUP_KEEP_WEEKLY-4}"
POSTGRES_DB="${POSTGRES_DB-menu_planner}"
POSTGRES_USER="${POSTGRES_USER-menu}"

# Значения участвуют в bash-арифметике: произвольный текст там небезопасен.
for key in BACKUP_KEEP_DAILY BACKUP_KEEP_WEEKLY; do
  if [[ ! "${!key}" =~ ^[1-9][0-9]{0,3}$ ]]; then
    printf 'Конфигурация: %s должен быть целым от 1 до 9999 без ведущих нулей\n' "$key" >&2
    exit 1
  fi
done

STAMP="$(date +%F)"
WEEKDAY="$(date +%u)"

log() { printf '\033[1;32m[backup]\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m[backup]\033[0m %s\n' "$*" >&2; exit 1; }

TMP="$(mktemp -d)"
cleanup() { rm -rf "$TMP"; }
trap cleanup EXIT

dump_database() {
  log "Дамп БД $POSTGRES_DB"
  menu_compose exec -T db \
    pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" | gzip > "$TMP/db-$STAMP.sql.gz"
  [ -s "$TMP/db-$STAMP.sql.gz" ] || die "Дамп пуст"
}

archive_photos() {
  log "Архив фото"
  local backend_id photos_volume
  backend_id="$(menu_compose ps -q backend)"
  [ -n "$backend_id" ] || die "Контейнер backend не запущен"
  photos_volume="$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/app/photos"}}{{.Name}}{{end}}{{end}}' "$backend_id")"
  [ -n "$photos_volume" ] || die "Не найден volume с фото"
  docker run --rm -v "$photos_volume":/data:ro -v "$TMP":/backup alpine:3.20 \
    tar czf "/backup/photos-$STAMP.tar.gz" -C /data .
  [ -s "$TMP/photos-$STAMP.tar.gz" ] || die "Архив фото пуст"
}

upload() {
  log "Выгрузка в $BACKUP_REMOTE"
  rclone copyto "$TMP/db-$STAMP.sql.gz" "$BACKUP_REMOTE/db/$STAMP.sql.gz"
  rclone copyto "$TMP/photos-$STAMP.tar.gz" "$BACKUP_REMOTE/photos/$STAMP.tar.gz"

  if [ "$WEEKDAY" -eq 7 ]; then
    log "Недельная копия"
    rclone copyto "$BACKUP_REMOTE/db/$STAMP.sql.gz" "$BACKUP_REMOTE/weekly/$STAMP-db.sql.gz"
    rclone copyto "$BACKUP_REMOTE/photos/$STAMP.tar.gz" "$BACKUP_REMOTE/weekly/$STAMP-photos.tar.gz"
  fi
}

# Независимое подтверждение доставки: copyto мог завершиться успешно без объекта.
verify_upload() {
  local file dir
  for file in "$STAMP.sql.gz" "$STAMP.tar.gz"; do
    if [ "$file" = "$STAMP.sql.gz" ]; then dir=db; else dir=photos; fi
    if ! rclone lsf "$BACKUP_REMOTE/$dir/" --files-only 2>/dev/null | grep -qx "$file"; then
      die "Проверка выгрузки: $dir/$file не найден в $BACKUP_REMOTE"
    fi
  done
}

prune() {
  local dir="$1" keep="$2" cutoff file date_part listing code
  cutoff="$(date -d "$((keep - 1)) days ago" +%F)"
  log "Чистка $dir (оставляем с $cutoff)"
  # lsf на отсутствующий каталог даёт код 3 — это нормально до первой недели.
  # Прочие ошибки (доступ к хранилищу) не должны выдаваться за успех.
  set +e
  listing="$(rclone lsf "$BACKUP_REMOTE/$dir/" --files-only 2>/dev/null)"
  code=$?
  set -e
  if [ "$code" -ne 0 ] && [ "$code" -ne 3 ]; then
    die "Не удалось прочитать $BACKUP_REMOTE/$dir (rclone код $code)"
  fi
  while IFS= read -r file; do
    [ -n "$file" ] || continue
    date_part="${file:0:10}"
    if [[ "$date_part" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] && [[ "$date_part" < "$cutoff" ]]; then
      rclone deletefile "$BACKUP_REMOTE/$dir/$file"
    fi
  done <<< "$listing"
}

dump_database
archive_photos
upload
verify_upload
prune db "$BACKUP_KEEP_DAILY"
prune photos "$BACKUP_KEEP_DAILY"
prune weekly "$((BACKUP_KEEP_WEEKLY * 7))"

log "Готово: db-$STAMP.sql.gz, photos-$STAMP.tar.gz"
