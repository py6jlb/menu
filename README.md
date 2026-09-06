# Меню для домохозяек

Веб-приложение для семейного планирования меню: рецепты, недельное планирование, список покупок.

Стек: .NET 8 Minimal APIs (backend), Vue 3 + Vite (SPA), Postgres, nginx. Всё поднимается Docker Compose.

## Структура

- `backend/` — .NET 8 solution (`MenuPlanner.sln`), приложение `src/MenuPlanner.Api` (Minimal APIs + EF Core + Postgres).
- `frontend/` — Vue 3 + Vite SPA, собирается в статику и раздаётся nginx.
- `docker-compose.yml` — оркестрация: `db`, `backend`, `frontend`.

## Быстрый старт

Требуется Docker с плагином Compose (v2).

```bash
docker compose up --build
```

Порты:

| Сервис    | Порт |
|-----------|------|
| Postgres  | 5432 |
| Backend   | 8080 |
| Frontend  | 8081 |

- Фронтенд (SPA): http://localhost:8081
- Backend health: http://localhost:8080/health

При поднятом backend страница показывает «API connected»; если backend недоступен — «API недоступен».

## Проверка здоровья

```bash
curl http://localhost:8080/health
# {"status":"ok","service":"menu-planner-api"}

curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8081/
# 200
```

## Разработка

Backend запускается в контейнере (`mcr.microsoft.com/dotnet/sdk:8.0`) — на хосте .NET SDK не требуется. Для команд `dotnet` используйте, например:

```bash
docker run --rm -v "$(pwd)":/work -w /work/backend mcr.microsoft.com/dotnet/sdk:8.0 dotnet --version
```

Frontend в dev-режиме (Vite dev-сервер, проксирует `/api` и `/health` на backend на `localhost:8080`):

```bash
cd frontend
npm install
npm run dev
```

## Конфигурация backend

- Строка подключения: env `ConnectionStrings__Default` (или `DB_CONNECTION_STRING`), при отсутствии — дефолт `Host=localhost;...` в `appsettings.json`.
- При старте применяется `EnsureCreated` (создание схемы БД, миграции появятся в следующих тикетах).
- Endpoint `GET /health` возвращает `200 {"status":"ok"}`.