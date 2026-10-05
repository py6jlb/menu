#!/usr/bin/env bash
set -euo pipefail

# Серверный Compose: единое чтение literal-конфигурации, без автозагрузки .env.
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
config_server
menu_compose "$@"
