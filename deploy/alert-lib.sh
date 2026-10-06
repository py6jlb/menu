#!/usr/bin/env bash
# Доверенная библиотека оповещений о недоступности, старых бэкапах и ресурсах
# (тикет 72). Модуль не печатает секреты: URL канала не логируется и не попадает
# в argv (curl читает его из временного --config), в сообщении и состоянии его
# нет. Значения из хранилища/файлов проверяются перед использованием.

# alert_url_valid <url>: http(s)-URL безопасного алфавита. Кавычки и обратный
# слэш исключены, чтобы literal-запись в curl --config оставалась однозначной.
alert_url_valid() {
  local re='^https?://[A-Za-z0-9._~:/?#@!$&()*+,;=%-]+$'
  [[ "$1" =~ $re ]]
}

# alert_url_host <url>: хост без пути и секретной части — для сообщения.
alert_url_host() {
  local rest="${1#*://}"
  rest="${rest%%/*}"
  printf '%s' "${rest%%\?*}"
}

# alert_set_epoch <id>: epoch-секунды времени complete-набора. Наносекунды
# отбрасываются, остаётся ISO-8601 в UTC.
alert_set_epoch() {
  local id="$1"
  backup_set_valid "$id" || return 1
  date -u -d "${id:0:10}T${id:11:2}:${id:13:2}:${id:15:2}Z" +%s
}

# alert_json_escape <text>: строка, безопасная внутри JSON-строки.
alert_json_escape() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  value="${value//$'\n'/ }"
  value="${value//$'\r'/}"
  printf '%s' "$value"
}

# alert_problem_hint <problem> <cause>: следующий диагностический шаг.
alert_problem_hint() {
  case "$1" in
    public_path) printf '%s' "проверь deploy/smoke.sh и журналы края/backend (deploy/logs.sh <trace-id>)" ;;
    backup)      printf '%s' "проверь systemctl status menu-backup.service, доступ к BACKUP_REMOTE (rclone) и запусти backup при необходимости" ;;
    disk)        printf '%s' "проверь df -h и освободи место: docker system prune, старые журналы/образы" ;;
    memory)      printf '%s' "проверь docker stats и лимиты памяти контейнеров" ;;
    load)        printf '%s' "проверь docker stats, нагрузку на CPU и systemctl status menu-backup.service" ;;
    *)           printf '%s' "проверь состояние установки и журналы" ;;
  esac
}

# alert_alert_message <install> <at> <release> <problem> <cause> <detail>
# Содержит время, установку/release, тип проблемы и следующий шаг, без
# пользовательских данных.
alert_alert_message() {
  local install="$1" at="$2" release="$3" problem="$4" cause="$5" detail="$6"
  printf '[%s] ALERT %s (%s): %s. time=%s release=%s next=%s\n' \
    "$install" "$problem" "$cause" "$detail" "$at" "$release" \
    "$(alert_problem_hint "$problem" "$cause")"
}

# alert_recovery_message <install> <at> <release> <problem> <cause>
alert_recovery_message() {
  local install="$1" at="$2" release="$3" problem="$4" cause="$5"
  printf '[%s] RECOVERED %s (%s). time=%s release=%s next=%s\n' \
    "$install" "$problem" "$cause" "$at" "$release" \
    "проблема устранена, действий не требуется"
}

# alert_test_message <install> <at> <release>
alert_test_message() {
  printf '[%s] TEST: проверочное уведомление канала. time=%s release=%s\n' "$1" "$2" "$3"
}

# alert_curl_url_config <file> <url>: literal-запись URL для curl --config, чтобы
# секрет не попадал в argv. URL уже проверен alert_url_valid (без " и \).
alert_curl_url_config() {
  printf 'url = "%s"\n' "$2" > "$1"
}

# alert_df_sample <path>: "свободно_МБ занято_%" одним вызовом df. Одна выборка —
# одна точка; не удалось прочитать — ошибка, а не выдуманное значение.
alert_df_sample() {
  local out
  out="$(df -Pm "$1" 2>/dev/null | awk 'NR==2 && $4 ~ /^[0-9]+$/ && $5 ~ /^[0-9]+%$/ {gsub(/%/, "", $5); print $4, $5}')"
  [ -n "$out" ] || return 1
  printf '%s\n' "$out"
}

# alert_mem_available_mb <meminfo>: доступная память в МБ.
alert_mem_available_mb() {
  local out
  [ -r "$1" ] || return 1
  out="$(awk '/^MemAvailable:/ {printf "%d", $2 / 1024; found=1} END {if (!found) exit 1}' "$1")"
  [ -n "$out" ] || return 1
  printf '%s\n' "$out"
}

# alert_load_hundredths <loadavg>: load1 в сотых (целое сравнение без float).
alert_load_hundredths() {
  local out
  [ -r "$1" ] || return 1
  out="$(awk 'NR==1 {printf "%d", $1 * 100}' "$1")"
  [ -n "$out" ] || return 1
  printf '%s\n' "$out"
}
