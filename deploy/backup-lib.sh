#!/usr/bin/env bash
# Доверенная библиотека согласованного backup-набора (тикет 40).
#
# Набор считается полным только при наличии маркера complete/<id>. Объекты
# db/photos/manifest без маркера — незавершённый запуск: он не выбирается как
# точка восстановления и не участвует в ротации. Идентификаторы и имена файлов
# приходят из хранилища, поэтому перед подстановкой в пути они проверяются
# разрешённым алфавитом (защита от выхода из префикса).

# backup_set_id: уникальный id запуска. Наносекунды исключают перезапись при
# повторном запуске в ту же секунду/день; первые 10 символов — дата для ротации.
backup_set_id() {
  date -u +%Y-%m-%dT%H%M%S%NZ
}

# backup_utc_now: время в ISO-8601 UTC.
backup_utc_now() {
  date -u +%Y-%m-%dT%H:%M:%SZ
}

# backup_sha256 <file>: hex-дайджест файла.
backup_sha256() {
  sha256sum "$1" | cut -d' ' -f1
}

# backup_set_valid <id>: id из перечисления хранилища перед подстановкой в путь.
backup_set_valid() {
  [[ "$1" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{6,}Z$ ]]
}

# backup_sha_valid <sha>: ровно 64 hex-символа.
backup_sha_valid() {
  [[ "$1" =~ ^[0-9a-f]{64}$ ]]
}

# backup_name_valid <name>: имя объекта без разделителей пути.
backup_name_valid() {
  [[ "$1" =~ ^[A-Za-z0-9._-]+$ ]]
}

# backup_json_string <manifest> <key>: первое строковое значение поля.
backup_json_string() {
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

# backup_json_number <manifest> <key>: первое числовое значение поля.
backup_json_number() {
  local manifest="$1" key="$2"
  awk -v key="$key" '
    index($0, "\"" key "\": ") {
      line = $0
      sub(/^.*": /, "", line)
      sub(/[^0-9].*$/, "", line)
      print line
      exit
    }' "$manifest"
}

# backup_manifest_write <out> <id> <createdAt> <release> <schema> <dbName> <dbSha> \
#   <photosName> <photosSha> <recipes> <weekPlans> <planEntries>
# Все значения ограничены безопасным алфавитом, поэтому JSON собирается printf
# без экранирования произвольного текста.
backup_manifest_write() {
  local out="$1" id="$2" created="$3" release="$4" schema="$5" \
        db_name="$6" db_sha="$7" photos_name="$8" photos_sha="$9" \
        recipes="${10}" week_plans="${11}" plan_entries="${12}"
  {
    printf '{\n'
    printf '  "id": "%s",\n' "$id"
    printf '  "createdAt": "%s",\n' "$created"
    printf '  "release": "%s",\n' "$release"
    printf '  "schema": "%s",\n' "$schema"
    printf '  "dbName": "%s",\n' "$db_name"
    printf '  "dbSha256": "%s",\n' "$db_sha"
    printf '  "photosName": "%s",\n' "$photos_name"
    printf '  "photosSha256": "%s",\n' "$photos_sha"
    printf '  "recipes": %s,\n' "$recipes"
    printf '  "weekPlans": %s,\n' "$week_plans"
    printf '  "planEntries": %s\n' "$plan_entries"
    printf '}\n'
  } > "$out"
}

# backup_state_get <file> <key>: значение или пусто. Файл — данные, не shell-код.
backup_state_get() {
  local file="$1" key="$2"
  [ -f "$file" ] || return 0
  awk -v key="$key" 'index($0, key "=") == 1 { print substr($0, length(key) + 2); exit }' "$file"
}

# backup_state_set <file> <key> <value>: атомарно обновить/добавить KEY=value.
backup_state_set() {
  local file="$1" key="$2" value="$3" tmp
  mkdir -p "$(dirname "$file")"
  tmp="$(mktemp "${file}.XXXXXX")"
  if [ -f "$file" ]; then
    grep -v "^${key}=" "$file" > "$tmp" || true
  fi
  printf '%s=%s\n' "$key" "$value" >> "$tmp"
  mv "$tmp" "$file"
}

# backup_list_sets <remote> [<prefix>]: id полных наборов, по одному в строке.
# Отсутствие каталога (rclone код 3) — это пустой список; прочие ошибки чтения
# хранилища возвращают ошибку, чтобы пустой список не выдавался за успех.
backup_list_sets() {
  local remote="$1" prefix="${2-}" listing code
  set +e
  listing="$(rclone lsf "${remote}${prefix}/complete/" --files-only 2>/dev/null)"
  code=$?
  set -e
  if [ "$code" -ne 0 ] && [ "$code" -ne 3 ]; then
    printf 'Бэкап: не удалось прочитать наборы (%s%s/complete, rclone код %s)\n' \
      "$remote" "$prefix" "$code" >&2
    return 1
  fi
  printf '%s\n' "$listing" | sed '/^$/d' | sort
}

# backup_latest_set <remote> [<prefix>]: последний полный набор или пусто.
backup_latest_set() {
  backup_list_sets "$1" "${2-}" | tail -n1
}

# backup_delete_set <remote> <prefix> <id>: удалить все части набора.
backup_delete_set() {
  local remote="$1" prefix="$2" id="$3"
  backup_set_valid "$id" || {
    printf 'Бэкап: некорректный идентификатор набора из хранилища\n' >&2
    return 1
  }
  rclone deletefile "$remote$prefix/db/$id.sql.gz"
  rclone deletefile "$remote$prefix/photos/$id.tar.gz"
  rclone deletefile "$remote$prefix/manifests/$id.json"
  rclone deletefile "$remote$prefix/complete/$id"
}

# backup_prune <remote> [<prefix>] <keepDays> <protectId>
# Ротация по целым наборам старше cutoff; protectId (последняя проверенная
# drill-точка) не удаляется никогда.
backup_prune() {
  local remote="$1" prefix="$2" keep="$3" protect="$4" cutoff id date listing
  cutoff="$(date -u -d "$((keep - 1)) days ago" +%F)"
  listing="$(backup_list_sets "$remote" "$prefix")" || return 1
  while IFS= read -r id; do
    [ -n "$id" ] || continue
    [ "$id" = "$protect" ] && continue
    backup_set_valid "$id" || continue
    date="${id:0:10}"
    if [[ "$date" < "$cutoff" ]]; then
      backup_delete_set "$remote" "$prefix" "$id"
    fi
  done <<< "$listing"
}
