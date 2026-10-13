# 98: Вынести инфраструктуру в библиотеку `MenuPlanner.Infrastructure`

**What to build:** Не-фичевые папки (`Configuration`, `Data`, `Health`, `Observability`, `Emails`, `Migrations`) покидают `MenuPlanner.Api` и становятся библиотекой `backend/src/MenuPlanner.Infrastructure` с namespace `MenuPlanner.Infrastructure.*`. `MenuPlanner.Api` остаётся только фичами и хостом.

**Blocked by:** 97 — Вынести Domain в библиотеку (Infrastructure ссылается на Domain).

**Status:** resolved (ticket/98-infrastructure-library)

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

- [x] Проект создан, папки перемещены, namespace = `MenuPlanner.Infrastructure.*`; Api и tests собираются.
- [x] EF-миграции собираются и применяются из Infrastructure; `MigrationsAssembly` не нужен.
- [x] `release-gate.sh`/`README`/`AGENTS.md` указывают новые пути; `dotnet ef migrations has-pending-model-changes` → «No changes».
- [x] Логика/контракты/схема БД не изменены.
- [x] `dotnet test` (684/62) и `scripts/test-postgres.sh` (70) зелёные.

## Evidence

### Перемещения

`git mv` по шести папкам, подпапки сохранены (`backend/src/MenuPlanner.Api/<dir>` → `backend/src/MenuPlanner.Infrastructure/<dir>`):

- `Configuration/` — ApiExceptionHandling.cs, DeploymentOptions.cs, ForwardedHeaderConfiguration.cs, ProductionConfiguration.cs
- `Data/` — AppDbContext.cs, AppDbContextFactory.cs, DatabaseCapabilities.cs, DatabaseConnection.cs
- `Health/` — ReadinessEndpoint.cs, ReadinessProbe.cs
- `Observability/` — ObservabilityOptions.cs, ReleaseIdentity.cs, RequestLoggingMiddleware.cs, RequestTrace.cs, SensitivePath.cs
- `Emails/` — Contracts/Options/Outbox/Services (17 файлов)
- `Migrations/` — 8 миграций (.cs + .Designer.cs) и `AppDbContextModelSnapshot.cs`

`git status` видит перемещения как rename (`RM`); в содержимом менялись только строки `namespace`/`using` (`MenuPlanner.Api.{Configuration,Data,Health,Observability,Emails,Migrations}` → `MenuPlanner.Infrastructure.*`). Замена одним проходом `sed` по перемещённым файлам + `using` в Api (36 файлов) и tests (60 файлов). Ниже — как делили csproj/пакеты.

### Проект и ссылки

- Создан `backend/src/MenuPlanner.Infrastructure/MenuPlanner.Infrastructure.csproj` (`Microsoft.NET.Sdk`, `net10.0`, `Nullable`, `ImplicitUsings`, `InternalsVisibleTo MenuPlanner.Api.Tests`), `ProjectReference` → `MenuPlanner.Domain`, `FrameworkReference Microsoft.AspNetCore.App`.
- Пакеты EF Core, EF Core Relational, Npgsql.EntityFrameworkCore.PostgreSQL, MailKit, OpenTelemetry.Exporter.OpenTelemetryProtocol, OpenTelemetry.Extensions.Hosting, OpenTelemetry.Instrumentation.AspNetCore перенесены в Infrastructure.
- `MenuPlanner.Api.csproj`: добавлен `ProjectReference` на Infrastructure; оставлены веб/JWT/ImageSharp (`Microsoft.AspNetCore.Authentication.JwtBearer`, `SixLabors.ImageSharp`, `System.IdentityModel.Tokens.Jwt`) и сохранён `Microsoft.EntityFrameworkCore.Design` (PrivateAssets=all) — он нужен стартап-проекту для `dotnet ef`.
- `MenuPlanner.sln`: проект добавлен вручную в solution-папку `src` (GUID `{E8E3E899-1F4A-4990-8474-B7B622D60867}`), только `Any CPU` (стиль #97, без x64/x86).
- `MigrationsAssembly` не настраивался: `AppDbContext` и `Migrations/` в одной сборке; `Program.cs`, `appsettings*.json` остались в Api.

### Скрипты и инфраструктура

- `scripts/release-gate.sh`: `--project backend/src/MenuPlanner.Infrastructure --startup-project backend/src/MenuPlanner.Api`.
- `README.md` и `AGENTS.md`: та же команда `dotnet ef migrations add`; в `AGENTS.md` обновлены путь миграций и состав feature-папок.
- `backend/Dockerfile`: перед `restore` копируются `MenuPlanner.Domain.csproj` и `MenuPlanner.Infrastructure.csproj` (корректный слой восстановления пакетов); `publish` не менялся.
- `CONTEXT.md`: пути `Emails/` → `MenuPlanner.Infrastructure/Emails/`.
- В `deploy/*` прямых EF-путей не было (в `deploy/README.md` таблица этапов перечисляет только команду без пути) — правок не потребовалось.

### Проверки

- `docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test` — `Passed! - Failed: 0, Passed: 684, Skipped: 62, Total: 746`.
- `bash scripts/test-postgres.sh` — `Passed! - Failed: 0, Passed: 70, Skipped: 0, Total: 70`.
- EF-гейт (как в `release-gate.sh`): `dotnet tool restore && dotnet restore backend/src/MenuPlanner.Infrastructure && dotnet ef migrations has-pending-model-changes --project backend/src/MenuPlanner.Infrastructure --startup-project backend/src/MenuPlanner.Api` → `No changes have been made to the model since the last migration.`

### Отклонения / замечания

- Базовый `Microsoft.NET.Sdk` не включает implicit usings веб-SDK, поэтому `FrameworkReference` дополнен явными `<Using>` (`Microsoft.AspNetCore.{Builder,Hosting,Http,Routing}`, `Microsoft.Extensions.{Configuration,DependencyInjection,Hosting,Logging}`, `System.Net.Http.Json`) — иначе `IConfiguration`/`HttpContext`/`ILogger`/`IResult` не находились.
- `Microsoft.EntityFrameworkCore.Design` оставлен в Api (стартап-проект) ради `dotnet ef`; в Infrastructure не дублируется, гейт проходит.
- `Migrations/**` по-прежнему содержат строковые имена типов `MenuPlanner.Api.Domain.*` (наследие #97): `has-pending-model-changes` их не считает дрейфом, схема/рантайм не затронуты — строки не трогали.
- `scripts/check-dependencies.sh` продолжает сканировать Api.csproj: перенесённые пакеты попадают в отчёт транзитивно (`--include-transitive`), изменение не требуется.
- Историческое упоминание `MenuPlanner.Api.Health` в `deploy/tests/README.md` оставлено как запись о прошлой пробе/фиксе.
