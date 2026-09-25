# 34: Поддержка EF Core миграций вместо EnsureCreated

**What to build:** Перейти со `EnsureCreated` на EF Core миграции: старт применяет `MigrateAsync` (только для relational-провайдера, fail-fast), одна `Initial`-миграция фиксирует текущую модель, CLI `dotnet ef` доступен через локальный tool-манифест, сборка падает при рассинхроне модели и миграций.

**Blocked by:** None (can start immediately)

**Status:** resolved (commit acd2a47)

- [x] `Program.cs`: вместо `EnsureCreatedAsync` — `if (db.Database.IsRelational()) await db.Database.MigrateAsync();` (fail-fast, без флагов и ретраев)
- [x] `IDesignTimeDbContextFactory<AppDbContext>` читает `ConnectionStrings__Default`, dev-fallback `Host=localhost;Port=5432;Database=menu_planner;Username=menu;Password=menu`
- [x] `.config/dotnet-tools.json` в корне с прибитым `dotnet-ef` 10.0.11; команды через `dotnet tool restore`
- [x] `Initial`-миграция в `backend/src/MenuPlanner.Api/Migrations/`, захватывающая всю текущую модель (включая уникальный фильтрованный индекс из тикета 33)
- [x] `deploy/build-push.sh`: после `dotnet test`, до сборки образов — `has-pending-model-changes` в SDK-контейнере с `-u $(id -u):$(id -g)`
- [x] `AGENTS.md` и `README.md` обновлены: воркфлоу `dotnet ef migrations add`, авто-применение на старте, разовый `docker compose down -v` при переходе существующей dev-БД
- [x] `docs/adr/0007-ef-core-migrations.md`: решение, fail-fast, forward-only, откат приложения только с восстановлением БД из бэкапа
- [x] `dotnet test` зелёный; ручная проверка `docker compose down -v && docker compose up -d --build db backend` — схема создаётся миграцией, `__EFMigrationsHistory` заполнена, `/health` отвечает
