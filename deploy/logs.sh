#!/usr/bin/env bash
set -euo pipefail

# Поиск контрольной записи по trace-id в файле журналов otel-collector.
#
#   ./deploy/logs.sh <trace-id>
#
# Читает volume коллектора через одноразовый alpine: у distroless-образа
# коллектора нет shell. Trace-id — не секрет; содержимое строк уже минимизировано
# (share-токены заменены плейсхолдером).
trace_id="${1-}"
if [ -z "$trace_id" ]; then
  printf 'Использование: %s <trace-id>\n' "$0" >&2
  exit 2
fi
if [[ ! "$trace_id" =~ ^[A-Za-z0-9._-]{1,128}$ ]]; then
  printf 'Журналы: некорректный trace-id (ожидается безопасный алфавит)\n' >&2
  exit 2
fi

volume="${OTEL_LOGS_VOLUME-menu-planner_otel_logs}"
file="${OTEL_LOGS_FILE-/logs/logs.json}"

if ! docker volume inspect "$volume" >/dev/null 2>&1; then
  printf 'Журналы: volume %s не найден — стек не запущен?\n' "$volume" >&2
  exit 1
fi

if ! docker run --rm -v "$volume":/logs:ro alpine:3.20 \
    grep -F -- "$trace_id" "$file"; then
  printf 'Журналы: запись с trace-id %s не найдена в %s\n' "$trace_id" "$file" >&2
  exit 1
fi
