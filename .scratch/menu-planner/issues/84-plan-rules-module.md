# 84: Правила плана недели — из HTTP-лямбды в module

**What to build:** Валидация записей плана и проверка принадлежности рецептов семье перестают жить в `PlanEndpoints`; чистые правила — в `PlanValidation`, инвариант принадлежности — в `WeekPlanSaver`, поэтому они тестируемы без HTTP.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

## Design (из grilling)

- `Plans/PlanValidation.cs` (static, чистый): `ValidateEntries(IReadOnlyList<PlanEntryRequest>) → PlanErrorDto?` — перенос текущей приватной `Validate` без смены правил и кодов ошибок.
- Проверка «все рецепты принадлежат семье» уходит в `WeekPlanSaver.SaveAsync` — часть инварианта полной замены, рядом с атомарной проверкой ревизии; новый исход `WeekPlanMutationOutcome.NotOwnedRecipes` (endpoint мапит в тот же `400` с `PlanErrorDto`).
- `PlanEndpoints.SaveWeekAsync` остаётся оркестрацией: разобрать → `PlanValidation` → `WeekPlanSaver` → смапить исход.

## Checklist

- [ ] `PlanValidation.ValidateEntries` вынесен; поведение, коды и тексты ошибок не изменились.
- [ ] `WeekPlanSaver` проверяет принадлежность рецептов; `AppDbContext` из `PlanEndpoints.SaveWeekAsync` убран.
- [ ] `PlanEndpoints` не содержит доменных правил (только разбор запроса и ответ).
- [ ] Прямые тесты `PlanValidation` (все ветки: лимит, null-запись, день, приём, порции, дубль) + тест принадлежности в saver.
- [ ] `dotnet test` и `scripts/test-postgres.sh` зелёные; HTTP-контракты не изменились.

## Evidence

_(заполняется при реализации)_

## Comments
