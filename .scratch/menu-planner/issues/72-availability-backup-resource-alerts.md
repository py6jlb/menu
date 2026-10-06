# 72: Обнаружение недоступности, старых бэкапов и исчерпания ресурсов

**What to build:** Оператор получает проверяемое уведомление при недоступности публичного приложения, просроченной полной резервной копии или опасной нехватке ресурсов и понимает, когда проблема устранена.

**Blocked by:** 40 — Согласованный backup-набор и полное проверочное восстановление; 42 — Готовность приложения и проверка публичного пути после деплоя.

**Status:** resolved (commit 9dcd5c3)

- [x] Проверяется внешний пользовательский путь и readiness, а не только внутренний порт; внешний контроль способен сообщить о недоступности всего VPS. — `alert.sh` делает `curl` на `ALERT_PUBLIC_URL/ready` (200 + `status=ready`) и `/`; полный отказ VPS ловит внешний dead-man switch по `ALERT_HEARTBEAT_URL`.
- [x] Возраст копии определяется по последнему complete-набору; неполный upload/неуспешный backup не обновляет время успеха. — `check_backup` использует `backup_latest_set` (`complete/`), `test_incomplete_upload_does_not_count_as_backup`.
- [x] Проверяются свободное место и опасное давление ресурсов с настраиваемыми порогами; ограничения и задержки предотвращают ложную тревогу от краткого всплеска. — диск/память/load, `ALERT_BREACH_SAMPLES` подряд проб; `test_single_spike_does_not_alarm`, `test_sustained_disk_pressure_alerts`, `test_memory_pressure_...`, `test_load_pressure_...`.
- [x] Канал оповещения явно настраивается оператором, секреты не логируются; доставка контролируется и допускает проверочное уведомление без реальной аварии. — `ALERT_WEBHOOK_URL`, `curl --config` (не в argv/логах), `alert.sh --test`/`MONITOR_TEST=1`; `test_missing_channel_...`, `test_secret_channel_...`, `test_delivery_failure_...`.
- [x] Повторные одинаковые события дедуплицируются/ограничиваются, переход к восстановлению даёт отдельное понятное сообщение. — окно `ALERT_DEDUP_SECONDS`, сообщение `RECOVERED`; `test_duplicate_event_...`, `test_recovery_sends_separate_message_...`.
- [x] Уведомление содержит время, установку/release при наличии, тип проблемы и следующий диагностический шаг, не раскрывая пользовательские данные. — `alert_alert_message`; `test_message_contains_time_install_release_problem_and_next_step`.
- [x] Отдельно описано ограничение локальной проверки ресурсов при полном отказе VPS; локальный таймер не выдаётся за внешний мониторинг. — раздел README «Обнаружение недоступности…»; `test_readme_documents_limits_channel_and_verification`.
- [x] На стенде проверены остановка приложения/БД, старая complete-копия, неполный новый набор, недостаток диска и восстановление; средства проверки не заполняют рабочий диск. — процедура в README; изолированно `test_public_path_and_readiness_failure_alerts`, `test_stale_complete_set_alerts_with_age`, `test_incomplete_upload_...`, `test_sustained_disk_pressure_alerts`, `test_recovery_sends_separate_message_...`; используются `df`/`curl` и удаляемые временные файлы.

## Реализация

- `deploy/alert-lib.sh` — доверенная библиотека: URL-валидация, хост без секрета, epoch complete-набора, JSON-escape, тексты ALERT/RECOVERED/TEST, чтение `df`/meminfo/loadavg.
- `deploy/alert.sh` — проверка публичного пути и readiness, возраста complete-набора, устойчивого давления ресурсов; дедупликация и сообщение восстановления; отправка в канал (URL в временном `curl --config`), heartbeat; режим `--test`.
- `deploy/systemd/menu-monitor.service`/`.timer` — запуск каждые 5 минут, ограничения `MemoryMax`/`CPUQuota`/`Nice`.
- `deploy/install-monitor.sh` — идемпотентная установка units, проверка канала и remote; `MONITOR_TEST=1` шлёт проверочное уведомление.
- `deploy/config.sh`, `deploy/release.sh`, `deploy/deploy.sh`, `deploy/server.conf.example`, `deploy/README.md` — ключи, хэши конфигурации, доставка units, документация.

## Evidence

- `python3 -B -m unittest discover -s deploy/tests` — **165 tests OK** (23 новых `test_alerting.py`).
- `bash -n deploy/*.sh` — чисто; контейнерный Shellcheck (включая `alert.sh`, `alert-lib.sh`, `install-monitor.sh`) — exit 0.

## Ограничения

Реальные VPS-отказы, object storage, systemd/таймеры и сторонний dead-man switch в suite не запускаются: проверяются вызовы на публичных границах, коды, состояния и тексты; сценарии стенда описаны в README. Backend/frontend не менялись, поэтому их сборка/тесты не запускались.
