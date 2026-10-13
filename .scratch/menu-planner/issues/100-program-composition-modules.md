# 100: Разбить `Program.cs` на модульные extension'ы

**What to build:** `Program.cs` из хозяина всех регистраций превращается в тонкий composition-root: подключение модулей — через `AddX`/`UseX`/`MapX` extension'ы рядом с их кодом; `Program` держит только жизненный цикл хоста. Поведение, контракты, порядок middleware и команда `admin-bootstrap` не меняются.

**Blocked by:** None (can start immediately)

**Status:** resolved (ticket/100-program-composition-modules)

## Модули (Q7)

| Метод | Файл / namespace | Что регистрирует |
|---|---|---|
| `AddDatabase` | `Infrastructure/Data/DatabaseServiceCollectionExtensions.cs` | `AppDbContext`, `DatabaseConnection.Resolve` |
| `AddReadiness` | `Infrastructure/Health/…Extensions.cs` | `ReadinessOptions`, `IDatabaseReadinessProbe` |
| `AddEmailDelivery` | `Infrastructure/Emails/…Extensions.cs` | transport, `EmailSender`, outbox, `EmailDeliveryWorker`, protector |
| `AddObservability` | `Infrastructure/Observability/…Extensions.cs` | `ObservabilityOptions`, OTel, логирование (`WebApplicationBuilder`); включает `AddReleaseResource` |
| `AddProductionConfiguration` | `Infrastructure/Configuration/…Extensions.cs` | `ProductionConfiguration.Read(…, secret)`, `deploymentOptions` |
| `AddJwtAuthentication` | `Api/Features/Auth/Sessions/…Extensions.cs` | `JwtOptions`, `JwtTokenService`, `AddAuthentication/AddJwtBearer/AddAuthorization` |
| `AddFileStorage` | `Api/Features/Recipes/Files/…Extensions.cs` | `IRecipeFileStore`, lifecycle, GC, worker, cleanup options |
| `AddAuth` | `Api/Features/Auth/…Extensions.cs` | hasher, codes, rate limit, verification/reset/session/user-store, `AdminBootstrap` |
| `AddRecipes` | `Api/Features/Recipes/…Extensions.cs` | reader/context/mutation/sharing/promotion/import/repetition, `ShareOptions` |
| `AddPlans` | `Api/Features/Plans/…Extensions.cs` | saver, reader |
| `AddFamilies` | `Api/Features/Families/…Extensions.cs` | `FamilyService` |
| `AddSettings` | `Api/Features/Settings/…Extensions.cs` | `SettingsService` |
| `AddShared` | `Api/SharedServiceCollectionExtensions.cs` (host) | `TimeProvider.System` |

## Правила (Q8–Q12)

- Конфиг-ридеры → статические `JwtOptions.Read(IConfiguration)`, `AuthCodeOptions.Read(IConfiguration)`; `AddEmailDelivery` для ключа outbox сам зовёт `JwtOptions.Read`.
- `UseDatabaseMigration()` → `Infrastructure/Data`; `UseTrustedForwardedHeaders()`, `MapFeatureEndpoints()` → `Api/HostExtensions.cs` (namespace `MenuPlanner.Api`).
- Классы — `<Area>ServiceCollectionExtensions` в папке своей зоны; host — `HostExtensions`.
- `Ingredients`/`ShoppingList`: DI-регистраций нет — только `MapX`.
- `Program.cs` оставляет: `builder`, `AddX`, `Build`, `admin-bootstrap`, middleware (порядок), `MapFeatureEndpoints`, миграции, `Run`, fail-fast `GetRequiredService<IRecipeFileStore>()`.
- Порядок middleware и состав регистраций не меняются; глубокие `using`/явные типы сервисов из `Program.cs` уходят.

## Checklist

- [x] Все `AddX`/`UseX`/`MapFeatureEndpoints` созданы в зонах; `Program.cs` — тонкий хост.
- [x] Конфиг-ридеры перенесены в типы опций; `AddEmailDelivery` не зависит от порядка вызова `AddJwtAuthentication`.
- [x] Порядок middleware, `admin-bootstrap`, `MigrateAsync`, fail-fast сохранены; поведение/контракты не изменены.
- [x] `dotnet test` (684/62) и `scripts/test-postgres.sh` (70) зелёные.

## Evidence

### Что сделано

`Program.cs`: 295 → 80 строк. Осталось: `builder`/`configuration`, цепочка
`AddX`/`AddShared`, `builder.Build()`, `admin-bootstrap` (`AdminBootstrapArgs.IsCommand`),
middleware в прежнем порядке (`UseTrustedForwardedHeaders` условно внутри расширения,
`UseRequestLogging`, `UseApiExceptionHandling`), startup-логирование, fail-fast
`GetRequiredService<IRecipeFileStore>()`, `MapFeatureEndpoints()`,
`await app.UseDatabaseMigration()`, `app.Run()`, `public partial class Program { }`.

### Новые extension-файлы

Инфраструктура (`MenuPlanner.Infrastructure`):

| Файл | Метод | Что регистрирует |
|---|---|---|
| `Configuration/JwtOptions.cs` | `JwtOptions.Read` | настройки JWT (перенесён из `Api/Features/Auth/Sessions`) |
| `Configuration/ConfigurationServiceCollectionExtensions.cs` | `AddProductionConfiguration(config, secret)` | `DeploymentOptions` из `ProductionConfiguration.Read` |
| `Data/DatabaseServiceCollectionExtensions.cs` | `AddDatabase(config)` | `AppDbContext` через `DatabaseConnection.Resolve` |
| `Data/DatabaseApplicationBuilderExtensions.cs` | `UseDatabaseMigration()` | `MigrateAsync` под `IsRelational()` |
| `Health/HealthServiceCollectionExtensions.cs` | `AddReadiness(config)` | `ReadinessOptions`, `IDatabaseReadinessProbe` |
| `Emails/EmailServiceCollectionExtensions.cs` | `AddEmailDelivery(config)` | транспорт, `EmailSender`, outbox, `EmailDeliveryWorker`, protector |
| `Observability/ObservabilityServiceCollectionExtensions.cs` | `AddObservability(builder, config)` | `ObservabilityOptions`, OTel, логирование; включает `AddReleaseResource` |

Api (`MenuPlanner.Api`):

| Файл | Метод | Что регистрирует |
|---|---|---|
| `HostExtensions.cs` | `UseTrustedForwardedHeaders`, `MapFeatureEndpoints` | условные `ForwardedHeaders`; `/health`, `/ready` и те же 12 `MapXEndpoints()` в прежнем порядке |
| `SharedServiceCollectionExtensions.cs` | `AddShared()` | `TimeProvider.System` |
| `Features/Auth/Sessions/JwtAuthenticationServiceCollectionExtensions.cs` | `AddJwtAuthentication(config)` | `JwtOptions`, `JwtTokenService`, `AddAuthentication/AddJwtBearer/AddAuthorization` |
| `Features/Auth/AuthServiceCollectionExtensions.cs` | `AddAuth(config)` | hasher, codes, rate limit, verification/reset/session/user-store, `AdminBootstrap` |
| `Features/Recipes/RecipeServiceCollectionExtensions.cs` | `AddRecipes()` | reader/context/mutation/sharing/promotion/import/repetition, `ShareOptions` |
| `Features/Recipes/Files/FileStorageServiceCollectionExtensions.cs` | `AddFileStorage()` | `IRecipeFileStore`, lifecycle, GC, cleanup options, worker |
| `Features/Plans/PlanServiceCollectionExtensions.cs` | `AddPlans()` | `WeekPlanSaver`, `WeekPlanReader` |
| `Features/Families/FamilyServiceCollectionExtensions.cs` | `AddFamilies()` | `FamilyService` |
| `Features/Settings/SettingsServiceCollectionExtensions.cs` | `AddSettings()` | `SettingsService` |

`Ingredients`/`ShoppingList` DI-регистраций не имеют — только `MapX` в агрегаторе.

Конфиг-ридеры `ReadJwtOptions`/`ReadAuthCodeOptions` заменены на статические
`JwtOptions.Read(IConfiguration)` и `AuthCodeOptions.Read(IConfiguration)`;
`AddEmailDelivery` сам читает секрет JWT для ключа outbox.

### Отклонение

`JwtOptions` перенесён из `MenuPlanner.Api.Features.Auth.Sessions` в
`MenuPlanner.Infrastructure.Configuration`: иначе `AddEmailDelivery`
(Infrastructure) не смог бы вызвать `JwtOptions.Read`, т.к. Infrastructure не
ссылается на Api. HTTP-контракты, схема БД и команда `admin-bootstrap` не
затронуты. `JwtTokenService` продолжает работать с этим типом через новый using.

`AddShared` использует `TryAddSingleton<TimeProvider>` (а не `AddSingleton`):
`AddAuthentication` в .NET 10 сам делает `TryAddSingleton<TimeProvider>`, и при
порядке вызова «сначала JWT, потом AddShared» безусловная регистрация давала
дубль `TimeProvider` (падение `AuthApiFactory.ReplaceSingleton`).

### Проверка

- `dotnet build` — 0 ошибок; из предупреждений только предсуществующий `AD0001`
  (`RouteHandlerAnalyzer`) и `NU1902/NU1903` (ImageSharp), как и на `main`.
- `dotnet test` → Passed: 684, Skipped: 62, Failed: 0 (746).
- `scripts/test-postgres.sh` → Passed: 70, Skipped: 0, Failed: 0.
- `admin-bootstrap`-ветка и fail-fast `GetRequiredService<IRecipeFileStore>()`
  сохранены в `Program.cs`.
