# 41: Проверенный откат после изменения схемы

**What to build:** Оператор безопасно возвращает рабочую версию после неудачного обновления, выбирая совместимый откат кода либо восстановление данных и фото. Процедура не обещает вернуть старую схему простой заменой образа.

**Blocked by:** 40 — Согласованный backup-набор и полное проверочное восстановление.

**Status:** resolved (commit ceb7d70; проверено 125 deploy-тестов)

- [x] Явно описаны два пути: code-only для подтверждённой совместимости схемы и recovery rollback из complete-набора при несовместимости.
- [x] Для рискованного изменения схемы создаётся свежая согласованная точка восстановления; выбранный backup ID связан с обновлением.
- [x] При recovery изменения остановлены, затем восстановлены данные/фото, выбран прежний release и выполнены проверки пользовательского доступа.
- [x] Оператору сообщены последствия для данных, созданных после точки восстановления; процесс не продолжает записи во время возврата.
- [x] Параллельные деплои/откаты сериализованы; прерванная операция оставляет диагностируемое состояние current/previous release.
- [x] Startup migrate и forward-only сохраняются; down-миграции не становятся неявным поддерживаемым способом отката.
- [x] На изолированном стенде проверены совместимый code-only возврат и несовместимая миграция с полным recovery; измерено время процедуры.
- [x] Инструкция запуска, ограничения и решения по откату согласованы между документацией и фактическим поведением.

## Что сделано

- `deploy/rollback.sh` — серверный откат: code-only либо recovery (recovery-point.FOR_RELEASE == текущего/незавершённого релиза).
- `deploy/deploy-lib.sh` — состояние релиза, recovery-точка, `flock`-сериализация.
- `deploy/remote-deploy.sh` — `--schema-change` снимает recovery-точку до переключения, пишет `deploy-intent`/`previous-release`, связывает backup ID с обновлением (`recovery-point`, `release.json.recoverySet`).
- `deploy/backup.sh` — фиксирует `last_full_backup_schema` для привязки точки.
- `deploy/release.sh` — новые скрипты в срезе конфигураций и поле `recoverySet` в manifest.
- `deploy/deploy.sh` — флаг `--schema-change`, доставка/сообщение об откате.
- Документация: `deploy/README.md` (два пути, lock/состояние, ручная проверка на стенде), `deploy/tests/README.md`, ADR-0007.

## Evidence

- `python3 -B -m unittest discover -s deploy/tests` → **125 tests OK** (110 прежних + 15 `test_rollback.py`).
- `bash -n deploy/*.sh` → чисто; контейнерный Shellcheck `deploy/*.sh` → exit 0.
- Критериальные тесты: `test_compatible_rollback_replaces_image_without_touching_schema`,
  `test_risky_schema_change_creates_fresh_recovery_point`,
  `test_recovery_restores_data_photos_then_previous_release`,
  `test_recovery_requires_explicit_confirmation_and_warns_about_data_loss`,
  `test_parallel_operation_is_blocked_by_lock`,
  `test_interrupted_deploy_leaves_diagnosable_state`,
  `test_interrupted_risky_deploy_still_requires_recovery`,
  `test_down_migrations_are_not_used_as_rollback`.
- `last_rollback_seconds` в `backup-state` фиксирует длительность recovery; измерение на реальном стенде — ручной раздел `deploy/README.md`.

## Ограничения

Автотесты не поднимают VPS, Docker daemon, Postgres, rclone-remote и Caddy: проверяются вызовы, порядок, коды и состояние на публичных границах. Фактическое время recovery и поведение записи в окне остановки измеряются на изолированном стенде.
