#!/usr/bin/env bash
# Доверенная библиотека состояния релиза, recovery-точки и сериализации
# деплоя/отката (тикет 41).
#
# Файлы состояния — данные KEY=value, не shell-код. Блокировка (flock) не даёт
# параллельным деплоям и откатам одновременно менять состояние и контейнеры.

# deploy_state_get <file> <key>: значение или пусто.
deploy_state_get() {
  local file="$1" key="$2"
  [ -f "$file" ] || return 0
  awk -v key="$key" 'index($0, key "=") == 1 { print substr($0, length(key) + 2); exit }' "$file"
}

# deploy_state_set <file> <key> <value>: атомарно обновить/добавить KEY=value.
deploy_state_set() {
  local file="$1" key="$2" value="$3" tmp
  mkdir -p "$(dirname "$file")"
  tmp="$(mktemp "${file}.XXXXXX")"
  if [ -f "$file" ]; then
    grep -v "^${key}=" "$file" > "$tmp" || true
  fi
  printf '%s=%s\n' "$key" "$value" >> "$tmp"
  mv "$tmp" "$file"
}

# deploy_lock_acquire [<file>] [<timeout>]: эксклюзивный lock операции.
# fd 9 удерживается до конца процесса; параллельная операция не начинается и
# получает понятную ошибку вместо гонки.
deploy_lock_acquire() {
  local app_dir="${APP_DIR:-.}"
  local file="${1-${DEPLOY_LOCK_FILE:-$app_dir/deploy.lock}}"
  local timeout="${2-${DEPLOY_LOCK_TIMEOUT:-600}}"
  if ! command -v flock >/dev/null 2>&1; then
    printf 'Деплой: flock недоступен — нельзя обеспечить сериализацию операций\n' >&2
    return 1
  fi
  exec 9>"$file" || return 1
  if ! flock -w "$timeout" 9; then
    printf 'Деплой: операция уже выполняется (%s занят), подожди завершения\n' \
      "$file" >&2
    return 1
  fi
}

# deploy_recovery_point_write <file> <set> <forRelease> <fromRelease> <schema> <createdAt>
# Точка привязана к обновлению forRelease: пока оно текущее, откат обязан быть
# recovery, а не простой заменой образа.
deploy_recovery_point_write() {
  local file="$1"
  mkdir -p "$(dirname "$file")"
  {
    printf 'SET=%s\n' "$2"
    printf 'FOR_RELEASE=%s\n' "$3"
    printf 'FROM_RELEASE=%s\n' "$4"
    printf 'SCHEMA=%s\n' "$5"
    printf 'CREATED_AT=%s\n' "$6"
  } > "$file"
}

# deploy_recovery_set_for <file> <release>: id набора, если recovery-точка
# привязана именно к этому релизу; иначе пусто.
deploy_recovery_set_for() {
  local file="$1" release="$2"
  [ -f "$file" ] || return 0
  [ "$(deploy_state_get "$file" FOR_RELEASE)" = "$release" ] || return 0
  deploy_state_get "$file" SET
}
