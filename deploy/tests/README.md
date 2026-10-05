# Проверки конфигурации deploy-скриптов

Публичные швы: `config_load <optional-file> <local|server>` из доверенной библиотеки, реальные entrypoints до внешних действий и runtime-adapter `DatabaseConnection.Resolve`. Приватные детали парсера не тестируются. Тесты используют временные каталоги и вымышленные секреты. Игнорирование bytecache проверяется публичной командой `git check-ignore`.

## Запуск и будущий CI

Для deploy-suite нужны Linux, Bash 4.3+, Python 3 (stdlib), Git и Docker CLI с Compose plugin (`config --format json` и `config --environment`). На разработке проверено с Compose **v5.5.1**. Docker daemon, доступ в registry, сервер и rclone credentials не нужны. Если Compose отсутствует, suite падает, а не пропускает проверку совместимости.

Из корня репозитория:

```bash
python3 -B -m unittest discover -s deploy/tests -v
for script in deploy/*.sh; do bash -n "$script" || exit; done
if command -v shellcheck >/dev/null 2>&1; then shellcheck deploy/*.sh; fi
```

В CI установи Python/Bash/Git/Compose plugin и запускай эти команды отдельным deploy-specific job. Shellcheck, если установлен в CI, должен проходить обязательно. Python-зависимостей и дополнительных библиотек нет. `-B` предотвращает bytecache при проверках; `__pycache__/` и `*.py[cod]` также игнорируются Git. Полный backend suite и frontend build — отдельные проверки основного агента/CI.

Контейнерный Shellcheck для изменённых deploy-скриптов, если инструмента нет на хосте:

```bash
docker run --rm --user "$(id -u):$(id -g)" -v "$PWD":/app:ro -w /app \
  koalaman/shellcheck:stable deploy/config.sh deploy/release.sh deploy/backup-lib.sh \
  deploy/backup.sh deploy/build-push.sh deploy/deploy.sh deploy/compose.sh \
  deploy/remote-deploy.sh deploy/restore-drill.sh deploy/install-backup.sh deploy/smoke.sh
```

Targeted runtime-тесты требуют Docker daemon и SDK-контейнер (SDK на хост не устанавливается):

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e DOTNET_CLI_HOME=/tmp/dotnet-home -e NUGET_PACKAGES=/tmp/nuget \
  -v "$PWD":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test --filter FullyQualifiedName~DatabaseConnectionTests
```

В `DatabaseConnectionTests` конфигурация передаётся через публичный `Resolve`, результат повторно читается `NpgsqlConnectionStringBuilder`. Проверяются точные literal-значения пароля/имени пользователя/базы, structured/legacy precedence, defaults и явно пустые значения. Подключения к БД, миграций и приложения нет; все значения вымышленные. SDK restore может потребовать доступ к NuGet.

Legacy-проверка копирует только versioned `compose.sh`/`config.sh` в временный каталог, вызывает настоящий entrypoint с подменённой внешней командой Docker и перехватывает её `ConnectionStrings__Default`. Строка передаётся `Resolve` без `DB_HOST` (тот же legacy-путь, что у `cefd943`) и reparses Npgsql. Это проверка сериализации через публичные границы, а не тест приватного helper. Отдельная проверка настоящего Compose подтверждает, что backend получает ровно эту derived env-строку без повторной interpolation. Зависимость SDK существует только у tests, не у deploy на VPS.

В Python deploy-suite `docker`, `ssh`, `scp`, `rclone`, `npm` подменены исполняемыми transport-adapters в `PATH`. Проверка доставки эмулирует сервер локальным временным каталогом и запускает доставленный серверный entrypoint, останавливаясь на подменённом `docker`. Единственный настоящий Docker CLI вызов внутри этого suite — **`compose config`**, без pull/up/run/exec, сети, отправки писем или выгрузки бэкапов. Секретные prod-файлы не читаются.

## Доказательства критериев тикета 36

| Критерий / пример | Проверка |
|---|---|
| Штатные local/server templates, `SMTP_FROM_NAME=Меню для домохозяек` | `test_shipped_templates_and_format_edges`, `test_real_compose_with_template_and_env_only` |
| Пробелы (включая края), `$HOME`, `${SMTP_HOST}`, `$$`, `#`, обе кавычки, `\`, `$(touch marker)`, backticks — буквально, marker не создан | `test_template_and_literal_specials`, `test_real_compose_keeps_literals_and_ignores_dotenv` |
| Optional файл отсутствует, env-only работает | `test_optional_file_environment_precedence_and_export`, `test_env_only_entrypoints_reach_external_boundary` для всех шести entrypoints |
| Env > файл, включая явно пустое значение; значения экспортированы | `test_optional_file_environment_precedence_and_export`, `test_file_config_reaches_each_entrypoint_and_empty_env_wins`, настоящий Compose с пустыми `SMTP_FROM_NAME`, `SMTP_PORT`, `JWT_ISSUER` |
| Неизвестные/чужие ключи и `PATH`/`BASH_ENV`/`SHELLOPTS`, ошибочная строка, дубликат, CRLF/таб/NUL → nonzero без секрета | `test_malformed_config_is_nonzero_without_secrets`, `test_malformed_files_and_arithmetic_payload_fail_before_external_actions` |
| Каждый обязательный ключ отсутствует или явно пуст → ошибка с именем ключа до docker/scp/ssh/rclone; обязательный тег отсутствует | `test_missing_required_values_before_external_actions`, `test_missing_tag_and_explicitly_empty_build_tag_fail_before_external_actions` |
| Сроки хранения с command-looking arithmetic payload → отказ до внешних действий | `test_malformed_files_and_arithmetic_payload_fail_before_external_actions` |
| SSH-настройки с command-looking payload → отказ до транспорта | `test_ssh_control_payload_is_rejected_before_transport` |
| Общие границы Docker tag: invalid-leading `-`/`.`, 129 символов и пустой тег → отказ до транспорта; valid 128 символов проходит | `test_docker_tag_boundaries_before_external_actions` для build/local/remote/compose/backup |
| Серверные literal values в raw Compose env и модели без повторной interpolation; чужие `.env` и `COMPOSE_ENV_FILES` игнорируются; shell-free DB healthcheck | `test_real_compose_keeps_literals_and_ignores_dotenv` (реальный Compose) |
| Prod передаёт отдельные `DB_*`, builder сохраняет literal `"abc"`, крайние пробелы, `;`, `\`, `$`, `#` после Npgsql reparsing; structured выше legacy/default, dev/EF legacy overrides сохранены | `test_real_compose_keeps_literals_and_ignores_dotenv`, `DatabaseConnectionTests` |
| `DB_PORT`: missing → 5432; empty/invalid/0/-1/65536 → понятная ошибка без значения/inner exception; valid 1/6543/65535 сохраняется | `StructuredConfiguration_RejectsInvalidPort_WithoutPrintingValue`, `StructuredConfiguration_PreservesValidPort`, проверка defaults |
| Rollback на backend без `DB_*`: legacy-строка доставляется Compose, обычный пароль и literal-specials сохраняются в Npgsql | `LegacyComposeEnvironment_PreservesLiterals_ForResolverWithoutStructuredSupport`, оба `test_real_compose*` |
| Bytecache не попадает в Git | `test_python_bytecache_is_ignored` |
| Local/server разделены; доставлены библиотека, remote/backup/restore/compose, серверный файл не перезаписан, локальный секрет не доставлен; тег-аргумент выше серверного файла | `test_local_deploy_delivers_server_library_but_not_secrets` |

Compose `config` экранирует все `$` как `$$` при сериализации (публичное поведение `cmd/compose/config.go: runConfig`). При сравнении модели снимается только это транспортное экранирование; отдельно сверяется `config --environment`, который отдаёт literal env без такого экранирования. Это позволяет отличить формат вывода от реального повторного раскрытия переменных.

## TDD evidence

1. Первый тест воспроизвёл старое `source`: **RED**, `HOME: unbound variable`, nonzero на строке с пробелами/подстановкой. После минимального `config_load`: **GREEN**, 1 тест.
2. Проверки malformed config: **RED** на NUL/CR/таб/дубликате, затем **GREEN**, 3 теста.
3. Шов entrypoints: **RED** на недостающих параметрах backup до Docker и отсутствующих новых entrypoints, затем **GREEN**, 5 тестов.
4. Реальный Compose: после учёта документированного транспортного `$$` — **RED**, пустой env превращался в default (`Меню для домохозяек` вместо пустой строки). После `${VAR-default}` — **GREEN**, 6 тестов.
5. Arithmetic payload и DB healthcheck: **RED**, payload допускался к внешней границе, DB-конфигурация попадала в `CMD-SHELL`; после валидации сроков и `CMD` — **GREEN**, 7 тестов.
6. Разделённые шаблоны и изолированная доставка: **RED**, отсутствовал серверный шаблон; после разделения — **GREEN**, 9 тестов.

Дополнительные проверки всех file-config entrypoints, пустого env, SSH-настроек, тегов, настоящего Compose с шаблоном и env-only: первоначальный suite — **13 тестов GREEN** (с матрицами subtests).

### Исправления после первого code-review

1. Docker-tag table: **RED**, 13 subtests (invalid-leading и 129 символов дошли до транспорта; Compose/backup принимали и пустой тег). После общего validator — **GREEN**, включая длину 128 и прежние missing/empty проверки.
2. Runtime-адаптер: **RED**, 5 literal-кейсов; исходная строка `Password="abc"` возвращала пароль `abc` вместо `"abc"`. После назначения свойств `NpgsqlConnectionStringBuilder` — **GREEN**, 5 кейсов. Добавлены проверки legacy/default precedence и optional/empty values: targeted backend suite — **12 тестов**. Npgsql нормализует пустые поля в `null`; это учтено в проверке отсутствия подмены defaults.
3. Реальный Compose: **RED**, оставался `ConnectionStrings__Default`, отсутствовал `DB_PASSWORD`; после перехода на отдельные `DB_*` — **GREEN**.
4. Gitignore: **RED**, отдельные `.pyc/.pyo` не игнорировались (для `__pycache__/` правило уже было). После общего `*.py[cod]` — **GREEN**.

На этом этапе Python deploy-suite — **15 тестов GREEN** с матрицами subtests; backend targeted suite — **12 тестов GREEN**.

### Исправления после follow-up review и Shellcheck

1. `DB_PORT`: **RED**, 5 случаев — empty/65536 не отвергались, другие ошибочные значения попадали в стандартные исключения с value. После явного parsing и диапазона — **GREEN**, 20 targeted backend-тестов (missing default и valid-порты также проверены).
2. Rollback: **RED**, 6 legacy-кейсов через публичный entrypoint сохраняли старый `localhost/menu` вместо effective-конфигурации; настоящий Compose не передавал `ConnectionStrings__Default`. После Bash ADO.NET-сериализации и восстановления legacy-ключа в prod YAML — **GREEN**, все 26 targeted backend-тестов и Compose checks. Проверяются обычный пароль, двойные/одинарные кавычки, крайние пробелы, `;`, `\`, `$`, `#` без реальной БД.
3. Контейнерный Shellcheck: **RED**, SC1007 и SC2086; после явного `COMPOSE_ENV_FILES=''` и кавычек вокруг tar-аргумента — **GREEN**, exit 0 на всех семи изменённых deploy-скриптах. Backup-семантика не менялась.

Текущий итог на момент тикета 36: Python deploy-suite — **15 тестов GREEN**, backend targeted suite — **26 тестов GREEN**; bash syntax и контейнерный Shellcheck проходят. Актуальное число тестов (с тикетом 38) — в разделе ниже.

Ограничения проверки: стек не запускается; фактическая доступность/аутентификация Postgres, валидность SMTP/JWT, реальный SSH и установленный на VPS Compose не проверяются. Literal-гарантия строки подключения проверена reparsing Npgsql в runtime-adapter. Тикет не меняет поведение восстановления без `$1`, атомарность backup, release pinning, TLS или readiness.

## Тикет 38: воспроизводимый релиз

`test_release.py` проверяет доверенную библиотеку `deploy/release.sh` и сборку релиза без сети и Docker daemon:

- чистота checkout (`release_require_clean_tree`): tracked-правка и untracked-файл отклоняются;
- `release_resolve_commit`: неизвестная ревизия отвергается;
- manifest: хэши конфигурации, рендер JSON и сверка `release_verify_config` (изменённый файл ломает проверку);
- pinned release: `current-release` даёт `IMAGE_TAG`, уступая env и `server.conf`;
- структура: Node LTS + `npm ci`, отсутствие `:latest` для базовых образов, 404 для отсутствующего asset и SPA-fallback;
- сборка: dirty checkout отклоняется до внешних действий; без `--publish` нет `docker push`; с `--publish` push и registry-дайджесты попадают в manifest.

### TDD evidence (тикет 38)

1. Dirty-check: **RED** — `release_require_clean_tree` отсутствовала, build-push собирал с изменениями; после allowlist-проверки **GREEN**.
2. Manifest: **RED** — `release_manifest_field` падал на пути с `/` (sed expression), сверка конфигурации не работала; после awk-разбора **GREEN**.
3. Entrypoints: **RED** — build-push/deploy игнорировали чистоту дерева, тестовый checkout считался грязным из-за `bin/`; после `release_require_clean_tree` и gitignore-фикстуры **GREEN**.
4. Publish-гейт: **RED** — build-push всегда пушил; после `--publish` **GREEN**, manifest фиксирует registry-дайджесты.
5. Структурные гарантии: **RED** — `node:18` и `npm install`, отсутствие 404-asset; после `node:24-alpine` + `npm ci` и nginx-правила **GREEN**.

Итог: deploy-suite — **25 тестов GREEN**; контейнерный Shellcheck изменённых скриптов — чисто; frontend собирается `npm ci` в `node:24-alpine` без изменения lockfile. Ограничения: реальный registry, серверный `remote-deploy.sh`/`smoke.sh` и повторный деплой на стенде не запускаются — проверены изолированно (manifest, приоритет тега, структура).

## Тикет 39: работающая установка резервного копирования

`test_backup_restore.py` проверяет `backup.sh`, `restore-drill.sh` и `install-backup.sh` через transport-adapters: `docker`, `rclone` и `systemctl` подменены, rclone-remote эмулируется локальным каталогом (`FAKE_REMOTE_DIR`). Реальный Docker daemon, сеть, systemd и объектное хранилище не используются; все значения вымышленные.

| Критерий | Проверка |
|---|---|
| Доставленные инструменты и units; установка без checkout | `test_readme_describes_delivered_install_without_checkout`, `test_backup_drill_scripts_are_delivered`, `test_installs_renders_and_enables_units` |
| Пользователь службы, права, расположение rclone и доступ к remote | `test_unconfigured_remote_is_rejected`, `test_missing_rclone_is_rejected`, `test_unreadable_backup_script_is_rejected`, `test_installs_renders_and_enables_units` (рендер `User=`, `WorkingDirectory=`, `ExecStart=`) |
| Timer включён, следующий запуск виден | `test_installs_renders_and_enables_units` (`daemon-reload`, `enable --now menu-backup.timer`) |
| Сбой dump/архивации/хранилища/upload → ненулевой код, нет «Готово» | `test_dump_failure_...`, `test_archive_failure_...`, `test_missing_backend_container_...`, `test_missing_photos_volume_...`, `test_upload_failure_...`, `test_upload_without_remote_object_...`, `test_storage_listing_error_...` |
| Запуск восстановления без аргумента; отсутствие `$1` не прерывает | `test_runs_without_argument_and_verifies_remote_set`, `test_explicit_empty_argument_is_treated_as_missing` |
| SQL-восстановление останавливается при ошибке; БД/роль совпадают с дампом; нет фото → не успех | `test_sql_error_aborts`, `test_configured_database_identity_is_used`, `test_missing_photos_archive_is_not_success`, `test_corrupt_dump_is_rejected_before_container_start` |
| Изолированная временная БД, уникальное имя, очистка контейнера и volume | `test_runs_without_argument_and_verifies_remote_set` (`--network none`, суффикс имени, `rm -f -v`) |
| Успешная доставка объектов | `test_success_reaches_remote_and_declares_success` |

### TDD evidence (тикет 39)

1. Install-шов: **RED** — `install-backup.sh` отсутствовал, доставка не покрыта; после рендера units, проверки remote от имени службы и `enable --now` — **GREEN**.
2. Восстановление без `$1`: **RED** — `set -u` прерывал запуск, drill требовал пустой аргумент; после `${1-}` и `ON_ERROR_STOP` — **GREEN**.
3. Отказы backup: **RED** — отсутствие объекта после upload и ошибка чтения хранилища давали «Готово»; после `verify_upload` и разбора кода rclone (терпим только «каталог не найден») — **GREEN**.
4. Изоляция и очистка drill: **RED** — фиксированное имя контейнера без `--network none` и без снятия volume; после уникального суффикса, `--network none` и `rm -f -v` — **GREEN**.
5. Обязательный архив фото: **RED** — отсутствие фото давало успешный drill; после обязательной проверки архива — **GREEN**.

Ограничения: реальные Postgres, systemd, rclone-remote и `pg_dump`/`pg_restore` не запускаются. Проверяются вызовы на публичных границах скриптов, коды завершения и отсутствие ложного успеха. Согласование БД/фото в одну точку и полный запуск восстановленного приложения — тикет 40.

## Тикет 40: согласованный backup-набор и полное восстановление

`test_complete_backup.py` использует то же изолированное окружение, что и тикет 39 (подменённые `docker`/`rclone`, remote — локальный каталог), и проверяет целый complete-набор: уникальность, публикацию маркера, ротацию, состояние и drill. Дополнительно в `test_backup_restore.py` обновлены адаптеры под manifest и complete-маркеры (в частности, `menu_compose stop/start backend`, чтение схемы/контрольных объёмов и запуск закреплённого релиза).

| Критерий | Проверка |
|---|---|
| Уникальный набор за запуск, не перезапись в тот же день | `test_each_run_creates_unique_set_and_manifest` |
| Manifest: время, release, схема, контрольные суммы БД/фото | `test_each_run_creates_unique_set_and_manifest` |
| Complete-отметка только после доставки всех частей | `test_complete_marker_only_after_all_parts_delivered`, `test_interrupted_run_does_not_create_false_complete` |
| Выбор последней копии и ротация по целым complete-наборам | `test_selection_ignores_incomplete_latest`, `test_rotation_handles_whole_sets_and_protects_verified` |
| Ротация сохраняет последнюю проверенную точку | `test_rotation_handles_whole_sets_and_protects_verified`, `test_drill_failure_keeps_last_verified_point_protected` |
| Согласование записей БД и удаления/замены фото (короткое окно) | `test_snapshot_quiesces_writer_around_copy` |
| Полный недельный набор (копия целого набора) | `test_sunday_creates_complete_weekly_set` |
| Drill: checksum, БД+фото в изоляции, история миграций, контрольные рецепты/планы | `test_drill_rejects_checksum_mismatch_before_containers`, `test_drill_rejects_incompatible_schema`, `test_drill_rejects_mismatched_reference_counts`, `test_runs_without_argument_and_verifies_remote_set` |
| Каждый путь фото в восстановленной БД разрешается | `test_drill_resolves_every_photo_path` |
| Несовместимый набор / отсутствующий архив / повреждённый SQL → ошибка | `test_drill_rejects_incompatible_schema`, `test_missing_photos_archive_is_not_success`, `test_corrupt_dump_is_rejected_before_container_start` |
| Запуск закреплённого релиза против восстановленной БД | `test_drill_runs_pinned_release_and_records_result` |
| Сохранение времени последнего backup и результата drill | `test_backup_state_records_last_full_backup`, `test_drill_runs_pinned_release_and_records_result`, `test_drill_failure_is_recorded_in_state` |
| Проверки не изменяют production-данные | drill работает только с временными контейнерами/сетью; `test_runs_without_argument_and_verifies_remote_set` проверяет изоляцию и удаление |

### TDD evidence (тикет 40)

1. Уникальность набора: **RED** — старый `STAMP=$(date +%F)` перезаписывал набор того же дня (второй запуск не давал второй complete-маркер); после `backup_set_id` с наносекундами — **GREEN**.
2. Complete-отметка: **RED** — «Готово» печаталось после `verify_upload`, но признака пригодности точки не было; после публикации `complete/<id>` только после проверки всех частей — **GREEN**, сбой второго upload не создаёт ложный complete.
3. Выбор точки: **RED** — drill брал «последний дамп + последний архив фото» независимо; после выбора по `complete/<id>` (незавершённый набор игнорируется) — **GREEN**.
4. Ротация: **RED** — чистка шла по каждому каталогу отдельно, могла оставить сироту и удалить проверенную точку; после `backup_prune` по целым наборам с защитой `last_drill_ok_set` (провал drill защиту не снимает) — **GREEN**.
5. Согласование БД/фото: **RED** — при замене/удалении фото во время копирования БД и архив могли разойтись; после quiesce backend вокруг снятия — **GREEN** (`test_snapshot_quiesces_writer_around_copy`).
6. Drill: **RED** — не сверялись sha256, схема, контрольные рецепты/планы и пути фото, не запускался релиз; после проверок и запуска `menu-backend:<release>` — **GREEN**.
7. Состояние и RPO/RTO: **RED** — результат drill нигде не сохранялся; после `backup-state` (`last_full_backup*`, `last_drill_*`, `last_drill_ok_set`, `last_drill_seconds`) — **GREEN**; RPO/RTO описаны как измеряемые, без гарантированных чисел.

Итог: deploy-suite — **62 теста GREEN**; `bash -n deploy/*.sh` и контейнерный Shellcheck (включая `backup-lib.sh`) — чисто. Ограничения: реальные Postgres/rclone-remote/Docker daemon, S3-семантика, фактическое время восстановления и поведение Caddy в окне quiesce не запускаются; проверяются вызовы на публичных границах, коды и состояния. Запуск закреплённого релиза в изоляции проверен на уровне вызова `docker run` и health-пробы.
