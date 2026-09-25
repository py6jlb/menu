# 21: Prod-конфигурация — compose, Caddy, сеть, ForwardedHeaders

**What to build:** Продовый контур деплоя. `docker-compose.prod.yml` — образы из Docker Hub (без `build`), сервис `caddy` как единый край, наружу только 80/443; `db`/`backend`/`frontend` без публикации портов; healthchecks, mem limits, настройки логирования. `deploy/Caddyfile` — `{$DOMAIN}`: `/api/*` → `backend:8080`, `/health` → `backend:8080`, `/` → `frontend:80`; `encode zstd gzip` и security-заголовки; до покупки домена HTTP, после — авто Let's Encrypt. `frontend/nginx.conf` — убрать `/api`-прокси, оставить только статику и SPA-fallback. Backend — `UseForwardedHeaders` (`XForwardedFor`/`XForwardedProto`), чтобы IP клиента приходил из Caddy.

**Blocked by:** 20 (VPS — bootstrap и харденинг сервера)

**Status:** ready-for-agent

- [ ] `docker-compose.prod.yml`: образы из Docker Hub, без `build`, наружу только Caddy 80/443
- [ ] `db`/`backend`/`frontend` без `ports` (только внутренняя сеть)
- [ ] healthchecks и mem limits у сервисов
- [ ] `deploy/Caddyfile`: `/api/*` и `/health` → backend, `/` → frontend, `encode`, security-заголовки
- [ ] До домена — HTTP, после — авто-HTTPS по `{$DOMAIN}`
- [ ] `frontend/nginx.conf` без `/api`-прокси (только статика + SPA-fallback)
- [ ] Backend: `UseForwardedHeaders` (XForwardedFor/XForwardedProto)
- [ ] `docker compose -f docker-compose.prod.yml config` валиден
- [ ] Backend-тесты проходят в контейнере
