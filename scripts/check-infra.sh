#!/usr/bin/env bash
set -euo pipefail

# Проверка инфраструктурной конфигурации релиза: Compose (dev+prod), Caddyfile
# и otel-collector. Один `bash -n` конфигурацию не заменяет — здесь реально
# парсятся и валидируются теми же инструментами, что и в проде.
#
#   scripts/check-infra.sh
#
# Переменные:
#   GATE_DOCKER  команда docker (по умолчанию docker)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# shellcheck disable=SC1091
. "$ROOT/deploy/release.sh"

DOCKER="${GATE_DOCKER:-docker}"
CADDY_IMAGE="$RELEASE_CADDY_IMAGE"
COLLECTOR_IMAGE="$RELEASE_COLLECTOR_IMAGE"

log()  { printf '\033[1;32m[infra]\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m[infra]\033[0m %s\n' "$*" >&2; exit 1; }

if ! command -v "$DOCKER" >/dev/null 2>&1; then
  fail "не найдена команда «$DOCKER»"
fi

log "Compose dev: config --quiet"
"$DOCKER" compose -f "$ROOT/docker-compose.yml" config --quiet \
  || fail "docker-compose.yml не проходит config"

# Prod-Compose зависит от обязательных переменных окружения. Для проверки формы
# подставляются заведомо вымышленные значения; production-секреты не нужны.
log "Compose prod: config --quiet (вымышленное окружение)"
env \
  DOCKERHUB_USER=gate-check \
  IMAGE_TAG=gate-check \
  POSTGRES_DB=menu_planner \
  POSTGRES_USER=menu \
  POSTGRES_PASSWORD=gate-check-only \
  JWT_SECRET=gate-check-only-secret-0123456789abcdef \
  ConnectionStrings__Default="Host=db;Port=5432;Database=menu_planner;Username=menu;Password=gate-check-only" \
  DOMAIN=":80" \
  "$DOCKER" compose -f "$ROOT/docker-compose.prod.yml" config --quiet \
  || fail "docker-compose.prod.yml не проходит config"

log "Caddyfile: caddy validate"
"$DOCKER" run --rm -e DOMAIN=":80" \
  -v "$ROOT/deploy/Caddyfile":/etc/caddy/Caddyfile:ro \
  "$CADDY_IMAGE" caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile \
  || fail "deploy/Caddyfile не проходит caddy validate"

log "otel-collector: validate"
"$DOCKER" run --rm \
  -v "$ROOT/deploy/otel-collector.yaml":/etc/otelcol-contrib/config.yaml:ro \
  "$COLLECTOR_IMAGE" validate --config /etc/otelcol-contrib/config.yaml \
  || fail "deploy/otel-collector.yaml не проходит validate"

log "Инфраструктурная конфигурация валидна"
