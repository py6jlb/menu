#!/usr/bin/env bash
set -euo pipefail

# Серверный откат релиза. Два явных пути (не путать):
#
#   code-only  — прежний образ, схема признана совместимой. Схема НЕ откатывается:
#                миграции forward-only, приложение просто едет на прежнем коде.
#   recovery   — изменение схемы несовместимо. Записи останавливаются, данные и
#                фото восстанавливаются из complete-набора, привязанного к
#                обновлению (recovery-point), затем выбирается прежний release и
#                проверяется пользовательский доступ.
#
#   /opt/menu/deploy/rollback.sh [<tag>] [--yes] [--code-only]
#
# Без аргумента берётся previous-release. Recovery требует --yes (или
# ROLLBACK_ASSUME_YES=1): данные, созданные после точки восстановления, теряются.
# Деплои и откаты сериализуются lock-файлом $APP_DIR/deploy.lock.
#
# Down-миграции не являются поддерживаемым способом отката: схему возвращает
# только восстановление из набора.
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/release.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/backup-lib.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/deploy-lib.sh"

log() { printf '\033[1;32m[rollback]\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m[rollback]\033[0m %s\n' "$*" >&2; exit 1; }

TARGET=""
ASSUME_YES="${ROLLBACK_ASSUME_YES:-0}"
CODE_ONLY=0
for arg in "$@"; do
  case "$arg" in
    --yes)       ASSUME_YES=1 ;;
    --code-only) CODE_ONLY=1 ;;
    "")          ;;
    -*)          die "Неизвестный аргумент: $arg" ;;
    *)           [ -z "$TARGET" ] || die "Указано несколько тегов"
                 TARGET="$arg" ;;
  esac
done

config_server

if [ "${DEPLOY_LOCK_HELD:-0}" != "1" ]; then
  deploy_lock_acquire || exit 1
fi

CURRENT_TAG="$(deploy_state_get "$APP_DIR/current-release" IMAGE_TAG)"
[ -n "$CURRENT_TAG" ] || CURRENT_TAG="${IMAGE_TAG-latest}"
config_image_tag "$CURRENT_TAG" || die "Некорректный текущий релиз"

# Прерванный деплой рискованного обновления: current-release ещё прежний, но
# deploy-intent помнит целевой релиз, к которому привязана recovery-точка.
INTENT_TAG="$(deploy_state_get "$APP_DIR/deploy-intent" TAG)"
RECOVERY_SET=""
RECOVERY_FOR=""
for candidate in "$CURRENT_TAG" "$INTENT_TAG"; do
  [ -n "$candidate" ] || continue
  set="$(deploy_recovery_set_for "$APP_DIR/recovery-point" "$candidate")"
  if [ -n "$set" ]; then
    RECOVERY_SET="$set"
    RECOVERY_FOR="$candidate"
    break
  fi
done
RECOVERY_SCHEMA="$(deploy_state_get "$APP_DIR/recovery-point" SCHEMA)"
RECOVERY_CREATED="$(deploy_state_get "$APP_DIR/recovery-point" CREATED_AT)"
RECOVERY_FROM="$(deploy_state_get "$APP_DIR/recovery-point" FROM_RELEASE)"

if [ -z "$TARGET" ]; then
  TARGET="$(deploy_state_get "$APP_DIR/previous-release" IMAGE_TAG)"
  # При прерванном рискованном деплое previous-release ещё не записан — берём
  # релиз, под которым снята recovery-точка.
  [ -n "$TARGET" ] || TARGET="$RECOVERY_FROM"
fi
[ -n "$TARGET" ] || die "Нет previous-release: укажи <tag> явно или сначала выполни штатный деплой"
config_image_tag "$TARGET" || die "Некорректный тег отката"
if [ "$TARGET" = "$CURRENT_TAG" ] && [ -z "$RECOVERY_SET" ]; then
  die "Текущий релиз уже $TARGET — откатывать некуда"
fi

if [ -n "$RECOVERY_SET" ] && [ "$CODE_ONLY" -eq 1 ]; then
  die "Для $RECOVERY_FOR есть несовместимое изменение схемы: code-only запрещён, нужен recovery из набора $RECOVERY_SET"
fi

mark_stage() {
  deploy_state_set "$APP_DIR/rollback-state" STAGE "$1"
  deploy_state_set "$APP_DIR/rollback-state" FROM "$CURRENT_TAG"
  deploy_state_set "$APP_DIR/rollback-state" TO "$TARGET"
  [ -z "$RECOVERY_SET" ] || deploy_state_set "$APP_DIR/rollback-state" SET "$RECOVERY_SET"
}

switch_release() {
  # Тот же серверный путь, что у штатного деплоя: pull, up, ожидание /ready,
  # запись release.json и current/previous. Lock уже удержан нами.
  DEPLOY_LOCK_HELD=1 bash "$APP_DIR/deploy/remote-deploy.sh" "$TARGET"
}

if [ -z "$RECOVERY_SET" ]; then
  log "Откат code-only: $CURRENT_TAG -> $TARGET"
  log "Схема БД НЕ откатывается (миграции forward-only). Совместимость подтверждает оператор."
  mark_stage code-only
  switch_release
  rm -f "$APP_DIR/rollback-state"
  log "Готово: release $TARGET (previous $(deploy_state_get "$APP_DIR/previous-release" IMAGE_TAG))"
  exit 0
fi

# --- recovery -----------------------------------------------------------------
log "Recovery-откат: $CURRENT_TAG -> $TARGET"
printf '\033[1;33m[rollback]\033[0m ВНИМАНИЕ: данные и фото, созданные после\n' >&2
printf '  точки восстановления %s (%s), будут потеряны.\n' "$RECOVERY_SET" "$RECOVERY_CREATED" >&2
printf '  Восстанавливается схема %s, затем ставится прежний release %s.\n' \
  "$RECOVERY_SCHEMA" "$TARGET" >&2
if [ "$ASSUME_YES" -ne 1 ]; then
  die "Подтверди восстановление: rollback.sh $TARGET --yes (данные после точки теряются)"
fi

config_require BACKUP_REMOTE

POSTGRES_DB="${POSTGRES_DB-menu_planner}"
POSTGRES_USER="${POSTGRES_USER-menu}"
ROLLBACK_MEMORY_LIMIT="${ROLLBACK_MEMORY_LIMIT-512m}"
backup_size_valid "$ROLLBACK_MEMORY_LIMIT" \
  || die "ROLLBACK_MEMORY_LIMIT должен быть размером вида 512m или 1g"
backup_set_valid "$RECOVERY_SET" || die "Некорректный backup ID recovery-точки"

RBTMP="$(mktemp -d)"
cleanup() { rm -rf "$RBTMP"; }
trap cleanup EXIT
STARTED_AT="$(date +%s)"

RECOVERY_DOCUMENTS=0

rollback_download_verify() {
  local set="$1" db_name photos_name documents_name db_sha photos_sha documents_sha schema
  log "Скачивание recovery-набора $set"
  rclone copyto "$BACKUP_REMOTE/manifests/$set.json" "$RBTMP/manifest.json" 2>/dev/null \
    || die "Не найден manifest набора $set"
  db_name="$(backup_json_string "$RBTMP/manifest.json" dbName)"
  photos_name="$(backup_json_string "$RBTMP/manifest.json" photosName)"
  documents_name="$(backup_json_string "$RBTMP/manifest.json" documentsName)"
  db_sha="$(backup_json_string "$RBTMP/manifest.json" dbSha256)"
  photos_sha="$(backup_json_string "$RBTMP/manifest.json" photosSha256)"
  documents_sha="$(backup_json_string "$RBTMP/manifest.json" documentsSha256)"
  schema="$(backup_json_string "$RBTMP/manifest.json" schema)"
  backup_name_valid "$db_name" || die "Некорректное имя дампа в наборе"
  backup_name_valid "$photos_name" || die "Некорректное имя архива фото в наборе"
  backup_sha_valid "$db_sha" || die "Некорректная контрольная сумма дампа"
  backup_sha_valid "$photos_sha" || die "Некорректная контрольная сумма архива фото"
  [ "$schema" = "$RECOVERY_SCHEMA" ] \
    || die "Схема набора ($schema) не совпадает с привязанной ($RECOVERY_SCHEMA)"
  rclone copyto "$BACKUP_REMOTE/db/$db_name" "$RBTMP/db.gz" 2>/dev/null \
    || die "Не найден дамп БД набора $set"
  rclone copyto "$BACKUP_REMOTE/photos/$photos_name" "$RBTMP/photos.tar.gz" 2>/dev/null \
    || die "Не найден архив фото набора $set"
  [ -s "$RBTMP/db.gz" ] || die "Дамп БД пуст"
  [ -s "$RBTMP/photos.tar.gz" ] || die "Архив фото пуст"
  [ "$(backup_sha256 "$RBTMP/db.gz")" = "$db_sha" ] || die "Контрольная сумма дампа не совпала"
  [ "$(backup_sha256 "$RBTMP/photos.tar.gz")" = "$photos_sha" ] \
    || die "Контрольная сумма архива фото не совпала"
  gunzip -t "$RBTMP/db.gz" || die "Дамп повреждён (не gzip)"
  tar tzf "$RBTMP/photos.tar.gz" >/dev/null || die "Архив фото нечитаем"
  # Документы появились позже фото: набор без них (старый формат) не отвергается.
  if [ -n "$documents_name" ]; then
    backup_name_valid "$documents_name" || die "Некорректное имя архива документов в наборе"
    backup_sha_valid "$documents_sha" || die "Некорректная контрольная сумма архива документов"
    rclone copyto "$BACKUP_REMOTE/documents/$documents_name" "$RBTMP/documents.tar.gz" 2>/dev/null \
      || die "Не найден архив документов набора $set"
    [ -s "$RBTMP/documents.tar.gz" ] || die "Архив документов пуст"
    [ "$(backup_sha256 "$RBTMP/documents.tar.gz")" = "$documents_sha" ] \
      || die "Контрольная сумма архива документов не совпала"
    tar tzf "$RBTMP/documents.tar.gz" >/dev/null || die "Архив документов нечитаем"
    RECOVERY_DOCUMENTS=1
  fi
}

rollback_photos_volume() {
  local backend_id volume
  backend_id="$(menu_compose ps -q -a backend)"
  [ -n "$backend_id" ] || die "Контейнер backend не найден"
  volume="$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/app/photos"}}{{.Name}}{{end}}{{end}}' "$backend_id")"
  [ -n "$volume" ] || die "Не найден volume с фото"
  printf '%s' "$volume"
}

rollback_documents_volume() {
  local backend_id volume
  backend_id="$(menu_compose ps -q -a backend)"
  [ -n "$backend_id" ] || die "Контейнер backend не найден"
  volume="$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/app/documents"}}{{.Name}}{{end}}{{end}}' "$backend_id")"
  [ -n "$volume" ] || die "Не найден volume с документами"
  printf '%s' "$volume"
}

rollback_restore_database() {
  log "Остановка записи и восстановление БД $POSTGRES_DB"
  menu_compose exec -T db psql -v ON_ERROR_STOP=1 -q \
    -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
    -c 'DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;' >/dev/null
  if ! gunzip -c "$RBTMP/db.gz" | menu_compose exec -T db \
      psql -v ON_ERROR_STOP=1 --single-transaction -q \
      -U "$POSTGRES_USER" -d "$POSTGRES_DB"; then
    die "Восстановление БД завершилось ошибкой"
  fi
}

rollback_restore_photos() {
  local volume
  volume="$(rollback_photos_volume)"
  log "Восстановление фото в volume $volume"
  docker run --rm --memory "$ROLLBACK_MEMORY_LIMIT" \
    -v "$volume":/data -v "$RBTMP":/restore:ro alpine:3.20 \
    sh -c 'find /data -mindepth 1 -delete 2>/dev/null; tar xzf /restore/photos.tar.gz -C /data'
}

rollback_restore_documents() {
  local volume
  volume="$(rollback_documents_volume)"
  log "Восстановление PDF-документов в volume $volume"
  docker run --rm --memory "$ROLLBACK_MEMORY_LIMIT" \
    -v "$volume":/data -v "$RBTMP":/restore:ro alpine:3.20 \
    sh -c 'find /data -mindepth 1 -delete 2>/dev/null; tar xzf /restore/documents.tar.gz -C /data'
}

rollback_verify_access() {
  local tries=0 body status
  log "Проверка пользовательского доступа"
  while [ "$tries" -lt "${ROLLBACK_READY_TIMEOUT_SECONDS:-180}" ]; do
    if menu_compose exec -T caddy wget -q -T 2 -O - http://backend:8080/ready 2>/dev/null \
        | grep -q '"status":"ready"'; then
      break
    fi
    tries=$((tries + 1))
    sleep 1
  done
  body="$(menu_compose exec -T caddy wget -q -T 2 -O - http://backend:8080/ready 2>/dev/null || true)"
  printf '%s' "$body" | grep -q '"status":"ready"' \
    || die "readiness не подтвердилась после recovery"
  status="$(menu_compose exec -T caddy wget -q -T 2 -S -O /dev/null \
      http://backend:8080/api/recipes 2>&1 \
      | grep -oE 'HTTP/[0-9.]+ [0-9]{3}' | tail -n1 | grep -oE '[0-9]{3}$' || true)"
  [ "$status" = "401" ] \
    || die "Пользовательский маршрут /api/recipes недоступен (код ${status:-нет})"
}

rollback_download_verify "$RECOVERY_SET"

mark_stage stopping
log "Остановка записей: backend"
menu_compose stop backend >/dev/null

mark_stage restoring
rollback_restore_database
rollback_restore_photos
if [ "$RECOVERY_DOCUMENTS" -eq 1 ]; then
  rollback_restore_documents
fi

mark_stage switching
switch_release

mark_stage verifying
rollback_verify_access

ELAPSED=$(( $(date +%s) - STARTED_AT ))
STATE="${BACKUP_STATE_FILE-$APP_DIR/backup-state}"
backup_state_set "$STATE" last_rollback_to "$TARGET"
backup_state_set "$STATE" last_rollback_from "$CURRENT_TAG"
backup_state_set "$STATE" last_rollback_set "$RECOVERY_SET"
backup_state_set "$STATE" last_rollback_at "$(backup_utc_now)"
backup_state_set "$STATE" last_rollback_seconds "$ELAPSED"
rm -f "$APP_DIR/recovery-point" "$APP_DIR/rollback-state"
log "Готово: recovery из $RECOVERY_SET, release $TARGET, время ${ELAPSED}s"
