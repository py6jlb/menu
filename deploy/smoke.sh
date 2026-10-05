#!/usr/bin/env bash
set -euo pipefail

# Smoke на стенде: выбранный релиз запущен, его образы совпадают с release.json,
# живой путь отвечает, отсутствующий статический asset даёт 404, SPA-маршрут работает.
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

image_digest() {
  docker image inspect --format '{{index .RepoDigests 0}}' "$1" 2>/dev/null || true
}

log "Идентичность manifest ($TAG)"
backend_expected="$(release_manifest_field "$MANIFEST" backendDigest)"
frontend_expected="$(release_manifest_field "$MANIFEST" frontendDigest)"
backend_actual="$(image_digest "$DOCKERHUB_USER/menu-backend:$TAG")"
frontend_actual="$(image_digest "$DOCKERHUB_USER/menu-frontend:$TAG")"
[ -n "$backend_actual" ] || die "Образ backend не найден локально"
[ -n "$frontend_actual" ] || die "Образ frontend не найден локально"
[ "$backend_actual" = "$backend_expected" ] || die "Дайджест backend не совпал с manifest"
[ "$frontend_actual" = "$frontend_expected" ] || die "Дайджест frontend не совпал с manifest"
log "Образы совпадают с manifest"

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
