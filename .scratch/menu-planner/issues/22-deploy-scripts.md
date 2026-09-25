# 22: Деплой-скрипты и первый деплой

**What to build:** Ручной пайплайн деплоя. `deploy/build-push.sh` — перед публикацией прогоняет `dotnet test` (в SDK-контейнере) и `npm run build`, затем собирает образы `backend`/`frontend` и пушит в Docker Hub тегами `:<git-sha>` + `:latest`. `deploy/deploy.sh <sha>` — через scp кладёт `docker-compose.prod.yml` и `Caddyfile` в `/opt/menu` на VPS, подтягивает образы с прибитым sha (`compose pull && up -d`, не `restart`), проверяет `/health`; откат — `deploy.sh <предыдущий-sha>`. `deploy/README.md` — runbook (переменные окружения, деплой, откат).

**Blocked by:** 21 (Prod-конфигурация — compose, Caddy, сеть, ForwardedHeaders)

**Status:** ready-for-agent

- [ ] `deploy/build-push.sh`: гейт `dotnet test` + `npm run build`, сборка и push образов
- [ ] Теги `:<git-sha>` и `:latest`
- [ ] `deploy/deploy.sh <sha>`: scp compose/Caddyfile, `compose pull && up -d` с прибитым sha
- [ ] Проверка `/health` после деплоя
- [ ] Откат через `deploy.sh <предыдущий-sha>`
- [ ] `deploy/README.md` (env, деплой, откат)
- [ ] Скрипты проверены `bash -n`
- [ ] Первый деплой на VPS выполнен, `/health` отвечает ok
