# 92: Тесты — зеркало фич + Support

**What to build:** `backend/tests/MenuPlanner.Api.Tests` раскладывается по фичевым папкам с зеркальными namespace'ами; вспомогательные типы уезжают в `Support/`; все Postgres-тесты остаются в едином `Postgres/`.

**Blocked by:** 90, 91.

**Status:** ready-for-agent

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

- [ ] Тесты перемещены через `git mv` по карте; namespace'ы зеркалят папки.
- [ ] `Support/` содержит только вспомогательные типы (не тест-классы).
- [ ] Смысл тестов не изменён; `dotnet test` и `scripts/test-postgres.sh` зелёные (тот же счёт passed/skipped).

## Evidence

_(заполняется при реализации)_
