# 01: Инфраструктура и сборка

**What to build:** Весь стек поднимается одной командой `docker compose up`: Postgres, .NET 8 backend, Vue 3/Vite frontend за nginx. Бэкенд умеет health-check, фронт показывает «API connected». Деплой-каркас готов к следующим тикетам.

**Blocked by:** None (can start immediately)

**Status:** resolved (commit `b41a5bd`)

- [x] Docker Compose поднимает Postgres, backend, frontend
- [x] .NET 8 solution с Minimal APIs и EF Core, миграции подключены
- [x] Vue 3 + Vite SPA, раздаётся nginx, проксирует API
- [x] Health-check endpoint и отображение «API connected» на фронте
- [x] README с инструкцией запуска