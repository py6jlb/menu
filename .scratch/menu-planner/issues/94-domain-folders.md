# 94: Domain — по концептам (второй этап)

**What to build:** `backend/src/MenuPlanner.Api/Domain/` (21 файл) раскладывается по концептам в тон фичам: сущности, каталоги и чистые правила рядом.

**Blocked by:** 90, 91 — чтобы проходы шли по устоявшейся раскладке.

**Status:** needs-triage (второй этап; уточнить карту перед стартом)

## Черновик карты

```
Domain/
  Accounts/   User, UserRole, UserSettings, AuthCode, FamilyCatalog?
  Family/     Family, FamilyMember, RecipeShare
  Recipes/    Recipe, RecipeIngredient, RecipeStep, RecipeCatalog, DietCatalog, IngredientCategoryRules
  Planning/   WeekPlan, PlanEntry, MealType, PlanningCatalog
  (корень)    общие типы без явной фичи
```

## Checklist

- [ ] Уточнить карту по содержимому файлов, зафиксировать здесь.
- [ ] Перемещение + namespace = папка; логика не меняется.
- [ ] `dotnet test` и `scripts/test-postgres.sh` зелёные.

## Evidence

_(заполняется при реализации)_
