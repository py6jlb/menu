#!/usr/bin/env bash
set -euo pipefail

# Сквозной browser smoke: поднимает dev-стек (lab, письма в журнал), проходит
# регистрацию и подтверждение почты в браузере, создаёт семью/рецепт, план и
# список покупок, проверяет анонимный просмотр ссылки и сломанную ссылку.
# Коды берутся из журнала backend управляемо (без случайных задержек).
#
# Порты наружу не публикуются: конфликты с уже запущенным dev-стеком не мешают,
# готовность проверяется на внутренней сети compose.
#
#   scripts/browser-smoke.sh
#
# Переменные:
#   GATE_DOCKER             команда docker (по умолчанию docker)
#   PLAYWRIGHT_IMAGE        образ с браузерами (по умолчанию закреплённый)
#   PLAYWRIGHT_VERSION      версия npm-пакета playwright (совпадает с образом)
#   SMOKE_READY_TIMEOUT_SECONDS  предел ожидания готовности (по умолчанию 180)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOCKER="${GATE_DOCKER:-docker}"
PLAYWRIGHT_IMAGE="${PLAYWRIGHT_IMAGE:-mcr.microsoft.com/playwright:v1.49.1-noble}"
PLAYWRIGHT_VERSION="${PLAYWRIGHT_VERSION:-1.49.1}"
READY_TIMEOUT="${SMOKE_READY_TIMEOUT_SECONDS:-180}"
PROJECT="menu-gate-smoke-$$"
SHARED="$(mktemp -d)"
OVERRIDE="$(mktemp)"
WATCHER=""

log()  { printf '\033[1;32m[browser-smoke]\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m[browser-smoke]\033[0m %s\n' "$*" >&2; exit 1; }

# Dev-Compose публикует порты на loopback; для smoke они не нужны и мешают уже
# запущенному стеку, поэтому !reset очищает публикацию (Compose v2.24+).
cat > "$OVERRIDE" <<'YAML'
services:
  db:
    ports: !reset []
  backend:
    ports: !reset []
  frontend:
    ports: !reset []
YAML

COMPOSE=("$DOCKER" compose -p "$PROJECT" -f "$ROOT/docker-compose.yml" -f "$OVERRIDE")

cleanup() {
  [ -n "$WATCHER" ] && kill "$WATCHER" >/dev/null 2>&1 || true
  "${COMPOSE[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$SHARED" "$OVERRIDE"
}
trap cleanup EXIT

# Уникальные, но контролируемые учётные данные smoke (не секрет пользователя).
EMAIL="gate-smoke-$$@example.com"
PASSWORD="Gate-smoke-123456"

log "Поднимаю dev-стек ($PROJECT)"
DEPLOYMENT_MODE=lab EMAIL_TRANSPORT=log "${COMPOSE[@]}" up -d --build

wait_ready() {
  local i
  for ((i = 0; i < READY_TIMEOUT; i += 2)); do
    if "${COMPOSE[@]}" exec -T backend curl -fsS -o /dev/null http://127.0.0.1:8080/ready \
      >/dev/null 2>&1; then
      log "backend готов"
      return 0
    fi
    sleep 2
  done
  printf '[browser-smoke] Диагностика backend:\n' >&2
  "${COMPOSE[@]}" logs --tail 80 backend >&2 || true
  fail "backend не готов за ${READY_TIMEOUT}s"
}

wait_ready

log "Захват кодов подтверждения из журнала backend"
"${COMPOSE[@]}" logs -f --no-log-prefix backend 2>/dev/null \
  | grep --line-buffered -oE 'class="code">[0-9]{6}' \
  | grep --line-buffered -oE '[0-9]{6}' >> "$SHARED/codes.txt" &
WATCHER=$!

log "Запуск Playwright-сценария"
if ! "$DOCKER" run --rm --network "${PROJECT}_default" \
  -e FRONTEND_URL="http://frontend" \
  -e CODES_FILE="/shared/codes.txt" \
  -e SMOKE_EMAIL="$EMAIL" \
  -e SMOKE_PASSWORD="$PASSWORD" \
  -e PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 \
  -v "$ROOT/scripts/browser-smoke":/smoke:ro \
  -v "$SHARED":/shared \
  "$PLAYWRIGHT_IMAGE" \
  sh -c "mkdir -p /tmp/smoke && cp /smoke/flow.mjs /tmp/smoke/ && cd /tmp/smoke \
    && npm init -y >/dev/null 2>&1 \
    && npm install --no-save --no-audit playwright@${PLAYWRIGHT_VERSION} >/dev/null 2>&1 \
    && node flow.mjs"; then
  printf '[browser-smoke] Диагностика backend:\n' >&2
  "${COMPOSE[@]}" logs --tail 80 backend >&2 || true
  fail "Playwright-сценарий провалился"
fi

log "Browser smoke пройден"
