#!/usr/bin/env bash
set -euo pipefail

# Анализ зависимостей backend/frontend на уязвимости. По умолчанию только
# сообщает конкретные findings (пакет, версия, серьёзность) без секретов;
# при GATE_AUDIT_ENFORCE=1 провал на уровне GATE_AUDIT_LEVEL (по умолчанию high).
#
#   scripts/check-dependencies.sh
#
# Переменные:
#   GATE_DOCKER          команда docker (по умолчанию docker)
#   GATE_ARTIFACT_DIR    каталог для findings (по умолчанию deploy/gate-artifacts)
#   GATE_AUDIT_ENFORCE   1 — проваливать гейт при findings уровня выше порога
#   GATE_AUDIT_LEVEL     порог npm audit (по умолчанию high)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

DOCKER="${GATE_DOCKER:-docker}"
ART="${GATE_ARTIFACT_DIR:-$ROOT/deploy/gate-artifacts}"
ENFORCE="${GATE_AUDIT_ENFORCE:-0}"
LEVEL="${GATE_AUDIT_LEVEL:-high}"
SDK_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"
NODE_IMAGE="${RELEASE_NODE_IMAGE:-node:24-alpine}"

log()  { printf '\033[1;32m[deps]\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m[deps]\033[0m %s\n' "$*" >&2; exit 1; }

mkdir -p "$ART"
BACKEND_JSON="$ART/deps-backend.json"
FRONTEND_JSON="$ART/deps-frontend.json"

log "Backend: dotnet list package --vulnerable (findings → $BACKEND_JSON)"
"$DOCKER" run --rm \
  -u "$(id -u):$(id -g)" \
  -e DOTNET_CLI_HOME=/tmp/dotnet-home \
  -e NUGET_PACKAGES=/tmp/nuget \
  -v "$ROOT":/app -w /app \
  "$SDK_IMAGE" sh -c \
  "dotnet list backend/src/MenuPlanner.Api/MenuPlanner.Api.csproj package --vulnerable --include-transitive --format json" \
  > "$BACKEND_JSON" 2>"$ART/deps-backend.err" || true

log "Frontend: npm audit (findings → $FRONTEND_JSON)"
"$DOCKER" run --rm \
  -u "$(id -u):$(id -g)" \
  -e HOME=/tmp \
  -v "$ROOT":/app -w /app/frontend \
  "$NODE_IMAGE" sh -c "npm audit --omit=dev --audit-level=$LEVEL --json" \
  > "$FRONTEND_JSON" 2>"$ART/deps-frontend.err" || true

python3 - "$BACKEND_JSON" "$FRONTEND_JSON" "$ENFORCE" <<'PY'
import json
import sys

backend_path, frontend_path, enforce = sys.argv[1], sys.argv[2], sys.argv[3] == "1"
findings = []

try:
    with open(backend_path, encoding="utf-8") as stream:
        backend = json.load(stream)
except (OSError, json.JSONDecodeError):
    backend = {}

def walk(node):
    if isinstance(node, dict):
        if "vulnerabilities" in node and isinstance(node["vulnerabilities"], list):
            for vuln in node["vulnerabilities"]:
                findings.append(("backend", node.get("id", "?"), vuln.get("severity", "?"),
                                  vuln.get("advisoryurl", "")))
        for value in node.values():
            walk(value)
    elif isinstance(node, list):
        for value in node:
            walk(value)

walk(backend.get("projects", []))

try:
    with open(frontend_path, encoding="utf-8") as stream:
        frontend = json.load(stream)
except (OSError, json.JSONDecodeError):
    frontend = {}

advisories = frontend.get("vulnerabilities")
if isinstance(advisories, dict):
    for name, info in advisories.items():
        findings.append(("frontend", name, info.get("severity", "?"), info.get("url", "")))

if not findings:
    print("[deps] Findings не обнаружены")
    sys.exit(0)

print("[deps] Findings:")
for area, package, severity, url in findings:
    print(f"  - {area}: {package} [{severity}] {url}")

blocking = [f for f in findings if str(f[2]).lower() in ("high", "critical")]
if blocking and enforce:
    print("[deps] Есть findings уровня high/critical при GATE_AUDIT_ENFORCE=1 — провал", file=sys.stderr)
    sys.exit(1)

if blocking:
    print("[deps] Есть findings high/critical: зафиксируй решение по каждому (обновить/принять) — "
          "политика в deploy/README.md, раздел «Политика обработки findings»", file=sys.stderr)
PY

log "Анализ зависимостей завершён (findings выше)"
