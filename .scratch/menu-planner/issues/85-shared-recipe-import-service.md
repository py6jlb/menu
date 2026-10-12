# 85: Импорт внешнего рецепта — в SharedRecipeImportService

**What to build:** Доменная логика импорта внешнего рецепта (проверки, создание обёртки, гонка уникального индекса) переезжает из HTTP-лямбды `SharedRecipeEndpoints.ImportAsync` в scoped module; endpoint только мапит исход в HTTP.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

## Design (из grilling)

- `Recipes/SharedRecipeImportService.cs` (scoped): `ImportAsync(string token, Guid familyId) → исход`:
  `Created(Guid recipeId)` / `InvalidLink` / `OwnFamily` / `AlreadyImported(Guid recipeId)`; гонка уникального индекса `(FamilyId, SourceRecipeId)` — внутри (catch `DbUpdateException` → перечитать победителя).
- `SharedRecipeEndpoints.ImportAsync` убирает `AppDbContext` из лямбды и мапит исход в текущие статусы (`201`, `404`, `400`, `409`).
- Публичное чтение `GetAsync` не трогаем.

## Checklist

- [x] `SharedRecipeImportService` вынесен; `AppDbContext` убран из лямбды импорта.
- [x] HTTP-статусы и тела ошибок/конфликта не изменились.
- [x] Тесты: in-memory исходы (`InvalidLink`/`OwnFamily`/`AlreadyImported`/`Created`) + Postgres-гонка параллельного импорта.
- [x] `dotnet test` и `scripts/test-postgres.sh` зелёные.

## Evidence

Ветка `ticket/85-shared-recipe-import-service`.

- Новый scoped-модуль `backend/src/MenuPlanner.Api/Recipes/SharedRecipeImportService.cs`:
  `ImportAsync(string token, Guid familyId, CancellationToken)` → `SharedRecipeImportOutcome` +
  `SharedRecipeImportResult(Outcome, RecipeId)`. Исходы: `Created`, `InvalidLink`, `OwnFamily`,
  `AlreadyImported`. Гонка уникального индекса `(FamilyId, SourceRecipeId)` ловится внутри
  (`DbUpdateException` → detach → перечитать победителя → `AlreadyImported`).
- `SharedRecipeEndpoints.ImportAsync` больше не принимает `AppDbContext`, мапит исход в прежние
  статусы/тела: `201 RecipeImportResultDto`, `409 RecipeImportConflictDto`, `400` «Это рецепт вашей
  семьи.», `404` «Ссылка недействительна.». `GetAsync` не тронут.
- Регистрация в `Program.cs`; `CONTEXT.md` (раздел «Разделение кода») пополнен сервисом.
- Тесты: `backend/tests/MenuPlanner.Api.Tests/SharedRecipeImportServiceTests.cs` (6 in-memory исходов);
  `backend/tests/MenuPlanner.Api.Tests/Postgres/PostgresSharedRecipeImportTests.cs` (гонка параллельного
  импорта через `AsyncBarrier` на вставке — один `Created`, один `AlreadyImported`, одна строка-обёртка).
- `docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test`:
  Passed 663, Skipped 62 (Postgres без `MENU_PLANNER_TEST_POSTGRES`), Failed 0.
- `scripts/test-postgres.sh`: Passed 70, Skipped 0, Failed 0.

## Comments
