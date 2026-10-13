# 94: Domain — по концептам

**What to build:** `backend/src/MenuPlanner.Api/Domain/` (22 файла) раскладывается по подпапкам, повторяющим фичи и разделы `CONTEXT.md`; namespace зеркалит папку. Логика, имена типов и контракты не меняются.

**Blocked by:** 90, 91 — раскладка backend и namespace'ы уже устоялись.

**Status:** ready-for-agent

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

- [ ] Файлы перемещены через `git mv` по карте.
- [ ] `namespace` = папка (`MenuPlanner.Api.Domain.<Folder>`); `using` в src и tests обновлены.
- [ ] Логика, имена типов, HTTP-контракты и схема БД не изменены.
- [ ] `Domain/` не оставляет файлов в корне.
- [ ] `dotnet test` (684/62) и `scripts/test-postgres.sh` (70) зелёные.

## Evidence

_(заполняется при реализации)_
