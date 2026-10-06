#!/usr/bin/env bash
set -euo pipefail

# Единый автоматический гейт перед публикацией релиза.
#
#   scripts/release-gate.sh                 # все быстрые этапы
#   scripts/release-gate.sh --with-browser  # плюс сквозной browser smoke
#   scripts/release-gate.sh --list
#   scripts/release-gate.sh --only backend,postgres
#   scripts/release-gate.sh --skip deps
#
# Гейт ничего не публикует, не деплоит сервер и не требует production-секретов.
# Каждый этап пишет диагностику в отдельный журнал; при провале печатается
# этап, причина (хвост журнала) и путь к полному журналу.
#
# Переменные:
#   GATE_ARTIFACT_DIR   каталог диагностики (по умолчанию deploy/gate-artifacts)
#   GATE_DOCKER         команда docker (по умолчанию docker)
#   GATE_BROWSER        1 — включить этап browser (как --with-browser)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# shellcheck disable=SC1091
. "$ROOT/deploy/release.sh"

GATE_ARTIFACT_DIR="${GATE_ARTIFACT_DIR:-$ROOT/deploy/gate-artifacts}"
DOCKER="${GATE_DOCKER:-docker}"
LOG_TAIL_LINES="${GATE_LOG_TAIL_LINES:-40}"

WITH_BROWSER=0
ONLY=""
SKIP=""
LIST=0

# Все этапы в порядке выполнения. Каждый этап — отдельная проверяемая область.
ALL_STAGES=(shell deploy infra backend postgres migrations frontend deps browser)
declare -A STAGE_TITLE=(
  [shell]="shell: синтаксис и shellcheck"
  [deploy]="deploy: конфигурация и скрипты (python suite)"
  [infra]="infra: Compose, Caddy, Collector"
  [backend]="backend: быстрые тесты (.NET)"
  [postgres]="postgres: критические гарантии PostgreSQL"
  [migrations]="migrations: drift-guard EF Core"
  [frontend]="frontend: state-тесты и чистая сборка по lockfile"
  [deps]="deps: анализ зависимостей и образов"
  [browser]="browser: сквозной smoke в браузере"
)

usage() {
  cat <<'EOF'
Использование: scripts/release-gate.sh [опции]

  --list                показать этапы и выйти
  --only a,b            выполнить только перечисленные этапы
  --skip a,b            исключить перечисленные этапы
  --with-browser        добавить этап browser (полный стек)
  --artifacts DIR       каталог диагностики
  -h, --help            эта справка
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --list) LIST=1 ;;
    --only) ONLY="${2-}"; shift ;;
    --skip) SKIP="${2-}"; shift ;;
    --with-browser) WITH_BROWSER=1 ;;
    --artifacts) GATE_ARTIFACT_DIR="${2-}"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'Гейт: неизвестная опция «%s»\n' "$1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

if [ "${GATE_BROWSER-}" = "1" ]; then
  WITH_BROWSER=1
fi

# contains <csv> <item>
contains() {
  local csv=",$1," item="$2"
  case "$csv" in *",$item,"*) return 0 ;; *) return 1 ;; esac
}

# Явный --only browser включает этап browser и без --with-browser.
if [ -n "$ONLY" ] && contains "$ONLY" browser; then
  WITH_BROWSER=1
fi

selected_stages() {
  local stage
  for stage in "${ALL_STAGES[@]}"; do
    if [ -n "$ONLY" ] && ! contains "$ONLY" "$stage"; then
      continue
    fi
    if [ -n "$SKIP" ] && contains "$SKIP" "$stage"; then
      continue
    fi
    if [ "$stage" = "browser" ] && [ "$WITH_BROWSER" -ne 1 ]; then
      continue
    fi
    printf '%s\n' "$stage"
  done
}

if [ "$LIST" -eq 1 ]; then
  printf '%-12s %s\n' "stage" "описание"
  for stage in "${ALL_STAGES[@]}"; do
    printf '%-12s %s\n' "$stage" "${STAGE_TITLE[$stage]}"
  done
  exit 0
fi

# Проверка допустимости имён этапов до запуска.
validate_names() {
  local csv="$1" item
  [ -n "$csv" ] || return 0
  IFS=',' read -ra items <<< "$csv"
  for item in "${items[@]}"; do
    [ -n "$item" ] || continue
    case " ${ALL_STAGES[*]} " in
      *" $item "*) ;;
      *) printf 'Гейт: неизвестный этап «%s»\n' "$item" >&2; exit 2 ;;
    esac
  done
}
validate_names "$ONLY"
validate_names "$SKIP"

for tool in "$DOCKER" git python3 bash; do
  command -v "$tool" >/dev/null 2>&1 || {
    printf 'Гейт: не найдена команда «%s»\n' "$tool" >&2
    exit 2
  }
done

mkdir -p "$GATE_ARTIFACT_DIR"
GATE_ARTIFACT_DIR="$(cd "$GATE_ARTIFACT_DIR" && pwd)"
# Этапы-скрипты (infra/deps/postgres/browser) пишут диагностику в тот же каталог.
export GATE_ARTIFACT_DIR
export GATE_DOCKER="$DOCKER"
SUMMARY="$GATE_ARTIFACT_DIR/summary.txt"
# build-push запускает гейт дважды (быстрые этапы → сборка → browser): второй
# запуск дописывает сводку, а не стирает первую. GATE_SUMMARY_APPEND=1 включает это.
if [ "${GATE_SUMMARY_APPEND-}" = "1" ]; then
  printf '\n# повторный запуск: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" >> "$SUMMARY"
else
  : > "$SUMMARY"
fi

log()  { printf '\033[1;34m[gate]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[gate]\033[0m %s\n' "$*" >&2; }

stage_shell() {
  local script
  for script in "$ROOT"/deploy/*.sh "$ROOT"/scripts/*.sh; do
    [ -f "$script" ] || continue
    bash -n "$script" || {
      printf 'bash -n: %s\n' "$script" >&2
      return 1
    }
  done
  if command -v shellcheck >/dev/null 2>&1; then
    shellcheck "$ROOT"/deploy/*.sh "$ROOT"/scripts/*.sh
  else
    log "shellcheck на хосте не найден — запуск в контейнере"
    "$DOCKER" run --rm -v "$ROOT":/app:ro -w /app \
      koalaman/shellcheck:stable deploy/*.sh scripts/*.sh
  fi
}

stage_deploy() {
  ( cd "$ROOT" && python3 -B -m unittest discover -s deploy/tests -v )
}

stage_infra() {
  "$ROOT/scripts/check-infra.sh"
}

stage_backend() {
  "$DOCKER" run --rm \
    -u "$(id -u):$(id -g)" \
    -e DOTNET_CLI_HOME=/tmp/dotnet-home \
    -e NUGET_PACKAGES=/tmp/nuget \
    -v "$ROOT":/app -w /app \
    "$RELEASE_SDK_IMAGE" dotnet test
}

stage_postgres() {
  "$ROOT/scripts/test-postgres.sh"
}

stage_migrations() {
  "$DOCKER" run --rm \
    -u "$(id -u):$(id -g)" \
    -e DOTNET_CLI_HOME=/tmp/dotnet-home \
    -e NUGET_PACKAGES=/tmp/nuget \
    -v "$ROOT":/app -w /app \
    "$RELEASE_SDK_IMAGE" sh -c "dotnet tool restore && dotnet ef migrations has-pending-model-changes --project backend/src/MenuPlanner.Api --startup-project backend/src/MenuPlanner.Api"
}

stage_frontend() {
  "$DOCKER" run --rm \
    -u "$(id -u):$(id -g)" \
    -e HOME=/tmp \
    -v "$ROOT":/app -w /app/frontend \
    "$RELEASE_NODE_IMAGE" sh -c "npm ci && npm test && npm run build"
  if ! git -C "$ROOT" diff --quiet -- frontend/package-lock.json; then
    printf 'frontend: npm ci изменил frontend/package-lock.json — lockfile невоспроизводим\n' >&2
    return 1
  fi
}

stage_deps() {
  "$ROOT/scripts/check-dependencies.sh"
}

stage_browser() {
  "$ROOT/scripts/browser-smoke.sh"
}

run_stage() {
  local stage="$1" logfile="$GATE_ARTIFACT_DIR/$1.log"
  log "Этап «$stage» — ${STAGE_TITLE[$stage]}"
  local start end rc
  start="$(date +%s)"
  # Подshell с errexit: падение любой команды этапа останавливает этап, но не
  # гейт. Внешний errexit на время снят, чтобы перехватить код возврата.
  set +e
  ( set -e; "stage_$stage" ) > "$logfile" 2>&1
  rc=$?
  end="$(date +%s)"
  if [ "$rc" -eq 0 ]; then
    printf '%-12s OK    %ss\n' "$stage" "$((end - start))" >> "$SUMMARY"
    log "Этап «$stage» — OK ($((end - start))s)"
    return 0
  fi
  printf '%-12s FAIL  %ss  %s\n' "$stage" "$((end - start))" "$logfile" >> "$SUMMARY"
  printf '\n\033[1;31m[gate] ПРОВАЛ этапа «%s» — %s\033[0m\n' "$stage" "${STAGE_TITLE[$stage]}" >&2
  printf '[gate] Диагностика (последние %s строк, полный журнал %s):\n' "$LOG_TAIL_LINES" "$logfile" >&2
  tail -n "$LOG_TAIL_LINES" "$logfile" >&2 || true
  printf '[gate] Полный журнал: %s\n' "$logfile" >&2
  return 1
}

STAGES=()
while IFS= read -r line; do
  [ -n "$line" ] && STAGES+=("$line")
done < <(selected_stages)

if [ "${#STAGES[@]}" -eq 0 ]; then
  warn "Не выбрано ни одного этапа"
  exit 2
fi

log "Гейт релиза: этапы ${STAGES[*]}"
log "Каталог диагностики: $GATE_ARTIFACT_DIR"

FAILED=""
for stage in "${STAGES[@]}"; do
  # run_stage вызывается НЕ в условии `if`: иначе bash подавил бы errexit
  # внутри этапа и падение команды не остановило бы этап.
  set +e
  run_stage "$stage"
  rc=$?
  set -e
  if [ "$rc" -ne 0 ]; then
    FAILED="$stage"
    break
  fi
done

if [ -n "$FAILED" ]; then
  printf '\nГейт: ПРОВАЛ на этапе «%s». Сводка: %s\n' "$FAILED" "$SUMMARY" >&2
  exit 1
fi

log "Гейт ЗЕЛЁНЫЙ — все этапы пройдены"
log "Сводка: $SUMMARY"
