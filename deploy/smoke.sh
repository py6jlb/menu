#!/usr/bin/env bash
set -euo pipefail

# Smoke на стенде: выбранный релиз запущен, его образы и тег совпадают с release.json,
# повторный Compose берёт pinned-тег, а публичный путь проверяется через край (Caddy),
# а не внутренним backend-запросом: readiness, SPA-страница, существующий asset и
# безопасный запрос к /api. В доменном режиме используется внешний HTTPS.
#
#   /opt/menu/deploy/smoke.sh
#
# Переменные окружения:
#   SMOKE_HTTP_TIMEOUT_SECONDS  таймаут одного HTTP-запроса (default 10)

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/release.sh"
config_server

# Выбранный (прибитый) тег: current-release/server.conf задают IMAGE_TAG.
TAG="${IMAGE_TAG:-latest}"

log() { printf '\033[1;32m[smoke]\033[0m %s\n' "$*"; }

# При провале сохраняем состояния контейнеров и логи — их достаточно для диагностики.
diagnose() { diagnose_to "$APP_DIR/smoke-diagnostics.log" "Smoke failure"; }

die() { printf '[smoke] %s\n' "$*" >&2; diagnose; exit 1; }

SMOKE_HTTP_TIMEOUT_SECONDS="${SMOKE_HTTP_TIMEOUT_SECONDS:-10}"
[[ "$SMOKE_HTTP_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]] \
  || die "SMOKE_HTTP_TIMEOUT_SECONDS должен быть целым > 0"

# ":80" или пусто — Caddy слушает HTTP локально; реальный домен — внешний HTTPS
# с сертификатом Let's Encrypt, то есть проверяется именно публичный путь.
DOMAIN="${DOMAIN-}"
case "$DOMAIN" in
  ""|:*) EDGE_BASE="http://localhost" ;;
  *)
    [[ "$DOMAIN" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ]] || die "Некорректный DOMAIN"
    EDGE_BASE="https://$DOMAIN"
    ;;
esac

MANIFEST="$APP_DIR/release.json"
[ -f "$MANIFEST" ] || die "Нет $MANIFEST — сначала штатный деплой"

manifest_tag="$(release_manifest_field "$MANIFEST" tag)"
[ "$manifest_tag" = "$TAG" ] || die "Manifest описывает тег $manifest_tag, выбран $TAG"

log "Идентичность manifest ($TAG)"
backend_expected="$(release_manifest_field "$MANIFEST" backendDigest)"
frontend_expected="$(release_manifest_field "$MANIFEST" frontendDigest)"
backend_actual="$(release_image_digest "$DOCKERHUB_USER/menu-backend:$TAG")"
frontend_actual="$(release_image_digest "$DOCKERHUB_USER/menu-frontend:$TAG")"
[ "$backend_actual" != "unknown" ] || die "Образ backend не найден локально"
[ "$frontend_actual" != "unknown" ] || die "Образ frontend не найден локально"
[ "$backend_actual" = "$backend_expected" ] || die "Дайджест backend не совпал с manifest"
[ "$frontend_actual" = "$frontend_expected" ] || die "Дайджест frontend не совпал с manifest"
log "Образы совпадают с manifest"

log "Повторный Compose берёт pinned-тег"
images="$(menu_compose config --images)"
for expected_image in "$DOCKERHUB_USER/menu-backend:$TAG" "$DOCKERHUB_USER/menu-frontend:$TAG"; do
  printf '%s\n' "$images" | grep -qx "$expected_image" \
    || die "Compose не использует $expected_image"
done
log "Compose прибит к $TAG"

# Единственный HTTP-клиент — caddy-контейнер, все запросы идут на его же край.
edge_fetch() {
  menu_compose exec -T caddy wget -q -T "$SMOKE_HTTP_TIMEOUT_SECONDS" "$@"
}

log "Проверка readiness через край ($EDGE_BASE)"
readiness_body="$(edge_fetch -O - "$EDGE_BASE/ready")" \
  || die "readiness недоступна через край"
printf '%s' "$readiness_body" | grep -q '"status":"ready"' \
  || die "readiness не подтвердила готовность: $readiness_body"

log "Проверка SPA-страницы через край"
spa_body="$(edge_fetch -O - "$EDGE_BASE/")" || die "SPA-страница не отвечает через край"
printf '%s' "$spa_body" | grep -qi '<!doctype html' || die "SPA-маршрут не отдаёт HTML"

asset_path="$(printf '%s' "$spa_body" \
  | grep -oE '/assets/[A-Za-z0-9._-]+\.(js|css)' | head -n1 || true)"
[ -n "$asset_path" ] || die "SPA-страница не ссылается на существующий asset"
edge_fetch -O /dev/null "$EDGE_BASE$asset_path" \
  || die "asset $asset_path не отдаётся через край"
log "SPA отвечает, asset $asset_path существует"

log "Безопасный запрос к /api через край"
api_status="$(edge_fetch -S -O /dev/null "$EDGE_BASE/api/recipes" 2>&1 \
  | grep -oE 'HTTP/[0-9.]+ [0-9]{3}' | tail -n1 | grep -oE '[0-9]{3}$' || true)"
[ "$api_status" = "401" ] \
  || die "Маршрут /api не достиг backend (код ${api_status:-нет})"
log "Маршрут /api достиг backend (401 без сессии)"

log "Smoke успешен ($TAG)"
