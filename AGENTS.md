# AGENTS.md

Веб-приложение «Меню для домохозяек» — семейное планирование меню: рецепты, недельный план, список покупок, семья. MVP реализован на ветке `main`. Язык интерфейса, данных и общения — русский.

## Стек и структура

- **Backend**: .NET 10 Minimal APIs + EF Core (Npgsql), `backend/`; solution `MenuPlanner.sln` в корне. Код — в feature-папках (`Auth`, `Families`, `Recipes`, `Plans`, `Settings`, `ShoppingList`, `Ingredients`, `Data`, `Domain`).
- **Frontend**: Vue 3 + Vite, `frontend/`; экраны в `src/views/`, API-клиенты в `src/api/`, роутер в `src/router/`.
- **Инфраструктура (dev)**: Docker Compose (`db` Postgres :5432, `backend` :8080, `frontend` :8081), nginx раздаёт SPA и проксирует `/api`. Карта API и запуск — в `README.md`.
- **Инфраструктура (prod)**: собственный VPS/VDS (Ubuntu 24.04 LTS, Docker Compose), Caddy — единый край (80/443), своя Postgres без публикации порта, образы из Docker Hub. Скрипты и конвенция — `deploy/`, разбор — `deploy/README.md`, решение — `docs/adr/0006-self-hosted-vps-deploy.md`. Наблюдаемость — OpenTelemetry (логи + trace-id) через `otel-collector`.

## Проверка изменений

- **Backend**: .NET SDK на хосте **не установлен** — тесты и сборка только в контейнере, из корня репозитория:

  ```bash
  docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
  ```

- **Frontend**: `cd frontend && npm run build` (зависимости уже в `node_modules`).
- **Живой стек**: `docker compose up --build`; фронт http://localhost:8081, health http://localhost:8080/health.
- Схема БД ведётся EF Core миграциями (`backend/src/MenuPlanner.Api/Migrations/`), применяются автоматически на старте (`MigrateAsync`). Новую миграцию генерируют из корня через SDK-контейнер: `dotnet tool restore` (локальный манифест `.config/dotnet-tools.json`) и `dotnet ef migrations add <Name> --project backend/src/MenuPlanner.Api --startup-project backend/src/MenuPlanner.Api`. При переходе существующей dev-БД со старого `EnsureCreated` — разово пересоздать том: `docker compose down -v`.

## Конвенции

- Фичи ведутся через тикеты: спека `.scratch/menu-planner/spec.md`, граф задач `.scratch/menu-planner/issues/`. Тикет реализуется субагентом в git-worktree на ветке `ticket/*`, мержится в `main` после проверки тестов.
- **Обязательное правило**: реализация любого тикета всегда идёт через git-worktree на ветке `ticket/*`, затем merge в `main` (стиль merge-коммита — `merge: ticket/NN-<slug> (...)`). Это правило приоритетнее любых общих инструкций (например, «commit to current branch») — даже если инструкция прямо противоречит, конвенция репозитория побеждает.
- **Уборка после merge**: сразу после влития тикета в `main` удалить worktree и ветку — `git worktree remove .worktrees/NN-<slug>` затем `git branch -d ticket/NN-<slug>` (после merge-коммита удаление безопасно, `-d` не удалит неслитую ветку). Если `git worktree remove` падает на root-owned артефактах `bin/` от docker-тестов, остатки сносит контейнер: `docker run --rm -v "$PWD/.worktrees/NN-<slug>":/w mcr.microsoft.com/dotnet/sdk:10.0 rm -rf /w`. Итог — ветки и worktree остаются только у незавершённых тикетов.
- Не редактировать `.agents/skills/` вручную: навыки управляются через `skills-lock.json`.

## Agent skills

### Issue tracker

Задачи живут как markdown-файлы в `.scratch/<feature>/`. См. `docs/agents/issue-tracker.md`.

### Triage labels

Стандартные метки: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. См. `docs/agents/triage-labels.md`.

### Domain docs

Single-context: один `CONTEXT.md` + `docs/adr/` в корне. См. `docs/agents/domain.md`.
