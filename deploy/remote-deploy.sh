#!/usr/bin/env bash
set -euo pipefail

# Серверная часть деплоя. Секреты остаются на сервере.
#   remote-deploy.sh <git-sha> [<commit>]
# Тег обязателен; повторный штатный запуск того же релиза — ./deploy/compose.sh up -d
# (он читает pinned-тег из /opt/menu/current-release).
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/release.sh"
TAG="${1-}"
COMMIT="${2-}"
if [ -z "$TAG" ]; then
  printf 'Использование: remote-deploy.sh <git-sha> [<commit>]\n' >&2
  exit 1
fi
config_image_tag "$TAG"
export IMAGE_TAG="$TAG"
config_server

READY_TIMEOUT_SECONDS="${READY_TIMEOUT_SECONDS:-180}"
if [[ ! "$READY_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]]; then
  printf 'READY_TIMEOUT_SECONDS должен быть целым > 0\n' >&2
  exit 1
fi

diagnose() {
  diagnose_to "$APP_DIR/deploy-diagnostics.log" "Deploy readiness failure"
}

write_release_state() {
  local backend_digest frontend_digest hashes built_at
  backend_digest="$(release_image_digest "$DOCKERHUB_USER/menu-backend:$TAG")"
  frontend_digest="$(release_image_digest "$DOCKERHUB_USER/menu-frontend:$TAG")"

  built_at=""
  if [ -f "$APP_DIR/deploy/release.json" ]; then
    built_at="$(release_manifest_field "$APP_DIR/deploy/release.json" builtAt)"
  fi
  [ -n "$built_at" ] || built_at="$(release_built_at)"
  [ -n "$COMMIT" ] || COMMIT="$(release_manifest_field "$APP_DIR/deploy/release.json" commit)"

  hashes="$(mktemp)"
  release_config_hashes "$APP_DIR" > "$hashes"
  release_manifest "$APP_DIR/release.json" "$COMMIT" "$TAG" "$built_at" \
    "$backend_digest" "$frontend_digest" "$hashes"
  rm -f "$hashes"

  printf 'IMAGE_TAG=%s\n' "$TAG" > "$APP_DIR/current-release.tmp"
  mv "$APP_DIR/current-release.tmp" "$APP_DIR/current-release"
}

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
echo "release manifest: $APP_DIR/release.json"
exit 0
