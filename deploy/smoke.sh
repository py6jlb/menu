#!/usr/bin/env bash
set -euo pipefail

# Smoke на стенде: выбранный релиз запущен, его образы и тег совпадают с release.json,
# повторный Compose берёт pinned-тег, живой путь отвечает, отсутствующий asset даёт 404,
# SPA-маршрут работает.
#
#   /opt/menu/deploy/smoke.sh

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/release.sh"
config_server

log() { printf '\033[1;32m[smoke]\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m[smoke]\033[0m %s\n' "$*" >&2; exit 1; }

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

log "Проверка живого пути"
menu_compose exec -T caddy wget -qO- http://backend:8080/health 2>/dev/null \
  | grep -q '"status":"ok"' || die "health недоступен"

menu_compose exec -T caddy wget -qO- http://frontend:80/ 2>/dev/null \
  | grep -qi '<!doctype html' || die "SPA-маршрут не отдаёт HTML"

if menu_compose exec -T caddy wget -qO- \
    "http://frontend:80/assets/__missing__-smoke.js" >/dev/null 2>&1; then
  die "Отсутствующий asset не вернул 404"
fi
log "SPA отвечает, отсутствующий asset — 404"
log "Smoke успешен ($TAG)"
