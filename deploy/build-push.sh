#!/usr/bin/env bash
set -euo pipefail

# Сборка релиза. Публикация образов — только с явным --publish.
#
#   ./deploy/build-push.sh            # проверки + сборка образов + manifest
#   ./deploy/build-push.sh --publish  # то же и push в Docker Hub
#
# Переменные (env или deploy/local.conf):
#   DOCKERHUB_USER  логин Docker Hub (обязателен)
#   IMAGE_TAG       тег образа (по умолчанию короткий git-sha)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
. "$ROOT/deploy/config.sh"
# shellcheck disable=SC1091
. "$ROOT/deploy/release.sh"
config_load "$ROOT/deploy/local.conf" local
config_require DOCKERHUB_USER

PUBLISH=0
case "${1-}" in
  --publish) PUBLISH=1 ;;
  "")        ;;
  *)         printf 'Использование: %s [--publish]\n' "$0" >&2; exit 1 ;;
esac

log() { printf '\033[1;32m[build-push]\033[0m %s\n' "$*"; }

release_require_clean_tree "$ROOT"

TAG="${IMAGE_TAG-$(git rev-parse --short HEAD)}"
config_image_tag "$TAG"
COMMIT="$(release_resolve_commit "$ROOT" HEAD)"
BACKEND_IMAGE="$DOCKERHUB_USER/menu-backend"
FRONTEND_IMAGE="$DOCKERHUB_USER/menu-frontend"
RELEASE_DIR="$ROOT/deploy/release"
MANIFEST="$RELEASE_DIR/$TAG.json"

log "Гейт релиза: быстрые этапы (без browser)"
"$ROOT/scripts/release-gate.sh" --artifacts "$ROOT/deploy/gate-artifacts"

log "Сборка образов ($TAG)"
docker build -t "$BACKEND_IMAGE:$TAG" backend
docker build -t "$FRONTEND_IMAGE:$TAG" frontend

if [ "$PUBLISH" -eq 1 ]; then
  log "Гейт релиза: browser smoke (перед публикацией)"
  "$ROOT/scripts/release-gate.sh" --only browser --artifacts "$ROOT/deploy/gate-artifacts"

  log "Публикация в Docker Hub"
  docker tag "$BACKEND_IMAGE:$TAG" "$BACKEND_IMAGE:latest"
  docker tag "$FRONTEND_IMAGE:$TAG" "$FRONTEND_IMAGE:latest"
  docker push "$BACKEND_IMAGE:$TAG"
  docker push "$BACKEND_IMAGE:latest"
  docker push "$FRONTEND_IMAGE:$TAG"
  docker push "$FRONTEND_IMAGE:latest"
  BACKEND_DIGEST="$(release_image_digest "$BACKEND_IMAGE:$TAG")"
  FRONTEND_DIGEST="$(release_image_digest "$FRONTEND_IMAGE:$TAG")"
else
  BACKEND_DIGEST="$(release_image_digest "$BACKEND_IMAGE:$TAG" ".Id")"
  FRONTEND_DIGEST="$(release_image_digest "$FRONTEND_IMAGE:$TAG" ".Id")"
fi

HASHES="$(mktemp)"
trap 'rm -f "$HASHES"' EXIT
release_config_hashes "$ROOT" > "$HASHES"

mkdir -p "$RELEASE_DIR"
release_manifest "$MANIFEST" "$COMMIT" "$TAG" "$(release_built_at)" \
  "$BACKEND_DIGEST" "$FRONTEND_DIGEST" "$HASHES"

log "Manifest: $MANIFEST"
if [ "$PUBLISH" -eq 0 ]; then
  log "Образы не опубликованы (добавь --publish)"
fi
log "Готово: $TAG"
log "Деплой: ./deploy/build-push.sh --publish && ./deploy/deploy.sh $TAG"
