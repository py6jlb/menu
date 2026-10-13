# 99: Собрать фичи в `MenuPlanner.Api/Features`

**What to build:** Готовые фичи в `MenuPlanner.Api` складываются в `Features/`, чтобы корень не разрастался; namespace `MenuPlanner.Api.Features.*`; тесты зеркалят `Features/`.

**Blocked by:** 98 — Вынести инфраструктуру (после неё в Api остаются только фичи).

**Status:** resolved (ticket/99-api-features-folder)

## Что делаем

- Перенести `Auth/`, `Families/`, `Ingredients/`, `Plans/`, `Recipes/`, `Settings/`, `ShoppingList/` → `MenuPlanner.Api/Features/…`.
- `namespace MenuPlanner.Api.<Feature>.*` → `MenuPlanner.Api.Features.<Feature>.*`; обновить `using` в Api и tests.
- `Program.cs` остаётся в корне Api; `appsettings*.json` — там же.
- Тесты: `MenuPlanner.Api.Tests/<Feature>/` → `MenuPlanner.Api.Tests/Features/<Feature>/` (namespace `MenuPlanner.Api.Tests.Features.<Feature>`); `Support/` и `Postgres/` остаются в корне тестов.
- Логика, контракты, схема БД не меняются.

## Checklist

- [x] Фичи в `Features/`, namespace = `MenuPlanner.Api.Features.*`.
- [x] Тесты зеркалят `Features/`; `Support/`/`Postgres/` не тронуты.
- [x] Собирается, `using` обновлены.
- [x] `dotnet test` (684/62) и `scripts/test-postgres.sh` (70) зелёные.

## Evidence

Ветка `ticket/99-api-features-folder`, worktree `.worktrees/99-api-features-folder` (в main не влито).

**Перемещения (git mv, подпапки сохранены):**

- `backend/src/MenuPlanner.Api/{Auth,Families,Ingredients,Plans,Recipes,Settings,ShoppingList}` → `backend/src/MenuPlanner.Api/Features/…` — 83 `.cs`.
  - Auth 26, Families 4, Ingredients 2, Plans 6, Recipes 37, Settings 4, ShoppingList 4.
- `backend/tests/MenuPlanner.Api.Tests/{Auth,Families,Ingredients,Plans,Recipes,Settings,ShoppingList}` → `backend/tests/MenuPlanner.Api.Tests/Features/…` — 50 `.cs`.
  - Auth 12, Families 1, Ingredients 1, Plans 6, Recipes 26, Settings 1, ShoppingList 3.
- Не тронуты: `Program.cs`, `appsettings*.json`, `MenuPlanner.Api.csproj` (корень Api); `Support/`, `Postgres/`, а также `Configuration/`, `Data/`, `Emails/`, `Health/`, `Observability/` (корень тестов).

**Правки namespace/using:**

- `namespace MenuPlanner.Api.<Feature>.*` → `MenuPlanner.Api.Features.<Feature>.*` (в т.ч. вложенные: `Auth.Accounts/Admin/Codes/PasswordReset/RateLimiting/Sessions/Verification`, `Recipes.Crud/External/Files/Reading/Repetition/Sharing`).
- `namespace MenuPlanner.Api.Tests.<Feature>` → `MenuPlanner.Api.Tests.Features.<Feature>`.
- `using` обновлены по всему `backend/src` и `backend/tests`.
- Итог: 341 вхождение `MenuPlanner.Api.Features.` + 50 `MenuPlanner.Api.Tests.Features.` в `.cs`.
- git: 133 файла `RM` (rename + правка namespace) + 19 `M` (файлы, оставшиеся на месте: `Program.cs`, тесты `Support`/`Postgres`/`Configuration`/`Data`/`Emails`/`Health`/`Observability`, где обновились только `using`).
- Не затронуты исторические namespace `MenuPlanner.Api.Domain.*` в миграциях `MenuPlanner.Infrastructure` — не входят в целевой список.
- Логика, контракты, схема БД не менялись.

**Проверки (SDK-контейнер `mcr.microsoft.com/dotnet/sdk:10.0`):**

- `dotnet test` → `Passed! - Failed: 0, Passed: 684, Skipped: 62, Total: 746`.
- `bash scripts/test-postgres.sh` → `Passed! - Failed: 0, Passed: 70, Skipped: 0, Total: 70`.

**Отклонения:** нет. Артефакты `bin/`/`obj/` от первого прогона были root-owned (контейнер без `-u`) — снесены контейнером перед `scripts/test-postgres.sh`; сам suite запускается от текущего пользователя.
