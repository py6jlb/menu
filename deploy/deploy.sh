#!/usr/bin/env bash
set -euo pipefail

# Деплой на VPS: доставка compose/Caddyfile и рестарт на прибитом теге.
#
#   ./deploy/deploy.sh <git-sha>
#   ./deploy/deploy.sh <предыдущий-sha>   # откат
#
# Переменные (env или deploy/.env):
#   VPS_HOST       адрес сервера (обязателен)
#   VPS_USER       пользователь SSH (menu)
#   VPS_SSH_PORT   порт SSH (8822)
#   APP_DIR        каталог на сервере (/opt/menu)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
[ -f deploy/.env ] && set -a && . deploy/.env && set +a

TAG="${1:-}"
if [ -z "$TAG" ]; then
  echo "Использование: $0 <git-sha>" >&2
  exit 1
fi
case "$TAG" in
  *[!A-Za-z0-9._-]*) echo "Некорректный тег: $TAG" >&2; exit 1 ;;
esac

VPS_HOST="${VPS_HOST:?Задай VPS_HOST (deploy/.env или env)}"
VPS_USER="${VPS_USER:-menu}"
VPS_SSH_PORT="${VPS_SSH_PORT:-8822}"
APP_DIR="${APP_DIR:-/opt/menu}"

TARGET="$VPS_USER@$VPS_HOST"

log() { printf '\033[1;32m[deploy]\033[0m %s\n' "$*"; }

log "Доставка конфигов на $TARGET:$APP_DIR"
scp -P "$VPS_SSH_PORT" docker-compose.prod.yml "$TARGET:$APP_DIR/docker-compose.prod.yml"
scp -P "$VPS_SSH_PORT" deploy/Caddyfile "$TARGET:$APP_DIR/Caddyfile"
scp -P "$VPS_SSH_PORT" deploy/otel-collector.yaml "$TARGET:$APP_DIR/otel-collector.yaml"

log "Рестарт на теге $TAG"
ssh -p "$VPS_SSH_PORT" "$TARGET" bash -s -- "$APP_DIR" "$TAG" <<'REMOTE'
set -euo pipefail
APP_DIR="$1"
TAG="$2"
cd "$APP_DIR"

if [ ! -f .env ]; then
  echo "Нет $APP_DIR/.env — заполни его по deploy/.env.example" >&2
  exit 1
fi

export IMAGE_TAG="$TAG"
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d --remove-orphans

echo "Проверка /health..."
for _ in $(seq 1 30); do
  if docker compose -f docker-compose.prod.yml exec -T caddy \
      wget -qO- http://backend:8080/health 2>/dev/null | grep -q '"status":"ok"'; then
    echo "health ok"
    exit 0
  fi
  sleep 2
done

echo "health не поднялся за 60 сек" >&2
exit 1
REMOTE

log "Готово. Откат: ./deploy/deploy.sh <предыдущий-sha>"
