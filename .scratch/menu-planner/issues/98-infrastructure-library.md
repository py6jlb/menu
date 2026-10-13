# 98: Вынести инфраструктуру в библиотеку `MenuPlanner.Infrastructure`

**What to build:** Не-фичевые папки (`Configuration`, `Data`, `Health`, `Observability`, `Emails`, `Migrations`) покидают `MenuPlanner.Api` и становятся библиотекой `backend/src/MenuPlanner.Infrastructure` с namespace `MenuPlanner.Infrastructure.*`. `MenuPlanner.Api` остаётся только фичами и хостом.

**Blocked by:** 97 — Вынести Domain в библиотеку (Infrastructure ссылается на Domain).

**Status:** ready-for-agent

## Что делаем

- Новый проект `backend/src/MenuPlanner.Infrastructure/MenuPlanner.Infrastructure.csproj` (`Microsoft.NET.Sdk`, `net10.0`); перенести туда зависимости EF/Npgsql/MailKit/OpenTelemetry (EF Core, Relational, Design, Npgsql.EntityFrameworkCore.PostgreSQL, MailKit, OpenTelemetry.*).
- Перенести папки `Configuration/`, `Data/`, `Health/`, `Observability/`, `Emails/`, `Migrations/` из `MenuPlanner.Api` в `MenuPlanner.Infrastructure` (подпапки сохранить).
- `namespace MenuPlanner.Api.*` → `MenuPlanner.Infrastructure.*` в перемещённых файлах; обновить `using` в Api и tests.
- `ProjectReference`: `Infrastructure → Domain`; `Api → Infrastructure` (+ уже `Api → Domain`).
- Убрать из `MenuPlanner.Api.csproj` перенесённые пакеты (оставить веб/JWT/ImageSharp и т.п.).
- `InternalsVisibleTo` для `MenuPlanner.Api.Tests` добавить на `Infrastructure`, если тесты используют его internal.
- Обновить пути EF в `scripts/release-gate.sh` и `deploy/*`: `--project backend/src/MenuPlanner.Infrastructure --startup-project backend/src/MenuPlanner.Api`.
- `Program.cs` (хост, DI, `MigrateAsync`) остаётся в Api; `appsettings*.json` — в Api.
- `MigrationsAssembly` не настраивать: `Migrations/` едет с `AppDbContext`.
- `MenuPlanner.sln`: добавить проект.
- `CONTEXT.md`: ссылки на сервисы/`DbContext` при необходимости (пути не перечислены по папкам — проверить).

## Checklist

- [ ] Проект создан, папки перемещены, namespace = `MenuPlanner.Infrastructure.*`; Api и tests собираются.
- [ ] EF-миграции собираются и применяются из Infrastructure; `MigrationsAssembly` не нужен.
- [ ] `release-gate.sh`/`deploy` указывают новые пути; `dotnet ef migrations has-pending-model-changes` → «No changes».
- [ ] Логика/контракты/схема БД не изменены.
- [ ] `dotnet test` (684/62) и `scripts/test-postgres.sh` (70) зелёные.

## Evidence

_(заполняется при реализации)_
