#!/usr/bin/env bash
set -euo pipefail

# Серверная часть деплоя. Секреты остаются на сервере.
#   remote-deploy.sh <git-sha> [<commit>] [--schema-change]
# Тег обязателен; повторный штатный запуск того же релиза — ./deploy/compose.sh up -d
# (он читает pinned-тег из /opt/menu/current-release).
#
# --schema-change объявляет рискованное изменение схемы: перед переключением
# снимается свежая согласованная recovery-точка (complete-набор) и привязывается
# к обновлению. Такой откат идёт только через rollback.sh (recovery), а не
# простой заменой образа.
#
# Параллельные деплои/откаты сериализуются lock-файлом $APP_DIR/deploy.lock.
# Прерванная операция оставляет current/previous release и deploy-intent для
# диагностики.
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

TAG="${1-}"
COMMIT="${2-}"
MODE="${3-}"
if [ -z "$TAG" ]; then
  printf 'Использование: remote-deploy.sh <git-sha> [<commit>] [--schema-change]\n' >&2
  exit 1
fi
SCHEMA_CHANGE=0
case "$MODE" in
  "")              ;;
  --schema-change) SCHEMA_CHANGE=1 ;;
  *) printf 'Неизвестный режим: %s\n' "$MODE" >&2; exit 1 ;;
esac
config_image_tag "$TAG"
export IMAGE_TAG="$TAG"
config_server

if [ "${DEPLOY_LOCK_HELD:-0}" != "1" ]; then
  deploy_lock_acquire || exit 1
fi

READY_TIMEOUT_SECONDS="${READY_TIMEOUT_SECONDS:-180}"
if [[ ! "$READY_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]]; then
  printf 'READY_TIMEOUT_SECONDS должен быть целым > 0\n' >&2
  exit 1
fi

diagnose() {
  diagnose_to "$APP_DIR/deploy-diagnostics.log" "Deploy readiness failure"
}

# Предыдущий успешный релиз до переключения — цель отката по умолчанию.
PREV_TAG="$(deploy_state_get "$APP_DIR/current-release" IMAGE_TAG)"
[ "$PREV_TAG" != "$TAG" ] || PREV_TAG=""
RECOVERY_SET=""

write_release_state() {
  local backend_digest frontend_digest hashes built_at
  backend_digest="$(release_image_digest "$DOCKERHUB_USER/menu-backend:$TAG")"
  frontend_digest="$(release_image_digest "$DOCKERHUB_USER/menu-frontend:$TAG")"

  built_at=""
  if [ -f "$APP_DIR/deploy/release.json" ]; then
    built_at="$(release_manifest_field "$APP_DIR/deploy/release.json" builtAt)"
  fi
  [ -n "$built_at" ] || built_at="$(release_built_at)"
  if [ -z "$COMMIT" ] && [ -f "$APP_DIR/deploy/release.json" ]; then
    COMMIT="$(release_manifest_field "$APP_DIR/deploy/release.json" commit)"
  fi
  [ -n "$COMMIT" ] || COMMIT="unknown"

  hashes="$(mktemp)"
  release_config_hashes "$APP_DIR" > "$hashes"
  release_manifest "$APP_DIR/release.json" "$COMMIT" "$TAG" "$built_at" \
    "$backend_digest" "$frontend_digest" "$hashes" "$RECOVERY_SET"
  rm -f "$hashes"

  printf 'IMAGE_TAG=%s\n' "$TAG" > "$APP_DIR/current-release.tmp"
  mv "$APP_DIR/current-release.tmp" "$APP_DIR/current-release"
}

# Свежая согласованная recovery-точка до рискованного обновления. backup.sh
# останавливает backend, поэтому набор относится к одной точке, и публикует
# complete-маркер. Набор снимается под релизом, который сейчас работает.
create_recovery_point() {
  local state="${BACKUP_STATE_FILE-$APP_DIR/backup-state}"
  local running="${PREV_TAG:-latest}" set schema created
  printf '\033[1;32m[deploy]\033[0m Рискованное изменение схемы: recovery-точка\n'
  IMAGE_TAG="$running" bash "$APP_DIR/deploy/backup.sh"
  set="$(deploy_state_get "$state" last_full_backup)"
  schema="$(deploy_state_get "$state" last_full_backup_schema)"
  created="$(deploy_state_get "$state" last_full_backup_at)"
  if [ -z "$set" ] || ! backup_set_valid "$set"; then
    printf 'Деплой: не удалось получить backup ID recovery-точки\n' >&2
    exit 1
  fi
  deploy_recovery_point_write "$APP_DIR/recovery-point" \
    "$set" "$TAG" "$PREV_TAG" "$schema" "$created"
  RECOVERY_SET="$set"
  printf '\033[1;32m[deploy]\033[0m Recovery-точка %s привязана к %s\n' "$set" "$TAG"
}

# Маркер намерения: если процесс прервётся, файл остаётся для разбора.
deploy_state_set "$APP_DIR/deploy-intent" MODE deploy
deploy_state_set "$APP_DIR/deploy-intent" TAG "$TAG"
deploy_state_set "$APP_DIR/deploy-intent" COMMIT "$COMMIT"
deploy_state_set "$APP_DIR/deploy-intent" STARTED_AT "$(release_built_at)"

if [ "$SCHEMA_CHANGE" -eq 1 ]; then
  create_recovery_point
fi

menu_compose pull
menu_compose up -d --remove-orphans

echo "Проверка /ready (до ${READY_TIMEOUT_SECONDS}s, с учётом миграций)..."
ready=0
for _ in $(seq 1 "$READY_TIMEOUT_SECONDS"); do
  if menu_compose exec -T caddy \
      wget -q -T 2 -O - http://backend:8080/ready 2>/dev/null \
      | grep -q '"status":"ready"'; then
    ready=1
    break
  fi
  sleep 1
done

if [ "$ready" -ne 1 ]; then
  diagnose
  echo "readiness не поднялась за ${READY_TIMEOUT_SECONDS} сек" >&2
  exit 1
fi

echo "ready ok"
write_release_state
# Предыдущий успешный релиз — цель отката; при первом деплое его ещё нет.
if [ -n "$PREV_TAG" ]; then
  printf 'IMAGE_TAG=%s\n' "$PREV_TAG" > "$APP_DIR/previous-release.tmp"
  mv "$APP_DIR/previous-release.tmp" "$APP_DIR/previous-release"
else
  rm -f "$APP_DIR/previous-release"
fi
rm -f "$APP_DIR/deploy-intent"
echo "release manifest: $APP_DIR/release.json"
exit 0
