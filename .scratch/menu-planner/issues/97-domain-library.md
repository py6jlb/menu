# 97: Вынести Domain в библиотеку `MenuPlanner.Domain`

**What to build:** `Domain/` покидает `MenuPlanner.Api` и становится отдельной библиотекой `backend/src/MenuPlanner.Domain` с namespace `MenuPlanner.Domain.*`; Api и тесты ссылаются на неё. Логика и имена типов не меняются.

**Blocked by:** None (can start immediately)

**Status:** resolved (ticket/97-domain-library)

## Что делаем

- Новый проект `backend/src/MenuPlanner.Domain/MenuPlanner.Domain.csproj` (`Microsoft.NET.Sdk`, `net10.0`, `Nullable`, `ImplicitUsings`); без внешних пакетов.
- Перенести `backend/src/MenuPlanner.Api/Domain/**` → `backend/src/MenuPlanner.Domain/**` (подпапки `Accounts/Families/Recipes/Plans/Emails` сохранить).
- `namespace MenuPlanner.Api.Domain.*` → `MenuPlanner.Domain.*`; обновить `using` в Api и tests.
- `MenuPlanner.Api.csproj`: добавить `ProjectReference` на `MenuPlanner.Domain`.
- Добавить проект в `MenuPlanner.sln` (папка `src`).
- `CONTEXT.md`: пути `Domain/…` → `MenuPlanner.Domain/…`.

## Checklist

- [x] Проект создан, файлы перемещены (`git mv`), namespace = `MenuPlanner.Domain.*`.
- [x] Api и tests собираются; `using` обновлены.
- [x] `MenuPlanner.sln` включает новый проект; `dotnet test` находит и собирает.
- [x] Логика/имена типов/HTTP-контракты/схема БД не изменены.
- [x] `dotnet test` (684/62) и `scripts/test-postgres.sh` (70) зелёные.

## Evidence

### Перемещения

`git mv backend/src/MenuPlanner.Api/Domain backend/src/MenuPlanner.Domain` — 21 файл, подпапки сохранены:

- `Accounts/`: AuthCode.cs, User.cs, UserRole.cs, UserSettings.cs
- `Emails/`: EmailOutboxMessage.cs
- `Families/`: FamilyCatalog.cs, Family.cs, FamilyMember.cs
- `Plans/`: MealType.cs, PlanEntry.cs, PlanningCatalog.cs, WeekPlan.cs, WeekStart.cs
- `Recipes/`: DietCatalog.cs, IngredientCategoryRules.cs, RecipeCatalog.cs, Recipe.cs, RecipeIngredient.cs, RecipeMatching.cs, RecipeShare.cs, RecipeStep.cs

Содержимое перемещённых файлов изменено только в строках `namespace`/`using` (`git diff -M` — similarity 97–100%).

### Проект и ссылки

- Создан `backend/src/MenuPlanner.Domain/MenuPlanner.Domain.csproj` (`Microsoft.NET.Sdk`, `net10.0`, `Nullable`, `ImplicitUsings`), без `PackageReference`.
- `MenuPlanner.Api.csproj`: добавлен `<ProjectReference Include="..\MenuPlanner.Domain\MenuPlanner.Domain.csproj" />`.
- `MenuPlanner.sln`: проект добавлен и вложен в solution-папку `src` (GUID `{BCFBAE4D-3124-4E2C-A41F-4B16BF07DD8E}`).
- `namespace MenuPlanner.Api.Domain.*` → `MenuPlanner.Domain.*` и `using MenuPlanner.Api.Domain...` → `MenuPlanner.Domain...` — 128 файлов в `backend/src` + `backend/tests` (кроме `Migrations/`), одним проходом `sed`.
- `CONTEXT.md`: 5 ссылок `` `Domain/…` `` → `` `MenuPlanner.Domain/…` ``.

### Проверки

- `docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test` — `Passed! - Failed: 0, Passed: 684, Skipped: 62, Total: 746`.
- `bash scripts/test-postgres.sh` — `Passed! - Failed: 0, Passed: 70, Skipped: 0, Total: 70`.

### Отклонения / замечания

- `dotnet sln add` в SDK-контейнере дополнительно прописал `Debug|x64/x86` и `Release|x64/x86` всем проектам (шумный diff). Файл восстановлен, проект добавлен вручную в стиле существующих записей (только `Any CPU`), вложением в `src`.
- `Migrations/**` (строковые имена типов в моделях/снапшотах) не трогались — по условию тикета; на рантайм они не влияют.
- Тестовый проект не получил отдельного `ProjectReference` на `MenuPlanner.Domain`: типы доступны транзитивно через ссылку на `MenuPlanner.Api`, сборка/тесты проходят.
- `AGENTS.md` (строка 7, список feature-папок с `Domain`) не менялся — вне явного скоупа тикета.
