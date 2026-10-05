#!/usr/bin/env bash
set -euo pipefail

# Серверная часть деплоя. Секреты остаются на сервере.
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
TAG="${1-}"
if [ -z "$TAG" ]; then
  printf 'Использование: remote-deploy.sh <git-sha>\n' >&2
  exit 1
fi
config_image_tag "$TAG"
export IMAGE_TAG="$TAG"
config_server
menu_compose pull
menu_compose up -d --remove-orphans

echo "Проверка /health..."
for _ in $(seq 1 30); do
  if menu_compose exec -T caddy \
      wget -qO- http://backend:8080/health 2>/dev/null | grep -q '"status":"ok"'; then
    echo "health ok"
    exit 0
  fi
  sleep 2
done
echo "health не поднялся за 60 сек" >&2
exit 1
