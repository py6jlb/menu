# 63: Актуальные ингредиенты внешних рецептов без лишней загрузки

**What to build:** Семейное автодополнение предлагает названия из текущих ингредиентов собственных и доступных внешних рецептов, не требует загрузки всех шагов и сохраняет ограниченную, устойчивую выдачу.

**Blocked by:** 60 — Единое актуальное чтение рецепта для списка и деталей.

**Status:** resolved (ticket/63-effective-ingredient-suggestions; commits 60bf635, 690d4b2)

- [x] Серверное автодополнение использует проекцию единого модуля чтения, не самостоятельное смешивание старого fallback и источников.
- [x] Правки источника видны при следующем запросе; warning учитывается, broken не даёт устаревших ингредиентов.
- [x] Нормализация trim/lowercase, поиск по префиксу, ранжирование по частоте и затем названию, ограничение до десяти различимых подсказок сохранены.
- [x] Фильтрация и ограничение реализованы так, чтобы не загружать полное содержимое семейной коллекции; не добавлено по запросу на каждый внешний рецепт.
- [x] Пустой q возвращает топ, пользователь без семьи — пустой ответ; чужие семейные ингредиенты не раскрываются.
- [x] Контракт остаётся совместимым с ручным вводом и стабильными строками frontend; не вводится обязательный глобальный справочник продуктов.
- [x] Проверены актуализация источника, warning/broken, одинаковые имена, частота и семейная изоляция; измерены SQL-count/объём на представительном наборе.

## Evidence

- **Единое чтение.** `RecipeReader.ReadIngredientSuggestionsAsync` (`backend/src/MenuPlanner.Api/Recipes/RecipeReader.cs`) — единственный вход автодополнения: свои ингредиенты читаются скалярной проекцией, для внешних рецептов состояние ссылок разрешается модулем, а названия доступных источников читаются через `ExternalRecipeSourceLoader.LoadIngredientSuggestionsAsync`. `warning` (источник жив, ссылка отозвана) остаётся читаемым и участвует в подсказках по ADR-0005, `broken` исключён. `IngredientEndpoints.AutocompleteAsync` больше не знает про `AppDbContext` и внешние сервисы — только `familyId` и вызов модуля.
- **Агрегация в SQL.** `IngredientUsageQuery.Build` (`Recipes/External/IngredientUsageQuery.cs`): пустые имена отсекаются, поиск по префиксу (`lower(trim(name)) LIKE q%`) и группировка по нормализованному названию (`GROUP BY lower(trim(name))`, `count`, `min`) выполняются в БД. В память попадают только различимые названия с частотами; итоговое слияние своих/внешних названий, ранжирование `частота → название` и `Take(10)` — в модуле. Пофайлового запроса на внешний рецепт нет.
- **Актуализация и состояния.** Живые названия читаются заново на каждый запрос. Тесты: `RecipeReaderTests.ReadIngredientSuggestions_ReflectsSourceEditOnNextRead`, `ReadIngredientSuggestions_BrokenSourceExcluded_WarningIncluded`; интеграционные `ExternalRecipeIntegrationFlowTests.Autocomplete_ReflectsLiveSourceEdits` / `Autocomplete_WarningExternal_StillIncludesLiveIngredients` / `Autocomplete_BrokenExternal_DoesNotIncludeStaleIngredients`.
- **Нормализация, частота, одинаковые имена, изоляция.** `ReadIngredientSuggestions_RanksByFrequency_NotAlphabetically` (частота важнее алфавита), `ReadIngredientSuggestions_MergesSameNameAcrossOwnAndExternal` (одинаковые имена сводятся к одному), `ReadIngredientSuggestions_NormalizesPrefixes_AndCapsAtTen` (trim/lowercase префикса, ровно 10), `ReadIngredientSuggestions_DoesNotLeakForeignFamilyIngredients`; существующие `IngredientAutocompleteTests` (пустой q — топ, без семьи — пусто, чужое не видно) зелёные.
- **SQL-count/объём (PostgreSQL).** `PostgresRecipeReadTests.ReadIngredientSuggestions_AggregatesInSql_ReadsNoSteps_AndDoesNotGrowWithExternalCount`: 25 источников × 3 ингредиента = 75 строк → 3 различимые подсказки; число команд одинаково при 1 и 25 внешних; в командах есть `GROUP BY` и нет `RecipeSteps`. `ReadIngredientSuggestions_ExcludesBrokenSourceCachedIngredients` — кэш сломанного источника не протекает.
- **Тесты.** Backend fast: `dotnet test` → Failed: 0, Passed: 474, Skipped: 55. PostgreSQL: `scripts/test-postgres.sh` → Failed: 0, Passed: 58. Совместимость: HTTP-контракт `/api/ingredients/autocomplete` и DTO `IngredientAutocompleteDto` не менялись, глобальный справочник продуктов не вводится; frontend без изменений. Термин «Актуальное чтение рецепта» в `CONTEXT.md` дополнен автодополнением.
