# 97: Вынести Domain в библиотеку `MenuPlanner.Domain`

**What to build:** `Domain/` покидает `MenuPlanner.Api` и становится отдельной библиотекой `backend/src/MenuPlanner.Domain` с namespace `MenuPlanner.Domain.*`; Api и тесты ссылаются на неё. Логика и имена типов не меняются.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

## Что делаем

- Новый проект `backend/src/MenuPlanner.Domain/MenuPlanner.Domain.csproj` (`Microsoft.NET.Sdk`, `net10.0`, `Nullable`, `ImplicitUsings`); без внешних пакетов.
- Перенести `backend/src/MenuPlanner.Api/Domain/**` → `backend/src/MenuPlanner.Domain/**` (подпапки `Accounts/Families/Recipes/Plans/Emails` сохранить).
- `namespace MenuPlanner.Api.Domain.*` → `MenuPlanner.Domain.*`; обновить `using` в Api и tests.
- `MenuPlanner.Api.csproj`: добавить `ProjectReference` на `MenuPlanner.Domain`.
- Добавить проект в `MenuPlanner.sln` (папка `src`).
- `CONTEXT.md`: пути `Domain/…` → `MenuPlanner.Domain/…`.

## Checklist

- [ ] Проект создан, файлы перемещены (`git mv`), namespace = `MenuPlanner.Domain.*`.
- [ ] Api и tests собираются; `using` обновлены.
- [ ] `MenuPlanner.sln` включает новый проект; `dotnet test` находит и собирает.
- [ ] Логика/имена типов/HTTP-контракты/схема БД не изменены.
- [ ] `dotnet test` (684/62) и `scripts/test-postgres.sh` (70) зелёные.

## Evidence

_(заполняется при реализации)_
