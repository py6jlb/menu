# AGENTS.md

Веб-приложение «Меню для домохозяек» — семейное планирование меню: рецепты, недельный план, список покупок, семья. MVP реализован на ветке `main`. Язык интерфейса, данных и общения — русский.

## Стек и структура

- **Backend**: .NET 10 Minimal APIs + EF Core (Npgsql), `backend/`; solution `MenuPlanner.sln` в корне. Код — в feature-папках (`Auth`, `Families`, `Recipes`, `Plans`, `Settings`, `ShoppingList`, `Ingredients`, `Data`, `Domain`).
- **Frontend**: Vue 3 + Vite, `frontend/`; экраны в `src/views/`, API-клиенты в `src/api/`, роутер в `src/router/`.
- **Инфраструктура**: Docker Compose (`db` Postgres :5432, `backend` :8080, `frontend` :8081), nginx раздаёт SPA и проксирует `/api`. Карта API и запуск — в `README.md`.

## Проверка изменений

- **Backend**: .NET SDK на хосте **не установлен** — тесты и сборка только в контейнере, из корня репозитория:

  ```bash
  docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
  ```

- **Frontend**: `cd frontend && npm run build` (зависимости уже в `node_modules`).
- **Живой стек**: `docker compose up --build`; фронт http://localhost:8081, health http://localhost:8080/health.
- Схема БД создаётся `EnsureCreated`, миграций нет: при изменении модели — `docker compose down -v` и поднять заново.

## Конвенции

- Фичи ведутся через тикеты: спека `.scratch/menu-planner/spec.md`, граф задач `.scratch/menu-planner/issues/`. Тикет реализуется субагентом в git-worktree на ветке `ticket/*`, мержится в `main` после проверки тестов.
- Не редактировать `.agents/skills/` вручную: навыки управляются через `skills-lock.json`.

## Agent skills

### Issue tracker

Задачи живут как markdown-файлы в `.scratch/<feature>/`. См. `docs/agents/issue-tracker.md`.

### Triage labels

Стандартные метки: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. См. `docs/agents/triage-labels.md`.

### Domain docs

Single-context: один `CONTEXT.md` + `docs/adr/` в корне. См. `docs/agents/domain.md`.
