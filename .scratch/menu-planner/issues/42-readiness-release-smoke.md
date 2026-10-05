# 42: Готовность приложения и проверка публичного пути после деплоя

**What to build:** Оператор отличает живой процесс от готового приложения и получает успешный результат деплоя только после проверки пути, которым пользуется семья, включая маршрутизацию, frontend и сервер.

**Blocked by:** None (can start immediately)

**Status:** resolved (commit ca8470c)

- [x] Существующий дешёвый liveness сохранён; readiness проверяет работоспособность БД с коротким ограниченным временем и не раскрывает секретную конфигурацию.
- [x] Остановка/недоступность БД даёт отрицательную readiness, возвращение БД восстанавливает готовность без обязательного перезапуска процесса.
- [x] Health-проверка контейнера не ограничена фактом открытого TCP-порта; временная неготовность не превращена в бесконечный цикл рестартов.
- [x] Проверка деплоя идёт через край и проверяет readiness, страницу SPA, существующий asset и безопасный серверный запрос; для доменного режима используется внешний HTTPS.
- [x] Сломанный маршрут Caddy, отсутствующий frontend/asset и недоступная БД проваливают проверку, а не объявляются успехом внутреннего backend-запроса.
- [x] Время ожидания явно ограничено и учитывает миграции; при провале сохраняются достаточные состояния/логи для диагностики.
- [x] Smoke проверен на изолированном стеке; пользовательский интерфейс после временного отказа не требует ручного удаления данных/сессии.

## Реализация

- `backend/src/MenuPlanner.Api/Health/` — `IDatabaseReadinessProbe`/`DatabaseReadinessProbe`
  (`CanConnectAsync`), `ReadinessOptions` (`READINESS_TIMEOUT_SECONDS`, default 3 c,
  1–30) и `ReadinessEndpoint` (`/ready`): linked-CTS таймаут, `200/status=ready` или
  `503/status=not-ready`; тело — только статус и имя сервиса, текст исключения
  логируется лишь по типу.
- `Program.cs` — `/health` без изменений (liveness), регистрация probe/options и `/ready`.
- `docker-compose.prod.yml` — healthcheck backend `curl -fsS .../health` вместо
  `/dev/tcp`; `restart: unless-stopped` не рестартит по `unhealthy`.
- `backend/Dockerfile` — `curl` в runtime-образ.
- `deploy/Caddyfile` — маршрут `/ready`.
- `deploy/smoke.sh` — четыре проверки через край (caddy-контейнер): readiness, SPA,
  реально упомянутый asset, безопасный `GET /api/recipes` (ожидается `401`); домен →
  внешний `https://<домен>`; `-T` таймаут; при провале `smoke-diagnostics.log`.
- `deploy/remote-deploy.sh` — ожидание `/ready` с `READY_TIMEOUT_SECONDS` (default 180,
  учитывает миграции) и `deploy-diagnostics.log`; readiness-гейт до записи manifest.
- `deploy/config.sh` — общий `diagnose_to` для сохранения состояний/логов.

## Evidence

- Backend fast: `dotnet test` — **267 passed, 21 skipped** (Postgres-тесты без env).
- Backend Postgres: `scripts/test-postgres.sh` — **24 passed** (включая
  `PostgresReadinessTests.Ready_TracksDatabaseAvailability_WithoutRestart`:
  `DROP DATABASE ... FORCE` → 503, `CREATE DATABASE` → 200 без рестарта процесса).
- Deploy: `python3 -B -m unittest discover -s deploy/tests` — **75 passed**
  (13 новых в `test_smoke.py`, 62 прежних).
- `bash -n deploy/*.sh` и контейнерный Shellcheck `config.sh`/`smoke.sh`/
  `remote-deploy.sh` — чисто.

## Ограничения

Реальный край (Caddy + TLS), Docker daemon и публичный DNS на изолированном стенде не
запускаются: край эмулируется каталогом `EDGE_DIR`, проверяются вызовы `wget` на
публичной границе, коды и отсутствие ложного успеха. Frontend build в worktree не
запускался (нет `node_modules`); исходники фронтенда не менялись, гарантия UI после
временного отказа — на существующем `client.js` (очистка сессии только по `401`).
