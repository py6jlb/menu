# 77: Категории продуктов в ингредиентах и списке покупок

**What to build:** Пользователь назначает продукту категорию из конечного справочника (мясо, молочка, бакалея и т.д.) прямо в строке ингредиента рецепта, а список покупок группируется по категориям, чтобы удобно ходить по магазину. Пустая категория — «Прочее» в конце списка.

**Blocked by:** None (can start immediately)

**Status:** resolved (ticket/77-ingredient-categories)

- [x] Категория продукта — поле ингредиента рецепта из конечного зашитого справочника; пустое значение допустимо и означает «Прочее».
- [x] Форма рецепта даёт выбрать категорию в строке ингредиента (дефолт пусто; пункт «— не выбрано»), подсказка названия подставляет категорию продукта.
- [x] Список покупок группирует позиции по категориям в порядке справочника, «Прочее» последней; внутри категории — плоский список, отсортированный по группе единиц, затем по названию.
- [x] Агрегированная позиция берёт категорию по большинству слагаемых ингредиентов; при равенстве — «Прочее»; конвертация и объединение по единицам сохраняются.
- [x] Внешний рецепт несёт категории источника (read-only сохраняется), сломанный по-прежнему исключён.
- [x] Просмотр рецепта показывает метку категории рядом с ингредиентом без группировки.
- [x] Валидация отклоняет категорию вне справочника; сохранение и чтение сохраняют значение; правила задокументированы в CONTEXT.md и ADR.

## Evidence

- Домен: `RecipeIngredient.Category` (nullable, `varchar(32)`); конечный справочник `RecipeCatalog.IngredientCategories` (11 кодов) с порядком отдела (`IngredientCategoryRank`); чистое правило большинства `IngredientCategoryRules.ResolveMajority` (единоличный лидер, иначе null = «Прочее»). Миграция `20261009080716_AddRecipeIngredientCategory` (nullable AddColumn).
- Запись/чтение: `RecipeIngredientRequest.Category` проверяется (`ingredient_category_invalid` вне справочника), пустое нормализуется в null; `MapIngredients` и `RecipeEndpoints.ToDto` несут значение. Внешний рецепт материализуется из живого источника целиком, поэтому категории едут от источника; промоушен копирует ингредиенты вместе с категорией.
- Список покупок: `IngredientLine`/`ShoppingListItem` получили `Category`; бакет голосует категориями и разрешает большинство; сортировка — категория (в порядке справочника, «Прочее» последней) → группа единиц → название; `ShoppingListItemDto.Category` в DTO.
- Автодополнение: `IngredientUsageQuery` группирует по (нормализованное имя, категория); `RecipeReader.ReadIngredientSuggestionsAsync` отдаёт `IngredientSuggestion(Name, Category)` по большинству — тем же правилом, что и список; `IngredientAutocompleteDto.Items` стал `{ name, category }`.
- Frontend: `CATEGORIES`/`OTHER_CATEGORY`/`categoryLabel` в `constants/recipe.js`; select категории в строке формы с подстановкой из подсказки (только если поле пустое); `ShoppingView` группирует по категориям, «Прочее» последней; `RecipeBody` показывает метку категории; `useRecipeDraft` несёт `category` в черновике, payload и сериализации.
- Документация: термин «Категория продукта» в `CONTEXT.md` с отличием от «Группы единиц», ADR `docs/adr/0014-ingredient-product-categories.md`.
- Тесты: backend 601 passed / 61 skipped (без Postgres-env) + PostgreSQL-suite 66 passed (`scripts/test-postgres.sh`); frontend 229 passed; `npm run build` — чисто. Новые тесты: большинство/ничья/пустая категория и порядок (`ShoppingListBuilderTests`), перенос категории из ингредиентов (`ShoppingListContentBuilderTests`), валидация и нормализация (`RecipeValidationTests`), категория подсказок по большинству (`RecipeReaderTests`), end-to-end большинство и round-trip (`ShoppingListFlowTests`), `PostgresRecipeReadTests`; фронтовые — `recipe.test.js`, `useRecipeDraft.test.js`.
