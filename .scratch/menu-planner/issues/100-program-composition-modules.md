# 100: Разбить `Program.cs` на модульные extension'ы

**What to build:** `Program.cs` из хозяина всех регистраций превращается в тонкий composition-root: подключение модулей — через `AddX`/`UseX`/`MapX` extension'ы рядом с их кодом; `Program` держит только жизненный цикл хоста. Поведение, контракты, порядок middleware и команда `admin-bootstrap` не меняются.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

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

- [ ] Все `AddX`/`UseX`/`MapFeatureEndpoints` созданы в зонах; `Program.cs` — тонкий хост.
- [ ] Конфиг-ридеры перенесены в типы опций; `AddEmailDelivery` не зависит от порядка вызова `AddJwtAuthentication`.
- [ ] Порядок middleware, `admin-bootstrap`, `MigrateAsync`, fail-fast сохранены; поведение/контракты не изменены.
- [ ] `dotnet test` (684/62) и `scripts/test-postgres.sh` (70) зелёные.

## Evidence

_(заполняется при реализации)_
