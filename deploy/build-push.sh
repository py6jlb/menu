#!/usr/bin/env bash
set -euo pipefail

# Сборка и публикация образов в Docker Hub.
#
#   ./deploy/build-push.sh
#
# Переменные (env или deploy/local.conf):
#   DOCKERHUB_USER  логин Docker Hub (обязателен)
#   IMAGE_TAG       тег образа (по умолчанию короткий git-sha)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
. "$ROOT/deploy/config.sh"
config_load "$ROOT/deploy/local.conf" local
config_require DOCKERHUB_USER

TAG="${IMAGE_TAG-$(git rev-parse --short HEAD)}"
config_image_tag "$TAG"
BACKEND_IMAGE="$DOCKERHUB_USER/menu-backend"
FRONTEND_IMAGE="$DOCKERHUB_USER/menu-frontend"

log() { printf '\033[1;32m[build-push]\033[0m %s\n' "$*"; }

log "Тесты backend (контейнер SDK, без root-артефактов)"
docker run --rm \
  -u "$(id -u):$(id -g)" \
  -e DOTNET_CLI_HOME=/tmp/dotnet-home \
  -e NUGET_PACKAGES=/tmp/nuget \
  -v "$ROOT":/app -w /app \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test

log "Проверка миграций EF Core (нет рассинхрона модели)"
docker run --rm \
  -u "$(id -u):$(id -g)" \
  -e DOTNET_CLI_HOME=/tmp/dotnet-home \
  -e NUGET_PACKAGES=/tmp/nuget \
  -v "$ROOT":/app -w /app \
  mcr.microsoft.com/dotnet/sdk:10.0 sh -c "dotnet tool restore && dotnet ef migrations has-pending-model-changes --project backend/src/MenuPlanner.Api --startup-project backend/src/MenuPlanner.Api"

log "Сборка frontend"
(cd frontend && npm run build)

log "Сборка образов ($TAG)"
docker build -t "$BACKEND_IMAGE:$TAG" -t "$BACKEND_IMAGE:latest" backend
docker build -t "$FRONTEND_IMAGE:$TAG" -t "$FRONTEND_IMAGE:latest" frontend

log "Публикация в Docker Hub"
docker push "$BACKEND_IMAGE:$TAG"
docker push "$BACKEND_IMAGE:latest"
docker push "$FRONTEND_IMAGE:$TAG"
docker push "$FRONTEND_IMAGE:latest"

log "Готово: $TAG"
log "Деплой: ./deploy/deploy.sh $TAG"
