# 99: Собрать фичи в `MenuPlanner.Api/Features`

**What to build:** Готовые фичи в `MenuPlanner.Api` складываются в `Features/`, чтобы корень не разрастался; namespace `MenuPlanner.Api.Features.*`; тесты зеркалят `Features/`.

**Blocked by:** 98 — Вынести инфраструктуру (после неё в Api остаются только фичи).

**Status:** ready-for-agent

## Что делаем

- Перенести `Auth/`, `Families/`, `Ingredients/`, `Plans/`, `Recipes/`, `Settings/`, `ShoppingList/` → `MenuPlanner.Api/Features/…`.
- `namespace MenuPlanner.Api.<Feature>.*` → `MenuPlanner.Api.Features.<Feature>.*`; обновить `using` в Api и tests.
- `Program.cs` остаётся в корне Api; `appsettings*.json` — там же.
- Тесты: `MenuPlanner.Api.Tests/<Feature>/` → `MenuPlanner.Api.Tests/Features/<Feature>/` (namespace `MenuPlanner.Api.Tests.Features.<Feature>`); `Support/` и `Postgres/` остаются в корне тестов.
- Логика, контракты, схема БД не меняются.

## Checklist

- [ ] Фичи в `Features/`, namespace = `MenuPlanner.Api.Features.*`.
- [ ] Тесты зеркалят `Features/`; `Support/`/`Postgres/` не тронуты.
- [ ] Собирается, `using` обновлены.
- [ ] `dotnet test` (684/62) и `scripts/test-postgres.sh` (70) зелёные.

## Evidence

_(заполняется при реализации)_
