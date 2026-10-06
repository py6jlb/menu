#!/usr/bin/env bash
# Доверенная библиотека воспроизводимого релиза.
# Связывает git-commit, дайджесты образов, версии конфигурации и время сборки
# в manifest, который сохраняется и проверяется при деплое и smoke.

# Поддерживаемая LTS-версия Node; та же, что в frontend/Dockerfile.
RELEASE_NODE_IMAGE="${RELEASE_NODE_IMAGE-node:24-alpine}"

# Закреплённые базовые образы. Обновление — отдельным коммитом с проверкой
# локальной сборки и деплоя на стенде (см. deploy/README.md).
release_base_images() {
  printf '%s\n' \
    mcr.microsoft.com/dotnet/sdk:10.0 \
    mcr.microsoft.com/dotnet/aspnet:10.0 \
    node:24-alpine \
    nginx:1.27-alpine \
    postgres:16 \
    caddy:2-alpine \
    otel/opentelemetry-collector-contrib:0.161.0 \
    alpine:3.20
}


# Файлы выпускаемого среза: их хэши входят в manifest как версии конфигурации.
release_config_files() {
  printf '%s\n' \
    docker-compose.prod.yml \
    deploy/Caddyfile \
    deploy/otel-collector.yaml \
    deploy/config.sh \
    deploy/compose.sh \
    deploy/release.sh \
    deploy/backup-lib.sh \
    deploy/deploy-lib.sh \
    deploy/remote-deploy.sh \
    deploy/rollback.sh \
    deploy/backup.sh \
    deploy/restore-drill.sh \
    deploy/smoke.sh \
    deploy/logs.sh \
    deploy/alert-lib.sh \
    deploy/alert.sh
}

# release_config_hashes <root>: строки "<относительный путь> <sha256>".
release_config_hashes() {
  local root="$1" file hash
  while IFS= read -r file; do
    [ -f "$root/$file" ] || continue
    hash="$(sha256sum "$root/$file" | cut -d' ' -f1)"
    printf '%s %s\n' "$file" "$hash"
  done < <(release_config_files)
}

# release_require_clean_tree <repo>: релиз собирается только с чистого checkout.
release_require_clean_tree() {
  local repo="${1-.}" status
  status="$(git -C "$repo" status --porcelain --untracked-files=normal)"
  if [ -n "$status" ]; then
    printf 'Релиз: есть незакоммиченные изменения — закоммить или убрать их до сборки\n' >&2
    return 1
  fi
}

# release_resolve_commit <repo> <rev>: полный sha коммита или ошибка.
release_resolve_commit() {
  local repo="$1" rev="$2" sha
  sha="$(git -C "$repo" rev-parse --verify "${rev}^{commit}" 2>/dev/null)" || {
    printf 'Релиз: не найден коммит «%s»\n' "$rev" >&2
    return 1
  }
  printf '%s\n' "$sha"
}

# release_built_at: UTC-время сборки в формате ISO-8601.
release_built_at() {
  date -u +%Y-%m-%dT%H:%M:%SZ
}

# release_manifest <out> <commit> <tag> <builtAt> <backendDigest> <frontendDigest> <hashesFile> [<recoverySet>]
# Значения ограничены безопасным алфавитом (sha, тег, ISO-время, дайджест, hex),
# поэтому JSON собирается printf без экранирования произвольного текста.
# recoverySet — id complete-набора, привязанного к рискованному обновлению.
release_manifest() {
  local out="$1" commit="$2" tag="$3" built_at="$4" backend_digest="$5" \
        frontend_digest="$6" hashes="$7" recovery_set="${8-}" first=1 file hash
  {
    printf '{\n'
    printf '  "commit": "%s",\n' "$commit"
    printf '  "tag": "%s",\n' "$tag"
    printf '  "builtAt": "%s",\n' "$built_at"
    printf '  "backendDigest": "%s",\n' "$backend_digest"
    printf '  "frontendDigest": "%s",\n' "$frontend_digest"
    if [ -n "$recovery_set" ]; then
      printf '  "recoverySet": "%s",\n' "$recovery_set"
    fi
    printf '  "config": {'
    while IFS=' ' read -r file hash; do
      [ -n "$file" ] || continue
      [ "$first" -eq 1 ] || printf ','
      printf '\n    "%s": "%s"' "$file" "$hash"
      first=0
    done < "$hashes"
    if [ "$first" -eq 0 ]; then
      printf '\n  '
    fi
    printf '}\n}\n'
  } > "$out"
}

# release_image_digest <image> [<docker-inspect-template>]
# Дайджест образа; "unknown", если Docker его не знает (локальная сборка без push).
release_image_digest() {
  local image="$1" template="${2-index .RepoDigests 0}" value
  value="$(docker image inspect --format "{{$template}}" "$image" 2>/dev/null || true)"
  [ -n "$value" ] || value="unknown"
  printf '%s' "$value"
}

# release_manifest_field <manifest> <key>: первое строковое значение поля.
release_manifest_field() {
  local manifest="$1" key="$2"
  awk -v key="$key" '
    index($0, "\"" key "\": \"") {
      line = $0
      sub(/^.*": "/, "", line)
      sub(/".*$/, "", line)
      print line
      exit
    }' "$manifest"
}

# release_verify_config <manifest> <hashesFile>: сверить версии конфигурации.
release_verify_config() {
  local manifest="$1" hashes="$2" file hash expected
  while IFS=' ' read -r file hash; do
    [ -n "$file" ] || continue
    expected="$(release_manifest_field "$manifest" "$file")"
    if [ -z "$expected" ] || [ "$expected" != "$hash" ]; then
      printf 'Релиз: конфигурация «%s» не соответствует manifest\n' "$file" >&2
      return 1
    fi
  done < "$hashes"
}
