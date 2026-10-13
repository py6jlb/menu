# Devcontainer: изолированная среда разработки

Один и тот же набор инструментов на любой машине с Docker: **.NET 10 SDK**, **Node 24**, **Docker-in-Docker**.

## Запуск

- **VS Code / Cursor**: команда **Dev Containers: Reopen in Container**.
- **CLI**: `devcontainer up --workspace-folder .` (расширение `@devcontainers/cli`).

Рабочая папка монтируется в `/workspaces/menu`; порты `8080` (backend), `8081` (frontend), `5432` (postgres) пробрасываются автоматически.

## Что доступно после старта

```bash
docker compose up --build          # живой стек: db + backend + frontend
scripts/test-postgres.sh           # критический PostgreSQL-suite (одноразовый postgres:16)
docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
cd frontend && npm run test && npm run build
```

`postCreateCommand` уже выполняет `dotnet tool restore` и `npm ci --prefix frontend`.

## Почему Docker-in-Docker

Команды проекта поднимают контейнеры с bind-mount рабочей копии (`docker run -v "$(pwd)":/app …`) и одноразовый Postgres. При монтировании docker-сокета хоста путь `/workspaces/menu` существует только внутри devcontainer и в демоне хоста его нет — bind-mount упал бы. Внутренний демон (docker-in-docker) видит ту же файловую систему, поэтому пути совпадают, а среда остаётся изолированной.

## Ограничения

- Нужен Docker с поддержкой `--privileged` (для docker-in-docker); на CI/dev-машинах это штатно.
- Образы `mcr.microsoft.com/dotnet/sdk:10.0` и `node:24-*` подтягиваются во внутренний демон при первом запуске.
- Продовые скрипты `deploy/` рассчитаны на сервер и в контейнере не запускаются.
