# Деплой на VPS

Продовый контур: Ubuntu 24.04 LTS, Docker Compose, Caddy как единый край, своя Postgres, бэкапы в объектное хранилище. Провайдер любой — инфраструктура агностична.

## Состав

| Файл | Назначение |
|---|---|
| `bootstrap.sh` | первичная настройка и харденинг сервера |
| `build-push.sh` | тесты, сборка и публикация образов в Docker Hub |
| `deploy.sh` | доставка конфигов и рестарт на прибитом теге, откат |
| `Caddyfile` | маршрутизация края (`/api`, `/health`, SPA) |
| `backup.sh` | бэкап БД и фото в объектное хранилище |
| `restore-drill.sh` | проверочное восстановление |
| `.env.example` | шаблон окружения |

## Порядок

1. **Сервер.** На чистом VPS:

   ```bash
   sudo SSH_PUBLIC_KEY='ssh-ed25519 AAAA... user@host' ./deploy/bootstrap.sh
   ```

   Проверь вход на новом порту в отдельном терминале, затем закрой старый:

   ```bash
   sudo ./deploy/bootstrap.sh --close-legacy-port
   ```

2. **Окружение.** Скопируй `deploy/.env.example` в `/opt/menu/.env` на сервере, заполни секреты и `chmod 600 .env`.

3. **Сборка и публикация** (локально, из корня репозитория):

   ```bash
   ./deploy/build-push.sh
   ```

   Гейт: backend-тесты и `npm run build`. Образы пушатся в Docker Hub тегами `:<git-sha>` и `:latest`.

4. **Деплой:**

   ```bash
   ./deploy/deploy.sh <git-sha>
   ```

   Скрипт кладёт `docker-compose.prod.yml` и `Caddyfile` в `/opt/menu`, делает `compose pull && up -d` на указанном теге и проверяет `/health`.

5. **Откат:** `./deploy/deploy.sh <предыдущий-sha>`.

## Переменные

Локально (`deploy/.env`): `DOCKERHUB_USER`, `VPS_HOST`, `VPS_USER`, `VPS_SSH_PORT`, `APP_DIR`.

На сервере (`/opt/menu/.env`): дополнительно `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `JWT_SECRET`, `JWT_ISSUER`, `JWT_AUDIENCE`, `SMTP_*`, `DOMAIN`, `IMAGE_TAG`, rclone-настройки для `backup.sh`.

`DOMAIN=:80` до покупки домена (HTTP), затем `DOMAIN=example.com` — Caddy выпустит Let's Encrypt автоматически.

## Порты

Наружу открыты только `80`/`443` (Caddy) и SSH. `db`, `backend`, `frontend` доступны только внутри compose-сети.
