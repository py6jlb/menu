# 60: Единое актуальное чтение рецепта для списка и деталей

**What to build:** Участник видит в списке и подробном рецепте согласованные актуальные данные, источник и состояние ссылки. Чтение краткого списка не загружает все шаги и ингредиенты каждого источника.

**Blocked by:** None (can start immediately)

**Status:** resolved (commit 19c32e6)

- [x] Предметный модуль чтения скрывает связь локального рецепта с источником, доступность и происхождение; потребители не собирают эти правила самостоятельно из нескольких resolver.
- [x] Список и подробная страница используют модуль; локальный ID и семейная принадлежность не заменяются ID/владением источника.
- [x] Summary и detail загружают нужную проекцию: имя/метаданные не требуют двух полных дочерних коллекций; нет запроса на каждый внешний рецепт отдельно.
- [x] Состояние и контент согласованы при удалении/изменении источника, недоступность не выглядит полноценным рецептом с нулевыми параметрами.
- [x] Изменения источника видны при повторном чтении; кэш имени и broken fallback сохраняют текущие предметные правила.
- [x] Warning остаётся читаемым, broken оставляет кэш имени без выдуманного контента; происхождение копии и запрет транзитивного sharing не меняются.
- [x] Публичный просмотр, существующие контракты и остальные потребители продолжают работать: расширение выполнено совместимо для последующей миграции сценариев.
- [x] Проверены обычный/external/warning/broken, чужая семья и удаление источника между действиями; записан baseline SQL-count/объёма для списка и деталей.
- [x] Рефакторинг не вводит универсальный repository/CQRS: модуль скрывает реальные гарантии чтения, а не только переадресует вызовы.

## Evidence

- Backend: scoped `RecipeReader` (`backend/src/MenuPlanner.Api/Recipes/RecipeReader.cs`) — единственный вход чтения рецепта для `GET /api/recipes` и `GET /api/recipes/{id}`. Он разом разрешает связь с источником, состояние ссылки (`ok`/`warning`/`broken`) и подпись семьи-источника; потребителя не собирают это из `ExternalRecipeSourceLoader`, `ExternalRecipeStateResolver`, `ExternalRecipeNameCache` и `SourceFamilyNameResolver`. `RecipeEndpoints.ListAsync`/`GetAsync` больше не знают об этих сервисах.
- Проекции: краткое чтение (`ReadSummariesAsync`) берёт скалярную проекцию источника (`ExternalRecipeSourceLoader.LoadSummariesAsync`) без `RecipeSteps`/`RecipeIngredients`; подробное (`ReadDetailAsync`) грузит источник с контентом. Локальный id, `FamilyId` и ревизия остаются строки-получателя, а не источника (тест `ReadDetail_ExternalWithLiveSource_KeepsLocalIdAndRevision_WithSourceContent`). Broken-источник возвращает кэш имени и пустой контент, не рецепт с нулевыми параметрами.
- Baseline SQL (PostgreSQL, уровень `RecipeReader`): список из 4 рецептов (3 внешних + 1 свой) — 7 команд, и это число не растёт с числом внешних (7 при 1 и при 25 внешних); подробный внешний рецепт с 4 шагами и 4 ингредиентами — 7 команд. В командах краткого чтения нет обращений к `RecipeSteps`/`RecipeIngredients` (`PostgresRecipeReadTests.ReadSummaries_UsesScalarProjection_WithoutChildTables`, `ReadSummaries_CommandCount_DoesNotGrowWithExternalRecipeCount`).
- Тесты backend (fast): `RecipeReaderTests` — обычный/external/warning/broken, чужая семья, отсутствующий рецепт, удаление источника между чтениями и обновление кэша имени. `docker run ... dotnet test` → Failed: 0, Passed: 430, Skipped: 53.
- Тесты backend (PostgreSQL): `scripts/test-postgres.sh` → Failed: 0, Passed: 56.
- Совместимость: HTTP-контракты, роуты и схема БД не менялись; публичный просмотр `/api/shared/{token}`, план, покупки, подбор и автодополнение продолжают использовать прежние пути и зелёные. `ExternalRecipeNameCache.RefreshAsync` принимает карту имён источника вместо `Recipe`, поведение и повторы без изменений.
- Разделение кода: `RecipeReader` добавлен в перечень БД-зависимых scoped-сервисов, термин «Актуальное чтение рецепта» — в `CONTEXT.md`. Универсального repository/CQRS не вводится: модуль сам выбирает проекцию, обновляет кэш имени и держит broken-fallback.
