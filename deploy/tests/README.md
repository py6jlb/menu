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
  deploy/remote-deploy.sh deploy/restore-drill.sh deploy/install-backup.sh deploy/smoke.sh \
  deploy/bootstrap.sh
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

## Тикет 43: повторяемый bootstrap без потери доступа

`test_bootstrap.py` проверяет `deploy/bootstrap.sh` без root: скрипт подключается через `source`, системные пути (`SSHD_CONFIG_DIR`, `SYSTEMD_UNIT_DIR`, `SUDOERS_DIR`, `FAIL2BAN_JAIL_DIR`, `SWAPFILE`, `FSTAB`, `OS_RELEASE_FILE`, `DOCKER_KEYRING`, `DOCKER_APT_LIST`, `LEGACY_MARKER`) переопределены на временный каталог, а внешние команды (`sshd`, `systemctl`, `ss`, `docker`, `ufw`, `fail2ban-client`, `install`, `chown`, `visudo`, `runuser`, `getent`, `id`, `adduser`, `usermod`, `curl`, `apt-get`, `dpkg`, `fallocate`, ...) подменены исполняемыми адаптерами в `PATH`. Root, sshd, systemd и Docker daemon не нужны; секреты вымышленные.

| Критерий | Проверка |
|---|---|
| Повторный прогон сохраняет существующие ключи, новый добавляется без дублей | `test_repeatable_runs_preserve_existing_and_avoid_duplicates` (два последовательных `bootstrap`) |
| Дубль не создаётся при том же ключе с другим комментарием; второй ключ добавляется; мусор отвергается | `test_same_key_with_different_comment_is_not_duplicated`, `test_second_key_is_appended_and_invalid_key_is_rejected` |
| Административный путь: NOPASSWD-файл и фактическая проверка `sudo -n`, а не членство в группе | `test_admin_path_uses_nopasswd_and_is_verified`, `test_broken_admin_path_is_rejected` |
| Отказ до применения при неверном `sshd -t`/эффективной конфигурации; старый порт не пропадает | `test_invalid_config_is_rejected_before_apply`, `test_invalid_effective_config_is_rejected` |
| Socket activation Ubuntu 24.04: `ssh.socket.d` c `ListenStream` на новом и старом порту | `test_socket_activation_listens_on_both_ports`, `test_marker_removes_legacy_listen_stream` |
| Порядок «сначала проверка sshd, потом закрытие/применение» | `test_validates_before_closing_and_keeps_marker_after` (индексы вызовов `sshd` < `systemctl restart`) |
| Неверная конфигурация не закрывает рабочий путь; закрытие отложено, пока новый порт не слушается | `test_invalid_config_does_not_close_legacy_port`, `test_refuses_when_new_port_is_not_listening` |
| Docker из официального repo с `signed-by`, без `get.docker.com` | `test_official_repository_with_signed_key_is_used` |
| Compose v2 проверяется явно; отсутствующий инструмент отвергается | `test_compose_v2_is_checked_explicitly`, `test_missing_tool_is_rejected` |
| fail2ban реально банит и снимает пробный адрес (RFC 5737) | `test_fail2ban_real_ban_action_is_verified` |
| Jail настроен на выбранный порт и применяется | `test_fail2ban_jail_targets_the_selected_port` |

### TDD evidence (тикет 43)

1. Ключи: **RED** — старый `read_public_key > authorized_keys` затирал файл при повторном прогоне. После `merge_authorized_key` (уникальность по типу+base64, сохранение чужих строк) — **GREEN**, включая два последовательных `bootstrap`.
2. Проверка sshd: **RED** — конфигурация писалась и сразу `reload`; неверный `sshd -t` не мешал применению. После `validate_sshd_config` (`sshd -t` + `sshd -T`) до firewall/`apply_sshd` — **GREEN**.
3. Порядок закрытия: **RED** — `--close-legacy-port` удалял страховочный конфиг до проверки. После `verify_ssh_listener` + повторной валидации до удаления — **GREEN**; при неверном конфиге или неслушающем порту старый путь остаётся.
4. Socket activation: **RED** — `Port` в `sshd_config` игнорировался бы `ssh.socket` Ubuntu 24.04. После drop-in `ListenStream` и `restart ssh.socket` — **GREEN**.
5. Docker: **RED** — использовался `curl get.docker.com | sh`, Compose v2 явно не проверялся. После официального repo с `signed-by` и проверки `docker compose version` v2 — **GREEN**.
6. fail2ban: **RED** — проверялась только установка. После реального `banip`/`unbanip` пробного `192.0.2.1` — **GREEN**.
7. sudoers: **RED** (`Permission denied` на втором прогоне из-за `chmod 440`) — после атомарной записи через `mktemp` + `visudo` + `mv` — **GREEN**.

Итог: deploy-suite — **80 тестов GREEN** (включая 18 новых `test_bootstrap.py`); `bash -n deploy/*.sh` и контейнерный Shellcheck (включая `bootstrap.sh`) — чисто.

Ограничения: автоматизированный suite не запускает настоящий sshd, systemd, Docker, ufw и fail2ban — проверяются публичные швы под подменёнными адаптерами. Реальный сценарий «два последовательных прогона на чистой временной VM + неверный SSH-конфиг, после которого работают ключевой вход, административный путь и Compose» — **ручная проверка на стенде**, описанная в `deploy/README.md` (разделы «Bootstrap: повторяемость и доступ» и «Аварийный доступ»); в этом окружении VM нет.

## Тикет 42: готовность и проверка публичного пути

`test_smoke.py` дополняет изолированный suite: транспорт `docker` — исполняемый adapter, который эмулирует `image inspect`, `compose config --images`, `compose exec caddy wget`, `compose ps/logs`. Край (Caddy + frontend + backend) эмулируется каталогом `EDGE_DIR`: путь URL отображается в файл, `.status` задаёт код ответа. Реальный Docker daemon, сеть, TLS и БД не запускаются; все секреты вымышленные.

| Критерий | Проверка |
|---|---|
| Дешёвый liveness сохранён; readiness проверяет БД и не раскрывает секреты | backend `HealthReadinessTests` (6 тестов): `/health` при недоступной БД, `/ready` 200/503, тело без строки подключения/текста исключения; `PostgresReadinessTests` на настоящем Postgres |
| Недоступная БД → отрицательная readiness; возвращение — без рестарта | `HealthReadinessTests.Ready_Recovers_WhenDatabaseReturns_WithoutRestart`, `Ready_TimesOut_WithBoundedWait`, `PostgresReadinessTests.Ready_TracksDatabaseAvailability_WithoutRestart` (DROP/CREATE настоящей БД) |
| Healthcheck контейнера не только TCP; временная неготовность не рестартит | `docker-compose.prod.yml` использует `curl .../health`; `restart: unless-stopped`; проверяется структурно (`test_smoke.py` + существующий `test_used_base_images_are_in_pinned_list`) |
| Проверка деплоя через край: readiness, SPA, asset, безопасный серверный запрос; домен — внешний HTTPS | `test_healthy_release_passes_through_edge`, `test_domain_mode_uses_external_https`, `test_http_timeout_is_bounded` |
| Сломанный маршрут Caddy / отсутствующий frontend / asset / недоступная БД проваливают проверку | `test_broken_api_route_is_not_success`, `test_broken_ready_route_is_not_success`, `test_missing_frontend_fails`, `test_missing_asset_fails`, `test_unavailable_database_fails_and_collects_diagnostics` |
| Ограниченное ожидание с учётом миграций; при провале сохраняются состояния/логи | `RemoteDeployTests.test_readiness_gates_release_state` (ждёт `/ready`, default 180 c), `test_unready_database_fails_bounded_with_diagnostics`, `test_invalid_timeout_is_rejected_before_edge`; `smoke-diagnostics.log`/`deploy-diagnostics.log` |
| UI после временного отказа не требует ручного удаления данных/сессии | `FrontendResilienceTests.test_transient_failure_does_not_discard_session` (`clearSession()` только по 401, без `localStorage` в API-клиенте) |

### TDD evidence (тикет 42)

1. Backend readiness: **RED** — namespace `MenuPlanner.Api.Health` и `IDatabaseReadinessProbe` отсутствовали, `HealthReadinessTests` не компилировался. После probe/endpoint с linked-CTS timeout и безопасным ответом — **GREEN**, 6 тестов.
2. Реальная БД: **RED** — недоступность БД эмулировалась только подменённым probe. После `PostgresReadinessTests` с `DROP DATABASE ... FORCE` / `CREATE DATABASE` настоящей БД — **GREEN**, 24 теста Postgres-suite (включая восстановление без рестарта процесса).
3. Smoke через край: **RED** — старый `smoke.sh` делал внутренний `http://backend:8080/health` и использовал необъявленный `TAG` (`unbound variable`). После маршрута `/ready` в Caddy, выбора тега из `IMAGE_TAG` и четырёх проверок через caddy-контейнер — **GREEN**, 13 тестов `test_smoke.py`.
4. Диагностика и таймауты: **RED** — при провале логи/состояния не сохранялись, ожидание не ограничивалось явно. После `smoke-diagnostics.log`/`deploy-diagnostics.log`, `SMOKE_HTTP_TIMEOUT_SECONDS` и `READY_TIMEOUT_SECONDS` — **GREEN**.

Итог: deploy-suite — **75 тестов GREEN** (62 прежних + 13); `bash -n deploy/*.sh` и контейнерный Shellcheck `smoke.sh`/`remote-deploy.sh`/`config.sh` — чисто; backend fast — **267 passed, 20 skipped**, Postgres-suite — **24 passed**. Ограничения: реальный край (Caddy+TLS), Docker daemon и публичный DNS не запускаются; край эмулируется файлами, проверяются вызовы `wget` на публичной границе, коды и отсутствие ложного успеха. Frontend build не запускался в worktree (нет `node_modules`); исходники фронтенда не менялись.

## Тикет 44: безопасный production-запуск и ресурсный бюджет

`test_production_hardening.py` — структурные проверки поставляемых артефактов без Docker daemon: Compose dev/prod, `backend/Dockerfile`, systemd-units, `backup.sh`/`restore-drill.sh`/`bootstrap.sh`. Плюс `SmokeProductionModeTests` в `test_smoke.py` и backend `ProductionConfigurationTests` (WebApplicationFactory). Все секреты вымышленные.

| Критерий | Проверка |
|---|---|
| Production отклоняет HTTP-адрес, отсутствие почты и placeholder-секреты понятной ошибкой | `ProductionConfigurationTests`: `Production_WithoutHttpsPublicUrl_IsRejected`, `Production_WithoutMail_IsRejected`, `Production_WithoutFrom_IsRejected`, `Production_WithKnownPlaceholderJwtSecret_IsRejected_WithoutRevealingValue`, `Factory_ProductionWithoutHttps_FailsFast_WithClearMessage` |
| Проверка не раскрывает значения; длина JWT — не единственная проверка шаблона | `Production_WithKnownPlaceholderJwtSecret_IsRejected_WithoutRevealingValue` (`DoesNotContain(secret)`), `Production_WithPlaceholderDbPassword_IsRejected_WithoutRevealingValue`; детекция известных значений/токенов, не только `Length < 32` |
| Лабораторный HTTP/письма в лог — только явно, не выдаётся за production | `LabMode_ExplicitlyAllowsHttpAndLoggingMail`, `SmokeProductionModeTests.test_http_without_explicit_lab_mode_is_rejected` (запрос не уходит), `test_domain_mode_is_allowed_in_production` |
| Backend от непривилегированного пользователя; PHOTOS_DIR подготовлен; фото можно писать/читать/копировать/удалять | `BackendImageHardeningTests.test_runtime_runs_as_non_root_user`, `test_photos_dir_prepared_for_service_user`; образ запущен с named volume — `uid=1654(app)`, `touch/cp/cat/rm` в `/app/photos` проходят |
| Минимальные capabilities и `no-new-privileges` там, где совместимо; исходящий SMTP доступен | `ProdComposeExternalPortsTests.test_services_carry_no_new_privileges`, `test_backend_and_caddy_have_minimal_capabilities`, `test_backend_can_reach_outbound_smtp`; `DevComposeLoopbackTests.test_dev_backend_is_unprivileged_and_opt_in_lab` |
| Ресурсный бюджет; backup/drill ограничены, учитывают место под архивы/журналы | `ResourceBudgetTests.test_backup_service_bounds_auxiliary_operation`, `test_backup_and_drill_bound_memory_and_require_free_space`, `test_backup_checks_space_before_quiescing_writer`, `test_drill_checks_space_before_downloading_set`; `DocumentationTests` |
| Плановый reboot не пересекается с копированием | `ResourceBudgetTests.test_reboot_does_not_intersect_backup_window` (02:30 + ≤10 мин < 04:30) |
| Dev-порты на loopback; prod публикует только ожидаемые порты | `DevComposeLoopbackTests.test_all_dev_ports_are_bound_to_loopback`, `ProdComposeExternalPortsTests.test_only_caddy_publishes_expected_external_ports` |
| Размеры VPS и ограничения — проверяемые требования, а не обещания | `DocumentationTests.test_readme_documents_budget_and_access`; таблица бюджета в `deploy/README.md` |

### TDD evidence (тикет 44)

1. Production-конфигурация: **RED** — `ProductionConfigurationTests` не компилировался (класса не было). После `ProductionConfiguration.Read` и раннего вызова в `Program.cs` — **GREEN**. Промежуточно **RED**: `ConfigureAppConfiguration` в `ApiFactory` не успевает до старта (валидация читает конфигурацию раньше) — 187 падений; после `UseSetting`-хука и явного `DEPLOYMENT_MODE=lab` в тестовых фабриках — **GREEN**.
2. Placeholder-секреты: **RED** — принимался dev-default `dev-only-secret-change-me-in-production-0123456789abcdef` (проходил только по длине); после известного списка/токен-детекции — **GREEN** без печати значения.
3. Непривилегированный образ: **RED** — runtime работал `root`, `/app/photos` root-owned. Первая правка падала на сборке (`group 'app' already exists`: в `aspnet:10.0` пользователь `app` уже есть); после `USER app` и `chown` — **GREEN**. Образ собран и запущен с named volume: `id` → `uid=1654(app)`; запись/копирование/чтение/удаление в `/app/photos` проходят.
4. Capabilities: **RED** — `docker-compose.prod.yml` не ограничивал capabilities. После `cap_drop: ALL` (backend, caddy), `NET_BIND_SERVICE` (caddy) и `no-new-privileges` — **GREEN** структурно; Compose `config --quiet` валиден.
5. Ресурсный бюджет: **RED** — у backup/drill не было проверки места и лимита памяти, reboot 04:00 пересекался с окном backup 03:30. После `BACKUP_MIN_FREE_MB`/`DRILL_MIN_FREE_MB`, `--memory`, `MemoryMax`/`CPUQuota`, таймера 02:30 и reboot 04:30 — **GREEN**.
6. Loopback: **RED** — dev-порты публиковались на `0.0.0.0`. После `127.0.0.1:` — **GREEN**.
7. Smoke: **RED** — HTTP-smoke проходил без явного lab. После guard в `smoke.sh` — **GREEN**, при production HTTP запрос к краю не отправляется.

Итог: deploy-suite — **110 тестов GREEN** (17 новых: 15 `test_production_hardening.py` + 2 `SmokeProductionModeTests`); backend fast — **286 passed, 21 skipped** (19 новых `ProductionConfigurationTests`); `bash -n deploy/*.sh` и контейнерный Shellcheck `config.sh`/`backup-lib.sh`/`backup.sh`/`restore-drill.sh`/`smoke.sh`/`bootstrap.sh` — чисто. Ограничения: реальные Caddy/TLS, systemd и Docker daemon в suite не запускаются; capabilities и non-root проверены структурно, а непривилегированный образ дополнительно запущен вручную с named volume. Frontend build и Postgres-suite в этом прогоне не запускались; `restore-drill.sh` получил `DEPLOYMENT_MODE=lab` для временного релиза.

## Тикет 41: откат с учётом совместимости схемы

`test_rollback.py` использует то же изолированное окружение (подменённые `docker`/`rclone`, remote — локальный каталог), что и тикеты 39–40, и проверяет серверные `remote-deploy.sh`/`rollback.sh` и библиотеку `deploy/deploy-lib.sh`. Recovery-набор — настоящий complete-набор, снятый `backup.sh` в изолированный remote; БД и фото восстанавливаются на подменённых адаптерах. Реальные VPS, Docker daemon, Postgres и rclone не используются, секреты вымышленные.

| Критерий | Проверка |
|---|---|
| Два явных пути: code-only при совместимой схеме и recovery при несовместимой | `test_compatible_rollback_replaces_image_without_touching_schema`, `test_recovery_restores_data_photos_then_previous_release`, `test_code_only_is_forbidden_when_schema_incompatible` |
| Простая замена образа не возвращает схему; оператор предупреждён | `test_compatible_rollback_replaces_image_without_touching_schema` (нет `rclone copyto`, нет `DROP SCHEMA`, вывод про schema/forward-only) |
| Свежая recovery-точка для `--schema-change`; backup ID привязан к обновлению | `test_risky_schema_change_creates_fresh_recovery_point` (`recovery-point.SET/FOR_RELEASE/FROM_RELEASE/SCHEMA`, `complete/<id>`, `release.json.recoverySet`) |
| Обычный (не рискованный) деплой не оставляет recovery-точку | `test_code_only_deploy_does_not_leave_recovery_point` |
| В recovery: изменения остановлены → данные/фото восстановлены → прежний release → проверка доступа | `test_recovery_restores_data_photos_then_previous_release` (порядок индексов `stop` < `DROP SCHEMA` < восстановление фото < `up`; readiness и `401` на `/api/recipes`) |
| Оператору сообщены последствия для данных после точки; запись не продолжается | `test_recovery_requires_explicit_confirmation_and_warns_about_data_loss` (set id, «потерян», `--yes`, ничего не тронуто до подтверждения) |
| Параллельные деплои/откаты сериализованы | `test_parallel_operation_is_blocked_by_lock` (`flock` на `deploy.lock`, `DEPLOY_LOCK_TIMEOUT=0`, до Docker не доходит) |
| Прерванная операция оставляет диагностируемое состояние current/previous | `test_interrupted_deploy_leaves_diagnosable_state` (`deploy-intent.TAG/MODE`, `current-release` не переключён) |
| Прерванный рискованный деплой всё равно требует recovery | `test_interrupted_risky_deploy_still_requires_recovery` (`deploy-intent.TAG` → привязанный set, `DROP SCHEMA`, цель из `FROM_RELEASE`) |
| Startup migrate/forward-only; down-миграции не откат | `test_down_migrations_are_not_used_as_rollback`, `test_adr_notes_down_migrations_are_not_implicit_rollback` |
| Инструкция и доставка согласованы | `test_readme_documents_both_paths_lock_and_manual_check`, `test_deploy_delivers_rollback_scripts`, `test_release_config_covers_rollback_scripts` |

### TDD evidence (тикет 41)

1. Code-only: **RED** — простого пути не было, `rollback.sh` отсутствовал; после выбора по отсутствию `recovery-point`, предупреждения о forward-only и деплоя прежнего образа — **GREEN**.
2. Recovery-точка: **RED** — `--schema-change` не снимал набор и не связывал ID с релизом; после `backup.sh` до переключения, `recovery-point` и `release.json.recoverySet` — **GREEN**.
3. Полный recovery: **RED** — не было остановки записей, восстановления БД/фото и проверки доступа; после `stop backend` → checksum-проверка набора → `DROP SCHEMA`/дамп → замена фото → прежний release → `/ready`+`401` — **GREEN**. Первый прогон также **RED** на `release_manifest_field` при отсутствующем `release.json` (пустой commit после отката) — после guard — **GREEN**.
4. Подтверждение и последствия: **RED** — recovery шёл без явного согласия; после требования `--yes`/`ROLLBACK_ASSUME_YES` и сообщения о потере данных после точки — **GREEN**.
5. Lock и диагностика: **RED** — параллельная операция могла начаться; после `flock` на `deploy.lock`, `deploy-intent` и `previous-release` только после успешной readiness — **GREEN**.
6. Документация: **RED** — README/ADR описывали откат как «деплой предыдущего sha»; после разделов о code-only/recovery, lock, данных после точки и ручной проверки на стенде — **GREEN**.

Итог: deploy-suite — **125 тестов GREEN** (110 прежних + 15 новых `test_rollback.py`); `bash -n deploy/*.sh` и контейнерный Shellcheck `deploy-lib.sh`/`rollback.sh`/`remote-deploy.sh`/`deploy.sh`/`release.sh` — чисто. Ограничения: реальный VPS, Docker daemon, Postgres, rclone-remote и Caddy не запускаются; проверяются вызовы, порядок, коды и состояния на публичных границах. Фактическое время восстановления и поведение записи в окне остановки — измеряются на изолированном стенде (раздел «Ручная проверка на изолированном стенде» в `deploy/README.md`).

## Тикет 47: доверенный прокси и обход поддельного IP

`ProxyTrustTests` в `test_production_hardening.py` — структурные проверки поставляемых Compose без Docker daemon: backend доверяет ровно одному прокси (Caddy в prod, nginx в dev), а сам прокси получает статический адрес в выделенной подсети. Backend-тесты (`AuthRateLimitTests`, `ForwardedHeaderConfigurationTests`) покрывают middleware отдельно: поддельный `X-Forwarded-For` от недоверенного peer игнорируется, от доверенного края — учитывается.

| Критерий | Проверка |
|---|---|
| prod доверяет только краю Caddy, backend не публикует порт | `ProxyTrustTests.test_prod_trusts_only_the_caddy_edge` (`TRUSTED_PROXY_ADDRESSES: "172.29.0.10"`, `ipv4_address: 172.29.0.10`, `subnet: 172.29.0.0/24`) |
| dev доверяет только nginx-входу; прямой запрос не доверяется | `ProxyTrustTests.test_dev_trusts_only_the_nginx_entry` (`172.28.0.10`) |
| Сырой `X-Forwarded-For` не доверяется; через настроенный край — учитывается | `ForwardedHeaderConfigurationTests.Middleware_IgnoresForwardedFor_FromUntrustedPeer`, `Middleware_HonoursForwardedFor_FromTrustedEdge`; `ClientIpResolverTests.Resolve_IgnoresForgedForwardedForHeader`; `AuthRateLimitFlowTests.Forgot_ForgedForwardedFor_DoesNotBypassIpLimit` |
| Единый `429` + `Retry-After`; UI ограничиваемых операций объясняет ожидание и снимает pending | `AuthRateLimitFlowTests.Login_IsRateLimited_ByEmail_WithRetryAfter`, `Register_IsRateLimited_ByIp`, `Reset_IsRateLimited_ByIp`; фронтенд-хелперы `Retry-After`/`rateLimitMessage` в `useCooldown.js` |
| Память лимитера ограничена; устаревшие окна удаляются; конкуренция безопасна; порядок «IP → операция» | `RateLimiterTests.TryConsume_RejectsNewKeysWhenFull_ButKeepsTrackedOnes`, `TryConsume_AtCapacity_FreesExpiredWindowsForNewKeys`, `RemoveExpired_DropsOnlyStaleWindows`, `TryConsume_IsThreadSafe_UnderContention`, `UniqueEmailStream_KeepsMemoryBounded`; `AuthRateLimitPolicyTests.Check_WhenIpLimited_DoesNotCreateNewIdentityKey` |
| Значения лимитов документированы и валидируются без секретов | `AuthRateLimitOptionsTests.Read_AppliesEnvironmentOverrides`, `Read_WithNonPositiveValue_ThrowsWithoutSecrets`, `Read_WithNonNumericValue_Throws` |
| Одна реплика без Redis; масштабирование — отдельное ограничение | `DocumentationTests.test_readme_documents_rate_limit_proxy_trust_and_single_replica` |

Итог: deploy-suite прибавил 3 структурных теста (`ProxyTrustTests` × 2 + расширенный `DocumentationTests`); backend fast — новые `RateLimiterTests`/`AuthRateLimitTests`/`ForwardedHeaderConfigurationTests`/`AuthRateLimitOptionsTests`. Ограничения: реальный Caddy/TLS и Docker daemon не запускаются — доверие проверяется на уровне middleware и поставляемых Compose; фронтенд проверяется сборкой `vite build`, без e2e-браузера.
