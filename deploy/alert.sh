#!/usr/bin/env bash
set -euo pipefail

# Обнаружение недоступности, старых бэкапов и нехватки ресурсов с оповещением
# оператора. Запускается на сервере по таймеру.
#
#   /opt/menu/deploy/alert.sh          # проверить публичный путь, бэкап, ресурсы
#   /opt/menu/deploy/alert.sh --test   # проверочное уведомление в канал
#
# Публичный путь и readiness проверяются через внешний адрес (curl на ALERT_PUBLIC_URL),
# а не только внутренний порт. Возраст копии считается по последнему complete-набору
# в BACKUP_REMOTE, поэтому неполный upload не обновляет время успеха. Ресурсы
# (диск, память, load) считаются устойчиво опасными только при нескольких подряд
# пробах — краткий всплеск не поднимает тревогу.
#
# Полный отказ VPS локальный таймер заметить не может: это делается внешним
# монитором по heartbeat (ALERT_HEARTBEAT_URL). Локальная проверка не выдаётся за
# внешний мониторинг; см. deploy/README.md.
#
# Переменные (env или /opt/menu/server.conf):
#   ALERT_WEBHOOK_URL            канал оповещения, обязателен (секрет, не логируется)
#   ALERT_HEARTBEAT_URL          внешний dead-man switch, опционально (секрет)
#   ALERT_PUBLIC_URL             публичный адрес; иначе PUBLIC_BASE_URL/DOMAIN
#   BACKUP_REMOTE                rclone-remote complete-наборов (обязателен для проверки)
#   BACKUP_MAX_AGE_HOURS         допустимый возраст копии (26)
#   ALERT_DISK_MIN_FREE_MB       минимум свободного места (2048)
#   ALERT_DISK_MAX_USED_PERCENT  максимум занятости диска (90)
#   ALERT_MEM_MIN_MB             минимум доступной памяти (256)
#   ALERT_LOAD_MAX_PER_CPU       максимум load1 на ядро (2)
#   ALERT_DEDUP_SECONDS          окно дедупликации повторов (3600)
# Окружение процесса (не server.conf): ALERT_STATE_FILE, ALERT_INSTALL_ID,
# ALERT_BREACH_SAMPLES, ALERT_SAMPLE_INTERVAL_SECONDS, ALERT_HTTP_TIMEOUT_SECONDS,
# ALERT_CPU_COUNT, ALERT_PROC_MEMINFO, ALERT_PROC_LOADAVG, CURL_BIN.

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$APP_DIR"

# shellcheck disable=SC1091
. "$APP_DIR/deploy/config.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/backup-lib.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/release.sh"
# shellcheck disable=SC1091
. "$APP_DIR/deploy/alert-lib.sh"

log() { printf '\033[1;32m[alert]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[alert]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[alert]\033[0m %s\n' "$*" >&2; exit 1; }

MODE="check"
case "${1-}" in
  "")      MODE="check" ;;
  --test)  MODE="test" ;;
  *) printf 'Использование: %s [--test]\n' "$0" >&2; exit 2 ;;
esac

config_load "$APP_DIR/server.conf" server || exit 1
config_load "$APP_DIR/current-release" server || exit 1

config_require ALERT_WEBHOOK_URL || exit 1
alert_url_valid "$ALERT_WEBHOOK_URL" || die "Некорректный ALERT_WEBHOOK_URL (ожидается http(s) URL)"
if [ -n "${ALERT_HEARTBEAT_URL-}" ]; then
  alert_url_valid "$ALERT_HEARTBEAT_URL" || die "Некорректный ALERT_HEARTBEAT_URL"
fi

ALERT_HTTP_TIMEOUT_SECONDS="${ALERT_HTTP_TIMEOUT_SECONDS-10}"
[[ "$ALERT_HTTP_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]] \
  || die "ALERT_HTTP_TIMEOUT_SECONDS должен быть целым > 0"
ALERT_DEDUP_SECONDS="${ALERT_DEDUP_SECONDS-3600}"
[[ "$ALERT_DEDUP_SECONDS" =~ ^[0-9]+$ ]] \
  || die "ALERT_DEDUP_SECONDS должен быть целым ≥ 0"
ALERT_STATE_FILE="${ALERT_STATE_FILE:-$APP_DIR/alert-state}"
ALERT_INSTALL_ID="${ALERT_INSTALL_ID:-$(hostname)}"
[[ "$ALERT_INSTALL_ID" =~ ^[A-Za-z0-9._-]+$ ]] || ALERT_INSTALL_ID="unknown"
CURL_BIN="${CURL_BIN:-curl}"

RELEASE=""
if [ -f "$APP_DIR/release.json" ]; then
  RELEASE="$(release_manifest_field "$APP_DIR/release.json" tag || true)"
fi
[ -n "$RELEASE" ] || RELEASE="${IMAGE_TAG-}"
[ -n "$RELEASE" ] || RELEASE="unknown"
AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

TMP="$(mktemp -d)"
cleanup() { rm -rf "$TMP"; }
trap cleanup EXIT

ALERT_FAILED=0

# alert_send <message>: доставить сообщение в канал. URL — только во временном
# --config (0600), не в argv и не в журнале; сама полезная нагрузка секретов не
# содержит. Неуспешная доставка — ненулевой код.
alert_send() {
  local message="$1" payload cfg code rc
  payload="$(mktemp "$TMP/payload.XXXXXX")"
  cfg="$(mktemp "$TMP/curl.XXXXXX")"
  alert_curl_url_config "$cfg" "$ALERT_WEBHOOK_URL"
  printf '{"text":"%s"}\n' "$(alert_json_escape "$message")" > "$payload"
  set +e
  code="$("$CURL_BIN" --config "$cfg" -sS -m "$ALERT_HTTP_TIMEOUT_SECONDS" \
    -o "$TMP/response" -w '%{http_code}' \
    -H 'Content-Type: application/json' --data-binary "@$payload" 2>"$TMP/curl-err")"
  rc=$?
  set -e
  rm -f "$cfg" "$payload"
  if [ "$rc" -eq 0 ] && [ -n "$code" ] && [ "${code:0:1}" = "2" ]; then
    return 0
  fi
  printf 'Оповещение: доставка не удалась (curl код %s, HTTP %s)\n' \
    "$rc" "${code:-нет}" >&2
  return 1
}

# alert_process <problem> <cause> <detail>: <cause> пуст — проблема устранена,
# отправляется отдельное сообщение восстановления. Повтор того же события
# подавляется в окне ALERT_DEDUP_SECONDS; смена причины — новое событие. Причины
# сведены к стабильным (например, давление на диск — «pressure»), чтобы дрейф
# значений не обходил дедупликацию.
alert_process() {
  local problem="$1" cause="$2" detail="$3" stored last now message
  now="$(date -u +%s)"
  stored="$(backup_state_get "$ALERT_STATE_FILE" "active_$problem")"
  last="$(backup_state_get "$ALERT_STATE_FILE" "last_sent_$problem")"
  if [ -n "$cause" ]; then
    if [ "$stored" = "$cause" ] && [[ "$last" =~ ^[0-9]+$ ]] \
        && [ $((now - last)) -lt "$ALERT_DEDUP_SECONDS" ]; then
      return 0
    fi
    message="$(alert_alert_message "$ALERT_INSTALL_ID" "$AT" "$RELEASE" \
      "$problem" "$cause" "$detail")"
    if alert_send "$message"; then
      backup_state_set "$ALERT_STATE_FILE" "active_$problem" "$cause"
      backup_state_set "$ALERT_STATE_FILE" "last_sent_$problem" "$now"
    else
      ALERT_FAILED=1
    fi
  elif [ -n "$stored" ]; then
    message="$(alert_recovery_message "$ALERT_INSTALL_ID" "$AT" "$RELEASE" \
      "$problem" "$stored")"
    if alert_send "$message"; then
      backup_state_set "$ALERT_STATE_FILE" "active_$problem" ""
      backup_state_set "$ALERT_STATE_FILE" "last_sent_$problem" ""
    else
      ALERT_FAILED=1
    fi
  fi
}

if [ "$MODE" = "test" ]; then
  alert_send "$(alert_test_message "$ALERT_INSTALL_ID" "$AT" "$RELEASE")" \
    || die "Проверочное уведомление не доставлено"
  log "Проверочное уведомление отправлено"
  exit 0
fi

config_require BACKUP_REMOTE || exit 1

# Публичный адрес: явный ALERT_PUBLIC_URL, иначе production PUBLIC_BASE_URL,
# иначе домен. HTTP без домена — только явный лабораторный режим.
ALERT_PUBLIC_URL="${ALERT_PUBLIC_URL-}"
if [ -z "$ALERT_PUBLIC_URL" ]; then
  if [ -n "${PUBLIC_BASE_URL-}" ]; then
    ALERT_PUBLIC_URL="$PUBLIC_BASE_URL"
  elif [ -n "${DOMAIN-}" ] && [ "$DOMAIN" != ":80" ]; then
    ALERT_PUBLIC_URL="https://$DOMAIN"
  else
    die "не задан публичный адрес: ALERT_PUBLIC_URL или PUBLIC_BASE_URL"
  fi
fi
alert_url_valid "$ALERT_PUBLIC_URL" || die "Некорректный ALERT_PUBLIC_URL"
case "$ALERT_PUBLIC_URL" in
  http://*)
    [ "${DEPLOYMENT_MODE-production}" = "lab" ] \
      || die "HTTP-адрес допустим только в лабораторном режиме (DEPLOYMENT_MODE=lab)" ;;
esac

BACKUP_MAX_AGE_HOURS="${BACKUP_MAX_AGE_HOURS-26}"
ALERT_DISK_MIN_FREE_MB="${ALERT_DISK_MIN_FREE_MB-2048}"
ALERT_DISK_MAX_USED_PERCENT="${ALERT_DISK_MAX_USED_PERCENT-90}"
ALERT_MEM_MIN_MB="${ALERT_MEM_MIN_MB-256}"
ALERT_LOAD_MAX_PER_CPU="${ALERT_LOAD_MAX_PER_CPU-2}"
for key in BACKUP_MAX_AGE_HOURS ALERT_DISK_MIN_FREE_MB ALERT_MEM_MIN_MB \
           ALERT_LOAD_MAX_PER_CPU; do
  value="${!key-}"
  [[ "$value" =~ ^[1-9][0-9]*$ ]] || die "$key должен быть целым > 0"
done
if [[ ! "$ALERT_DISK_MAX_USED_PERCENT" =~ ^[1-9][0-9]{0,2}$ ]] \
    || [ "$ALERT_DISK_MAX_USED_PERCENT" -gt 100 ]; then
  die "ALERT_DISK_MAX_USED_PERCENT должен быть целым 1–100"
fi

ALERT_BREACH_SAMPLES="${ALERT_BREACH_SAMPLES-2}"
[[ "$ALERT_BREACH_SAMPLES" =~ ^[1-9][0-9]*$ ]] \
  || die "ALERT_BREACH_SAMPLES должен быть целым > 0"
ALERT_SAMPLE_INTERVAL_SECONDS="${ALERT_SAMPLE_INTERVAL_SECONDS-15}"
[[ "$ALERT_SAMPLE_INTERVAL_SECONDS" =~ ^[0-9]+$ ]] \
  || die "ALERT_SAMPLE_INTERVAL_SECONDS должен быть целым ≥ 0"
ALERT_CPU_COUNT="${ALERT_CPU_COUNT-$(nproc)}"
[[ "$ALERT_CPU_COUNT" =~ ^[1-9][0-9]*$ ]] \
  || die "ALERT_CPU_COUNT должен быть целым > 0"
ALERT_PROC_MEMINFO="${ALERT_PROC_MEMINFO:-/proc/meminfo}"
ALERT_PROC_LOADAVG="${ALERT_PROC_LOADAVG:-/proc/loadavg}"

# http_get <url> <out-file>: код ответа в stdout; тело — в файл.
http_get() {
  "$CURL_BIN" -sS -m "$ALERT_HTTP_TIMEOUT_SECONDS" -o "$2" -w '%{http_code}' "$1"
}

# check_public_path: readiness и SPA по публичному адресу.
check_public_path() {
  local code body="$TMP/public-body" host
  host="$(alert_url_host "$ALERT_PUBLIC_URL")"
  if ! code="$(http_get "$ALERT_PUBLIC_URL/ready" "$body" 2>/dev/null)"; then
    alert_process public_path connect "нет соединения с публичным $host/ready"
    return 0
  fi
  if [ "$code" != "200" ] || ! grep -q '"status":"ready"' "$body" 2>/dev/null; then
    alert_process public_path ready "readiness на $host вернула код $code"
    return 0
  fi
  if ! code="$(http_get "$ALERT_PUBLIC_URL/" "$body" 2>/dev/null)"; then
    alert_process public_path connect "нет соединения с публичным $host/"
    return 0
  fi
  if [ "$code" != "200" ] || ! grep -qi '<!doctype html' "$body" 2>/dev/null; then
    alert_process public_path spa "SPA на $host не отдаёт HTML (код $code)"
    return 0
  fi
  alert_process public_path "" ""
}

# check_backup: возраст последнего complete-набора; незавершённый набор не
# считается копией и не обновляет время успеха.
check_backup() {
  local latest epoch age max
  if ! latest="$(backup_latest_set "$BACKUP_REMOTE")"; then
    alert_process backup unreadable "не удалось прочитать complete-наборы из BACKUP_REMOTE"
    return 0
  fi
  if [ -z "$latest" ]; then
    alert_process backup missing "нет ни одного complete-набора"
    return 0
  fi
  if ! epoch="$(alert_set_epoch "$latest")"; then
    alert_process backup unreadable "некорректный id complete-набора из хранилища"
    return 0
  fi
  age="$(( $(date -u +%s) - epoch ))"
  max="$((BACKUP_MAX_AGE_HOURS * 3600))"
  if [ "$age" -gt "$max" ]; then
    alert_process backup stale \
      "последний complete-набор $latest старше $((age / 3600)) ч (порог $BACKUP_MAX_AGE_HOURS ч)"
    return 0
  fi
  alert_process backup "" ""
}

# check_resources: устойчивое давление (несколько подряд проб) — иначе краткий
# всплеск дал бы ложную тревогу. Нечитаемая метрика — отдельная причина
# «unreadable», а не «здорово»: иначе сбой чтения давал бы ложное восстановление.
check_resources() {
  local samples="$ALERT_BREACH_SAMPLES" interval="$ALERT_SAMPLE_INTERVAL_SECONDS"
  local load_limit=$((ALERT_CPU_COUNT * ALERT_LOAD_MAX_PER_CPU * 100))
  local dfree=0 dused=0 mem=0 load=0 i sample avail used memavail loadh
  local disk_fail=0 mem_fail=0 load_fail=0 disk_cause
  for ((i = 0; i < samples; i++)); do
    if [ "$i" -gt 0 ]; then sleep "$interval"; fi
    if sample="$(alert_df_sample "$APP_DIR" 2>/dev/null)"; then
      read -r avail used <<< "$sample"
      [ "$avail" -lt "$ALERT_DISK_MIN_FREE_MB" ] && dfree=$((dfree + 1))
      [ "$used" -gt "$ALERT_DISK_MAX_USED_PERCENT" ] && dused=$((dused + 1))
    else
      disk_fail=$((disk_fail + 1))
    fi
    if memavail="$(alert_mem_available_mb "$ALERT_PROC_MEMINFO" 2>/dev/null)"; then
      [ "$memavail" -lt "$ALERT_MEM_MIN_MB" ] && mem=$((mem + 1))
    else
      mem_fail=$((mem_fail + 1))
    fi
    if loadh="$(alert_load_hundredths "$ALERT_PROC_LOADAVG" 2>/dev/null)"; then
      [ "$loadh" -gt "$load_limit" ] && load=$((load + 1))
    else
      load_fail=$((load_fail + 1))
    fi
  done

  disk_cause=""
  if [ "$disk_fail" -gt 0 ]; then
    disk_cause="unreadable"
  elif [ "$dfree" -ge "$samples" ] || [ "$dused" -ge "$samples" ]; then
    # Стабильная причина «pressure»: free↔used не считаются новым событием.
    disk_cause="pressure"
  fi
  if [ "$disk_cause" = "unreadable" ]; then
    alert_process disk unreadable "не удалось прочитать свободное место на $APP_DIR"
  elif [ "$disk_cause" = "pressure" ]; then
    alert_process disk pressure \
      "опасное давление на диск $APP_DIR (минимум $ALERT_DISK_MIN_FREE_MB МБ, максимум $ALERT_DISK_MAX_USED_PERCENT%)"
  else
    alert_process disk "" ""
  fi

  if [ "$mem_fail" -gt 0 ]; then
    alert_process memory unreadable \
      "не удалось прочитать доступную память из $ALERT_PROC_MEMINFO"
  elif [ "$mem" -ge "$samples" ]; then
    alert_process memory available \
      "мало доступной памяти (порог $ALERT_MEM_MIN_MB МБ)"
  else
    alert_process memory "" ""
  fi

  if [ "$load_fail" -gt 0 ]; then
    alert_process load unreadable \
      "не удалось прочитать load из $ALERT_PROC_LOADAVG"
  elif [ "$load" -ge "$samples" ]; then
    alert_process load high \
      "высокая нагрузка (порог $ALERT_LOAD_MAX_PER_CPU на ядро, ядер $ALERT_CPU_COUNT)"
  else
    alert_process load "" ""
  fi
}

# send_heartbeat: отметка внешнему dead-man switch. Пинг идёт при каждом
# завершившемся запуске, поэтому внешний монитор срабатывает на полный отказ VPS,
# а не на отдельную проблему компонента.
send_heartbeat() {
  [ -n "${ALERT_HEARTBEAT_URL-}" ] || return 0
  local cfg code
  cfg="$(mktemp "$TMP/hb.XXXXXX")"
  alert_curl_url_config "$cfg" "$ALERT_HEARTBEAT_URL"
  code="$("$CURL_BIN" --config "$cfg" -sS -m "$ALERT_HTTP_TIMEOUT_SECONDS" \
    -o /dev/null -w '%{http_code}' 2>/dev/null || true)"
  rm -f "$cfg"
  case "$code" in
    2*) return 0 ;;
  esac
  warn "heartbeat не отправлен (HTTP ${code:-нет}) — внешний монитор может счесть VPS мёртвым"
  ALERT_FAILED=1
}

log "Проверка публичного пути, бэкапа и ресурсов"
check_public_path
check_backup
check_resources
send_heartbeat

if [ "$ALERT_FAILED" -ne 0 ]; then
  die "часть уведомлений не доставлена"
fi
log "Проверка завершена"
