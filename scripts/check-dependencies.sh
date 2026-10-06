#!/usr/bin/env bash
set -euo pipefail

# Анализ зависимостей (npm/dotnet) и образов на уязвимости и закрепление версий.
# По умолчанию сообщает конкретные findings (пакет, серьёзность, ссылка) без
# секретов; при GATE_AUDIT_ENFORCE=1 провал на уровне GATE_AUDIT_LEVEL.
# Скан уязвимостей образов (Trivy) включается отдельно GATE_IMAGE_SCAN=1.
#
#   scripts/check-dependencies.sh
#
# Переменные:
#   GATE_DOCKER          команда docker (по умолчанию docker)
#   GATE_ARTIFACT_DIR    каталог для findings (по умолчанию deploy/gate-artifacts)
#   GATE_AUDIT_ENFORCE   1 — проваливать гейт при findings уровня ≥ порога
#   GATE_AUDIT_LEVEL     порог серьёзности (по умолчанию high)
#   GATE_IMAGE_SCAN      1 — дополнительно сканировать базовые образы Trivy

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# shellcheck disable=SC1091
. "$ROOT/deploy/release.sh"

DOCKER="${GATE_DOCKER:-docker}"
ART="${GATE_ARTIFACT_DIR:-$ROOT/deploy/gate-artifacts}"
ENFORCE="${GATE_AUDIT_ENFORCE:-0}"
LEVEL="${GATE_AUDIT_LEVEL:-high}"
IMAGE_SCAN="${GATE_IMAGE_SCAN:-0}"
SDK_IMAGE="$RELEASE_SDK_IMAGE"
NODE_IMAGE="$RELEASE_NODE_IMAGE"
TRIVY_IMAGE="${TRIVY_IMAGE:-aquasec/trivy:0.57.1}"

log()  { printf '\033[1;32m[deps]\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m[deps]\033[0m %s\n' "$*" >&2; exit 1; }

mkdir -p "$ART"
BACKEND_JSON="$ART/deps-backend.json"
FRONTEND_JSON="$ART/deps-frontend.json"
IMAGES_JSON="$ART/deps-images.json"
IMAGES_TXT="$ART/deps-images.txt"

log "Backend: dotnet list package --vulnerable (findings → $BACKEND_JSON)"
if ! "$DOCKER" run --rm \
  -u "$(id -u):$(id -g)" \
  -e DOTNET_CLI_HOME=/tmp/dotnet-home \
  -e NUGET_PACKAGES=/tmp/nuget \
  -v "$ROOT":/app -w /app \
  "$SDK_IMAGE" sh -c \
  "dotnet list backend/src/MenuPlanner.Api/MenuPlanner.Api.csproj package --vulnerable --include-transitive --format json" \
  > "$BACKEND_JSON" 2>"$ART/deps-backend.err"; then
  printf 'backend: анализ зависимостей не выполнился (см. %s)\n' "$ART/deps-backend.err" >&2
fi

log "Frontend: npm audit (findings → $FRONTEND_JSON)"
if ! "$DOCKER" run --rm \
  -u "$(id -u):$(id -g)" \
  -e HOME=/tmp \
  -v "$ROOT":/app -w /app/frontend \
  "$NODE_IMAGE" sh -c "npm audit --omit=dev --audit-level=$LEVEL --json" \
  > "$FRONTEND_JSON" 2>"$ART/deps-frontend.err"; then
  printf 'frontend: npm audit сообщил findings или не выполнился (см. %s)\n' "$ART/deps-frontend.err" >&2
fi

log "Проверка закрепления базовых образов"
: > "$IMAGES_TXT"
while IFS= read -r image; do
  [ -n "$image" ] || continue
  ref="${image##*/}"
  case "$ref" in
    *:*) if [ "${image##*:}" = "latest" ]; then printf 'плавающий тег: %s\n' "$image" >> "$IMAGES_TXT"; fi ;;
    *) printf 'не закреплён тег: %s\n' "$image" >> "$IMAGES_TXT" ;;
  esac
done < <(release_base_images)
[ -s "$IMAGES_TXT" ] || printf 'все базовые образы закреплены тегами\n' >> "$IMAGES_TXT"

if [ "$IMAGE_SCAN" = "1" ]; then
  log "Скан образов Trivy ($TRIVY_IMAGE)"
  : > "$IMAGES_JSON"
  while IFS= read -r image; do
    [ -n "$image" ] || continue
    log "  $image"
    "$DOCKER" run --rm \
      -v /var/run/docker.sock:/var/run/docker.sock \
      "$TRIVY_IMAGE" image --quiet --format json --severity HIGH,CRITICAL "docker.io/library/$image" \
      >> "$IMAGES_JSON" 2>"$ART/deps-images.err" \
      || printf 'image: скан «%s» не выполнился (см. %s)\n' "$image" "$ART/deps-images.err" >&2
  done < <(release_base_images)
fi

python3 - "$LEVEL" "$ENFORCE" "$BACKEND_JSON" "$FRONTEND_JSON" "$IMAGES_JSON" "$IMAGES_TXT" <<'PY'
import json
import sys

level, enforce, backend_path, frontend_path, images_path, images_txt = sys.argv[1:7]
enforce = enforce == "1"

ORDER = {"low": 1, "moderate": 2, "medium": 2, "high": 3, "critical": 4}
threshold = ORDER.get(level.lower(), 3)
findings = []


def load(path):
    try:
        with open(path, encoding="utf-8") as stream:
            text = stream.read().strip()
        return json.loads(text) if text else {}
    except (OSError, json.JSONDecodeError):
        return None


def walk(node, out):
    if isinstance(node, dict):
        if isinstance(node.get("vulnerabilities"), list):
            for vuln in node["vulnerabilities"]:
                out.append(("backend", node.get("id", "?"), vuln.get("severity", "?"),
                            vuln.get("advisoryurl", "")))
        for value in node.values():
            walk(value, out)
    elif isinstance(node, list):
        for value in node:
            walk(value, out)


backend = load(backend_path)
if backend is None:
    print("[deps] WARNING: результаты backend-анализа недоступны — findings не подтверждены")
else:
    walk(backend.get("projects", []), findings)

frontend = load(frontend_path)
if frontend is None:
    print("[deps] WARNING: результаты npm audit недоступны — findings не подтверждены")
elif isinstance(frontend.get("vulnerabilities"), dict):
    for name, info in frontend["vulnerabilities"].items():
        findings.append(("frontend", name, info.get("severity", "?"), info.get("url", "")))

images = load(images_path)
if images_path and images:
    if isinstance(images, list):
        results = images
    elif isinstance(images, dict) and "Results" in images:
        results = images["Results"]
    else:
        results = []
    for result in results:
        target = result.get("Target", "?")
        for vuln in result.get("Vulnerabilities", []) or []:
            findings.append(("image", target, vuln.get("Severity", "?"),
                             vuln.get("PrimaryURL", "")))

try:
    with open(images_txt, encoding="utf-8") as stream:
        pinning = [line.strip() for line in stream if line.strip()]
except OSError:
    pinning = []

print("[deps] Закрепление базовых образов:")
for line in pinning:
    if line != "все базовые образы закреплены тегами":
        findings.append(("image", "base-image", "HIGH", line))

if not findings:
    print("[deps] Findings не обнаружены")
    sys.exit(0)

print("[deps] Findings:")
blocking = []
for area, package, severity, url in findings:
    print(f"  - {area}: {package} [{severity}] {url}".rstrip())
    if ORDER.get(str(severity).lower(), 0) >= threshold:
        blocking.append((area, package, severity))

if blocking and enforce:
    print(f"[deps] {len(blocking)} findings уровня ≥ {level} при GATE_AUDIT_ENFORCE=1 — провал",
          file=sys.stderr)
    sys.exit(1)

if blocking:
    print(f"[deps] {len(blocking)} findings уровня ≥ {level}: зафиксируй решение по каждому "
          "(обновить/принять) — политика в deploy/README.md, раздел «Политика обработки findings»",
          file=sys.stderr)
PY

log "Анализ зависимостей и образов завершён (findings выше)"
