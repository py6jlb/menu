# 44: Безопасный production-запуск и ограниченный ресурсный бюджет

**What to build:** Production-установка отклоняет известные небезопасные настройки и работает в измеренном ресурсном бюджете; обычное резервное копирование не приводит к общему отказу приложения или исчерпанию диска.

**Blocked by:** None (can start immediately)

**Status:** resolved (commit 58ff9fc)

- [x] Перед публичной работой с реальными пользователями обязательны HTTPS, настроенная почта и отсутствие известных placeholder-секретов; лабораторный HTTP допускается только явно и не выдаётся за готовый production.
- [x] Проверки конфигурации дают понятную причину отказа и не раскрывают значения секретов; длина JWT-секрета не является единственной проверкой известных шаблонных значений.
- [x] Backend работает от непривилегированного пользователя с подготовленными правами на фото; проверены загрузка, чтение, копирование и удаление фото.
- [x] Минимальные capabilities и no-new-privileges применяются там, где это совместимо с рабочими контейнерами; исходящий SMTP остаётся доступен.
- [x] Зафиксирован ресурсный бюджет приложения, ОС и вспомогательных операций; backup/drill имеют ограничения, учитывается временное место под архивы и журналы.
- [x] Плановый reboot не пересекается с копированием; проверен совместный запуск обычной нагрузки и вспомогательной операции без OOM и исчерпания диска.
- [x] Dev-порты БД/backend/frontend привязаны к loopback; production сохраняет публикацию только ожидаемых внешних портов.
- [x] Измерения и сценарии запуска проверены на стенде; размеры VPS и ограничения документированы как проверяемые требования, а не универсальные значения.

## Реализация

- `backend/.../Configuration/ProductionConfiguration.cs`, `DeploymentOptions.cs`: режим `DEPLOYMENT_MODE` (`production` по умолчанию, `lab` — явно). Production требует `PUBLIC_BASE_URL` с `https://`, непустые `SMTP_HOST`/`SMTP_FROM`, случайные не-шаблонные `JWT_SECRET` (≥32), `DB_PASSWORD` (≥12) и `SMTP_PASSWORD`. Шаблон детектируется по known-списку и токенам; значения секретов в ошибке не печатаются. Вызов — ранний, в `Program.cs`.
- `backend/Dockerfile`: runtime работает от встроенного непривилегированного `app` (uid 1654); `/app/photos` создан и принадлежит ему; `COPY --chown`.
- `docker-compose.prod.yml`: `no-new-privileges` всем рабочим сервисам, `cap_drop: ALL` для backend и caddy (`NET_BIND_SERVICE` только caddy), `DEPLOYMENT_MODE`/`PUBLIC_BASE_URL` у backend. `docker-compose.yml`: dev-порты на `127.0.0.1`, `DEPLOYMENT_MODE=lab`.
- `deploy/backup-lib.sh`, `backup.sh`, `restore-drill.sh`: проверка свободного места (`BACKUP_MIN_FREE_MB`/`DRILL_MIN_FREE_MB`) до операции и лимит памяти (`--memory`) на вспомогательные контейнеры; release в drill стартует с `DEPLOYMENT_MODE=lab`.
- `deploy/systemd/menu-backup.service`: `MemoryMax=1G`, `CPUQuota=50%`, `Nice=10`, `IOSchedulingClass=idle`, `After=network-online.target`. `menu-backup.timer` — 02:30 + `RandomizedDelaySec=10m`; `bootstrap.sh` переносит авто-reboot на 04:30.
- `deploy/smoke.sh`: HTTP без домена проходит только при `DEPLOYMENT_MODE=lab`.
- Документация: `deploy/README.md` (production-конфигурация, пользователь/capabilities, ресурсный бюджет, порты), `docs/adr/0006`, `deploy/tests/README.md`.

## Evidence

- Backend fast: `dotnet test` — **286 passed, 21 skipped** (19 новых `ProductionConfigurationTests`: HTTPS/почта/placeholder без раскрытия, lab, production-старт).
- Backend Postgres: `scripts/test-postgres.sh` — **24 passed**.
- Deploy python: `python3 -B -m unittest discover -s deploy/tests -v` — **110 GREEN** (15 новых `test_production_hardening.py` + 2 `SmokeProductionModeTests`).
- `bash -n deploy/*.sh` — чисто; `koalaman/shellcheck:stable` для `config.sh`, `backup-lib.sh`, `backup.sh`, `restore-drill.sh`, `smoke.sh`, `bootstrap.sh` — exit 0.
- Compose: `docker compose -f docker-compose.yml config --quiet` и prod `config --quiet` — валидны.
- Образ: `docker build ./backend` успешен; запуск с named volume даёт `uid=1654(app)` и проходят запись/копирование/чтение/удаление в `/app/photos`.

## Ограничения

- Реальные Caddy/TLS, systemd, автоматический reboot и Docker daemon в suite не запускаются: capabilities и non-root проверены структурно, непривилегированный образ дополнительно запущен вручную с named volume.
- Размеры VPS (≥2 vCPU, ≥2 ГБ RAM + 2 ГБ swap, ≥40 ГБ диска) — проверяемое требование; фактические измерения (`docker stats`, `df`, `last_drill_seconds`, `last_full_backup_at`) подтверждаются на стенде.
- Frontend не менялся; Postgres-гарантии покрыты отдельным `scripts/test-postgres.sh`.
