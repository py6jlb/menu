# 94: Domain — по концептам

**What to build:** `backend/src/MenuPlanner.Api/Domain/` (22 файла) раскладывается по подпапкам, повторяющим фичи и разделы `CONTEXT.md`; namespace зеркалит папку. Логика, имена типов и контракты не меняются.

**Blocked by:** 90, 91 — раскладка backend и namespace'ы уже устоялись.

**Status:** resolved (ticket/94-domain-folders)

## Карта (финальная)

```
Domain/
  Accounts/   User, UserRole, UserSettings, AuthCode
  Families/   Family, FamilyMember, FamilyCatalog
  Recipes/    Recipe, RecipeIngredient, RecipeStep, RecipeCatalog,
              DietCatalog, IngredientCategoryRules, RecipeMatching, RecipeShare
  Plans/      WeekPlan, PlanEntry, MealType, PlanningCatalog, WeekStart
  Emails/     EmailOutboxMessage
```

Основание: разделы `CONTEXT.md` — «Аккаунты и доступ» (User/UserRole/UserSettings/AuthCode, письма → Emails), «Семья», «Рецепт» (включая «Ссылка на рецепт» → `RecipeShare`), «Планирование». Имена подпапок совпадают с фичевыми (`Accounts`, `Families`, `Recipes`, `Plans`, `Emails`), чтобы `grep` по папке находил всё в одном месте.

## Checklist

- [x] Файлы перемещены через `git mv` по карте.
- [x] `namespace` = папка (`MenuPlanner.Api.Domain.<Folder>`); `using` в src и tests обновлены.
- [x] Логика, имена типов, HTTP-контракты и схема БД не изменены.
- [x] `Domain/` не оставляет файлов в корне.
- [x] `dotnet test` (684/62) и `scripts/test-postgres.sh` (70) зелёные.

## Evidence

Ветка: `ticket/94-domain-folders` (worktree `.worktrees/94-domain-folders`).

**Перемещения (`git mv`, 21 файл):**

| Папка | Файлы |
| --- | --- |
| `Domain/Accounts/` | User, UserRole, UserSettings, AuthCode |
| `Domain/Families/` | Family, FamilyMember, FamilyCatalog |
| `Domain/Recipes/` | Recipe, RecipeIngredient, RecipeStep, RecipeCatalog, DietCatalog, IngredientCategoryRules, RecipeMatching, RecipeShare |
| `Domain/Plans/` | WeekPlan, PlanEntry, MealType, PlanningCatalog, WeekStart |
| `Domain/Emails/` | EmailOutboxMessage |

Файлов в корне `Domain/` не осталось. Примечание: в тикете указано «22 файла», фактически в `Domain/` было 21 (карта совпадает с 21).

**Namespace'ы:** каждый файл несёт `namespace MenuPlanner.Api.Domain.{Accounts,Families,Recipes,Plans,Emails};` (содержимое, имена типов и логика не тронуты; `RecipeMatching.cs` целиком в `Recipes`, `WeekStart.cs` — в `Plans`).

**Правки `using`:** убраны 89 `using MenuPlanner.Api.Domain;` в `backend/src` и `backend/tests`; проставлены точные `using MenuPlanner.Api.Domain.<Folder>;` по фактическому использованию типов. Затронуто 116 `.cs`-файлов (включая 21 перемещённый), суммарно +193/−112 строк (в основном `using`). Также обновлены точечные ссылки: `AdminUnlockTests.cs` — `MenuPlanner.Api.Domain.Accounts.User`; `Families/Contracts.cs` — `Domain.Family`/`Domain.FamilyMember` → `Domain.Families.Family`/`Domain.Families.FamilyMember`.

**Проверки:**

```
docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet build
# Build succeeded. 0 Error(s)

docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
# Passed!  - Failed: 0, Passed: 684, Skipped: 62, Total: 746

scripts/test-postgres.sh
# Passed!  - Failed: 0, Passed: 70, Skipped: 0, Total: 70
```

**Отклонения / наблюдения:**

- `Migrations/*.Designer.cs` и `AppDbContextModelSnapshot.cs` содержат строковые имена типов (`"MenuPlanner.Api.Domain.User"` и т.п.) — это сгенерированные снапшоты EF; не тронуты (не `namespace`/`using`, схема БД не меняется, тесты не сверяют снапшот). При следующей генерации миграции EF перепишет их на новые имена.
- `CONTEXT.md` (раздел «Domain docs») всё ещё ссылается на старые пути `Domain/User.cs` и др. Не менялся в рамках тикета (запрет на изменение содержимого вне `namespace`/`using`); актуализация путей — отдельным мелким тикетом.
