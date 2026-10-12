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

- [ ] `SharedRecipeImportService` вынесен; `AppDbContext` убран из лямбды импорта.
- [ ] HTTP-статусы и тела ошибок/конфликта не изменились.
- [ ] Тесты: in-memory исходы (`InvalidLink`/`OwnFamily`/`AlreadyImported`/`Created`) + Postgres-гонка параллельного импорта.
- [ ] `dotnet test` и `scripts/test-postgres.sh` зелёные.

## Evidence

_(заполняется при реализации)_

## Comments
