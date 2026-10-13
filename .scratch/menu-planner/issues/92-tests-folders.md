# 92: Тесты — зеркало фич + Support

**What to build:** `backend/tests/MenuPlanner.Api.Tests` раскладывается по фичевым папкам с зеркальными namespace'ами; вспомогательные типы уезжают в `Support/`; все Postgres-тесты остаются в едином `Postgres/`.

**Blocked by:** 90, 91.

**Status:** resolved (ticket/92-tests-folders)

## Карта

```
Auth/         AuthCode*, AuthFlow, AuthRateLimit, AuthSessionValidator,
              AdminBootstrap, AdminUnlock, EmailVerification(+Service),
              PasswordReset(+Service), ReadOnlyUnverified, RateLimiter
Recipes/      Recipe*, ExternalRecipe*, File*, RecipeFileUpload,
              RecipeGarbageCollector, DietCatalog, RecipeMatcher
Plans/        PlanFlow, PlanValidation, WeekPlanSaver, Repetition*, WeekStart
ShoppingList/ ShoppingList*
Families/     FamilyFlow
Emails/       Email*, Outbox*, Durable*
Settings/     (не-Postgres настройки, если есть)
Support/      ApiFactory, PostgresApiFactory, *TestDoubles,
              AsyncBarrier, BarrierNonQueryInterceptor,
              PostgresFactAttribute, PostgresSqlState
Postgres/     все Postgres-тесты — как есть
```

## Checklist

- [x] Тесты перемещены через `git mv` по карте; namespace'ы зеркалят папки.
- [x] `Support/` содержит только вспомогательные типы (не тест-классы).
- [x] Смысл тестов не изменён; `dotnet test` и `scripts/test-postgres.sh` зелёные (тот же счёт passed/skipped).

## Evidence

Все 74 корневых `.cs` разложены по папкам; namespace каждой папки — `MenuPlanner.Api.Tests.<Folder>`. `Postgres/` не перекладывался (namespace `MenuPlanner.Api.Tests.Postgres` как был). Изменения в перемещённых файлах — только `namespace` + `using`.

### Перемещения

| Папка | Файлов | Состав |
| --- | --- | --- |
| `Auth/` | 12 | AdminBootstrap, AdminUnlock, AuthCode, AuthFlow, AuthRateLimit, AuthSessionValidator, EmailVerificationFlow, EmailVerificationService, PasswordResetFlow, PasswordResetService, RateLimiter, ReadOnlyUnverified |
| `Recipes/` | 26 | DietCatalog, ExternalRecipeContext/Import/IntegrationFlow/Promotion/StateFlow/State, FileStorage, RecipeDiet/Document/FileLifecycleService/FileNames/FileUpload/Flow/GarbageCollector/Matcher/Match/PdfValidator/Photo/Reader/RevisionFlow/ShareFlow/ValidationFlow/Validation, SharedRecipeFlow, SharedRecipeImportService |
| `Plans/` | 6 | PlanFlow, PlanValidation, RepetitionCounter, RepetitionFlow, WeekPlanSaver, WeekStart |
| `ShoppingList/` | 3 | ShoppingListBuilder, ShoppingListContentBuilder, ShoppingListFlow |
| `Families/` | 1 | FamilyFlow |
| `Emails/` | 8 | DurableEmailDeliveryFlow, EmailConfiguration, EmailOutboxProcessor, EmailSender, EmailWiring, LoggingEmailTransport, OutboxPayloadProtector, SmtpEmailTransport |
| `Settings/` | 1 | SettingsFlow |
| `Configuration/` | 1 | ProductionConfiguration |
| `Data/` | 2 | DatabaseConnection, DbServices |
| `Health/` | 1 | HealthReadiness |
| `Ingredients/` | 1 | IngredientAutocomplete |
| `Observability/` | 4 | ObservabilityConfiguration, ReleaseIdentity, RequestLogging, SensitivePath |
| `Support/` | 8 (+1 новый) | ApiFactory, AuthTestDoubles, OutboxTestDoubles, InMemoryRecipeFileStore, SmtpTestServer, TestImages, TestLogCapture, TestOutbox; выделен `RecordingEmailTransport` |

`Support/` целиком состоит из вспомогательных типов (`ApiFactory`, `*TestDoubles`, фейки/серверы/образы/лог-капчер, `TestOutbox`), тест-классов там нет. Вспомогательный `RecordingEmailTransport`, определённый в `.cs` теста `Auth/EmailVerificationFlowTests`, вынесен отдельным файлом в `Support/` (им пользуются и Postgres-тесты).

### `using`

В тесты, использующие типы из `Support/`, автодобавлен `using MenuPlanner.Api.Tests.Support;` (в существующую группу `MenuPlanner.Api.*` по алфавиту). В 5 Postgres-файлах `using MenuPlanner.Api.Tests;` заменён на `using MenuPlanner.Api.Tests.Support;` (2 файла получили директиву впервые). В `RequestLoggingTests` относительная ссылка `Observability.ReleaseIdentity` заменена на `MenuPlanner.Api.Observability.ReleaseIdentity`: после переезда в namespace `…Tests.Observability` сегмент `Observability` стал указывать на сам тестовый namespace.

### Отклонения от карты (обоснование)

- **Postgres-инфрастуктура осталась в `Postgres/`.** Карта упоминает `PostgresApiFactory`, `AsyncBarrier`, `BarrierNonQueryInterceptor`, `PostgresFactAttribute`, `PostgresSqlState` в `Support/`, но правило тикета приоритетнее: «`Postgres*`-инфраструктуру, уже лежащую в `Postgres/`, оставь там; при сомнении выбирай место, откуда её импортируют». Все они импортируются только Postgres-тестами (общих нет), поэтому переносить в `Support/` значило бы добавить `using …Support` каждому Postgres-тесту без выгоды.
- **Не перечисленные в карте фичи получили свою папку** вместо сваливания в `Settings/`: `Configuration/`, `Data/`, `Health/`, `Ingredients/`, `Observability/` — зеркалят одноимённые feature-папки backend-проекта (после #90/#91 namespace = папка). `Settings/` занят тестом фичи Settings (`SettingsFlowTests`).

### Проверка

```bash
# в worktree .worktrees/92-tests-folders
docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet build
# → Build succeeded. 0 errors; 20 warnings — все предсуществующие NU1902/NU1903 (SixLabors.ImageSharp)

docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
# → Failed: 0, Passed: 684, Skipped: 62, Total: 746

bash scripts/test-postgres.sh
# → Failed: 0, Passed: 70, Skipped: 0, Total: 70
```

Проверка соответствия namespace папкам скриптом — расхождений нет. `git diff --check` — чисто; изменённые строки вне `namespace`/`using` — только тело вынесенного `RecordingEmailTransport` и полная квалификация `MenuPlanner.Api.Observability.ReleaseIdentity`.
