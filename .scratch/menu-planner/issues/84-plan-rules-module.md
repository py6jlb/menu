# 84: Правила плана недели — из HTTP-лямбды в module

**What to build:** Валидация записей плана и проверка принадлежности рецептов семье перестают жить в `PlanEndpoints`; чистые правила — в `PlanValidation`, инвариант принадлежности — в `WeekPlanSaver`, поэтому они тестируемы без HTTP.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

## Design (из grilling)

- `Plans/PlanValidation.cs` (static, чистый): `ValidateEntries(IReadOnlyList<PlanEntryRequest>) → PlanErrorDto?` — перенос текущей приватной `Validate` без смены правил и кодов ошибок.
- Проверка «все рецепты принадлежат семье» уходит в `WeekPlanSaver.SaveAsync` — часть инварианта полной замены, рядом с атомарной проверкой ревизии; новый исход `WeekPlanMutationOutcome.NotOwnedRecipes` (endpoint мапит в тот же `400` с `PlanErrorDto`).
- `PlanEndpoints.SaveWeekAsync` остаётся оркестрацией: разобрать → `PlanValidation` → `WeekPlanSaver` → смапить исход.

## Checklist

- [x] `PlanValidation.ValidateEntries` вынесен; поведение, коды и тексты ошибок не изменились.
- [x] `WeekPlanSaver` проверяет принадлежность рецептов; `AppDbContext` из `PlanEndpoints.SaveWeekAsync` убран.
- [x] `PlanEndpoints` не содержит доменных правил (только разбор запроса и ответ).
- [x] Прямые тесты `PlanValidation` (все ветки: лимит, null-запись, день, приём, порции, дубль) + тест принадлежности в saver.
- [x] `dotnet test` и `scripts/test-postgres.sh` зелёные; HTTP-контракты не изменились.

## Evidence

Реализовано в worktree `.worktrees/84-plan-rules-module`, ветка `ticket/84-plan-rules-module`.

**Файлы:**
- Добавлен `backend/src/MenuPlanner.Api/Plans/PlanValidation.cs` — `static`-класс без состояния, `ValidateEntries(IReadOnlyList<PlanEntryRequest>) → PlanErrorDto?`: перенос приватной `PlanEndpoints.Validate` без смены правил, порядка проверок, кодов и текстов.
- Изменён `backend/src/MenuPlanner.Api/Plans/WeekPlanSaver.cs` — новый исход `WeekPlanMutationOutcome.NotOwnedRecipes`; принадлежность всех рецептов набора семье проверяется в начале `SaveAsync` (инвариант полной замены) до чтения ревизии, поэтому чужой рецепт даёт `400`, а не `409`, и не пишется в план.
- Изменён `backend/src/MenuPlanner.Api/Plans/PlanEndpoints.cs` — `SaveWeekAsync` стал оркестрацией (разобрать → `PlanValidation.ValidateEntries` → `WeekPlanSaver` → смапить исход); `AppDbContext`/`Microsoft.EntityFrameworkCore`/`MenuPlanner.Api.Data`/`MenuPlanner.Api.Domain` убраны; `NotOwnedRecipes` мапится в тот же `400` с тем же `PlanErrorDto`. `TryParseWeekStart`/`WeekStartFormat` не тронуты (для #87).
- Добавлен `backend/tests/MenuPlanner.Api.Tests/PlanValidationTests.cs` — 13 прямых тестов на все ветки (валидный набор, все коды приёмов, лимит записей + приоритет порядка, null-запись с индексом, границы дня, неизвестный приём, границы порций, дубль слота).
- Добавлен `backend/tests/MenuPlanner.Api.Tests/WeekPlanSaverTests.cs` — 5 тестов инварианта принадлежности (свой рецепт → `Success`; чужой/неизвестный/смешанный → `NotOwnedRecipes` без записи; чужой при устаревшей ревизии → `NotOwnedRecipes`, не `Conflict`).
- Изменён `CONTEXT.md` — `PlanValidation` добавлен в список Stateless-правил.

**Проверки (SDK-контейнер, хост без .NET):**
- `docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test` — Failed: 0, Passed: 657, Skipped: 61 (Postgres), Total: 718.
- `scripts/test-postgres.sh` — Failed: 0, Passed: 69, Skipped: 0, Total: 69.
- HTTP-тесты `PlanFlowTests` зелёные без изменения смысла; новые компиляторные предупреждения не появились (только предсуществующие NU1902/NU1903 по ImageSharp).

## Comments
