#!/usr/bin/env bash
set -euo pipefail

# Критический PostgreSQL-suite рядом с быстрыми тестами.
#
#   scripts/test-postgres.sh
#
# Поднимает одноразовую Postgres 16 в отдельной Docker-сети (без публикации
# порта, без volume) и запускает только PostgreSQL-проверки в SDK-контейнере.
# На хосте .NET SDK не нужен. Dev-БД и рабочие данные не затрагиваются.
#
# Переменные:
#   POSTGRES_TEST_IMAGE  образ сервера (по умолчанию postgres:16)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${POSTGRES_TEST_IMAGE:-postgres:16}"
SUFFIX="$$"
NETWORK="menu-planner-pgtest-${SUFFIX}"
CONTAINER="menu-planner-pgtest-${SUFFIX}"
DB_NAME="menu_planner_pgt"
DB_USER="pgtester"
DB_PASSWORD="pgtester-secret"

log() { printf '\033[1;32m[pgtest]\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m[pgtest]\033[0m %s\n' "$*" >&2; exit 1; }

cleanup() {
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  docker network rm "$NETWORK" >/dev/null 2>&1 || true
}
trap cleanup EXIT

log "Одноразовый Postgres ($IMAGE) в изолированной сети"
docker network create "$NETWORK" >/dev/null
docker run -d --name "$CONTAINER" --network "$NETWORK" \
  -e POSTGRES_DB="$DB_NAME" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  "$IMAGE" >/dev/null

log "Ожидание готовности сервера"
ready=""
for _ in $(seq 1 60); do
  if docker exec "$CONTAINER" pg_isready -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 1
done
if [ -z "$ready" ]; then
  docker logs "$CONTAINER" >&2 || true
  fail "Postgres не стал готов за 60 секунд"
fi

log "PostgreSQL-suite (SDK-контейнер, без SDK на хосте)"
docker run --rm \
  --network "$NETWORK" \
  -u "$(id -u):$(id -g)" \
  -e DOTNET_CLI_HOME=/tmp/dotnet-home \
  -e NUGET_PACKAGES=/tmp/nuget \
  -e MENU_PLANNER_TEST_POSTGRES="Host=$CONTAINER;Port=5432;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD" \
  -v "$ROOT":/app -w /app \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test --filter "FullyQualifiedName~MenuPlanner.Api.Tests.Postgres"

log "Готово"
