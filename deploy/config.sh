#!/usr/bin/env bash
# Доверенная библиотека. Файлы настроек — данные KEY=value, не shell-код.

config_load() {
  local file="$1" scope="$2" line key value number=0 allowed seen=' '
  local LC_ALL=C
  case "$scope" in
    local) allowed=' DOCKERHUB_USER IMAGE_TAG VPS_HOST VPS_USER VPS_SSH_PORT APP_DIR ' ;;
    server) allowed=' DOCKERHUB_USER IMAGE_TAG POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD DEPLOYMENT_MODE PUBLIC_BASE_URL JWT_SECRET JWT_ISSUER JWT_AUDIENCE SMTP_HOST SMTP_PORT SMTP_USER SMTP_PASSWORD SMTP_FROM SMTP_FROM_NAME SMTP_ENABLE_STARTTLS DOMAIN SHARE_BASE_URL BACKUP_REMOTE BACKUP_KEEP_DAILY BACKUP_KEEP_WEEKLY COMPOSE_FILE DRILL_CONTAINER ' ;;
    *) printf 'Конфигурация: неизвестная область\n' >&2; return 1 ;;
  esac
  [ -e "$file" ] || return 0
  [ -f "$file" ] && [ -r "$file" ] || {
    printf 'Конфигурация: файл недоступен для чтения\n' >&2; return 1;
  }
  # Bash read иначе молча выбрасывает NUL, меняя секрет.
  if IFS= read -r -d '' line < "$file"; then
    printf 'Конфигурация: недопустимый NUL\n' >&2
    return 1
  fi
  while IFS= read -r line || [ -n "$line" ]; do
    number=$((number + 1))
    if [[ "$line" == *[[:cntrl:]]* ]]; then
      printf 'Конфигурация: строка %s, управляющий символ\n' "$number" >&2
      return 1
    fi
    [[ -z "$line" || "$line" == \#* ]] && continue
    if [[ "$line" != *=* ]]; then
      printf 'Конфигурация: строка %s, ожидается KEY=value\n' "$number" >&2
      return 1
    fi
    key="${line%%=*}"
    value="${line#*=}"
    if [[ ! "$key" =~ ^[A-Z][A-Z0-9_]*$ || "$allowed" != *" $key "* ]]; then
      printf 'Конфигурация: строка %s, недопустимый ключ\n' "$number" >&2
      return 1
    fi
    if [[ "$seen" == *" $key "* ]]; then
      printf 'Конфигурация: строка %s, повтор ключа\n' "$number" >&2
      return 1
    fi
    seen+="$key "
    if [[ ! -v "$key" ]]; then
      printf -v "$key" '%s' "$value"
    fi
    export "${key?}"
  done < "$file"
}

config_require() {
  local key
  for key in "$@"; do
    if [ -z "${!key-}" ]; then
      printf 'Конфигурация: задай обязательный %s (окружение или файл настроек)\n' "$key" >&2
      return 1
    fi
    export "${key?}"
  done
}

config_server() {
  config_load "$APP_DIR/server.conf" server || return
  # Состояние выбранного релиза, записанное remote-deploy. Ниже приоритета
  # server.conf и окружения: повторный штатный Compose берёт тот же тег.
  config_load "$APP_DIR/current-release" server || return
  config_require DOCKERHUB_USER POSTGRES_PASSWORD JWT_SECRET || return
  config_image_tag "${IMAGE_TAG-latest}"
}

config_image_tag() {
  if [[ ! "$1" =~ ^[A-Za-z0-9_][A-Za-z0-9._-]{0,127}$ ]]; then
    printf 'Конфигурация: некорректный IMAGE_TAG (Docker tag, 1–128 символов)\n' >&2
    return 1
  fi
}

# Только уже загруженное окружение: Compose не читает .env или COMPOSE_ENV_FILES.
menu_compose() {
  # Совместимость с backend до тикета 36: ADO.NET quoted-string grammar.
  # Все строковые поля в двойных кавычках, literal " удваивается; остальные
  # символы сохраняются. Значения не исполняются, SDK на сервере не нужен.
  local database="${POSTGRES_DB-menu_planner}" user="${POSTGRES_USER-menu}"
  local password="${POSTGRES_PASSWORD-}" legacy
  database="${database//\"/\"\"}"
  user="${user//\"/\"\"}"
  password="${password//\"/\"\"}"
  legacy="Host=\"db\";Port=5432;Database=\"$database\";Username=\"$user\";Password=\"$password\""
  # Derived-переменная только для этого вызова Compose; входящий legacy env
  # не может рассогласовать её с literal POSTGRES_* для rollback.
  ConnectionStrings__Default="$legacy" COMPOSE_DISABLE_ENV_FILE=true COMPOSE_ENV_FILES='' docker compose \
    --env-file /dev/null -f "${COMPOSE_FILE-docker-compose.prod.yml}" "$@"
}

# diagnose_to <log-file> <header>: сохранить состояние контейнеров и последние
# логи Compose для разбора провала деплоя или smoke. Секретов не печатает.
diagnose_to() {
  local log="$1" header="$2"
  {
    printf '%s at %s\n' "$header" "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    menu_compose ps 2>&1 || true
    menu_compose logs --no-color --tail=200 2>&1 || true
  } > "$log" 2>&1 || true
  printf 'Диагностика: %s\n' "$log" >&2
}
