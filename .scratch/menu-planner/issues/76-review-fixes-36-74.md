# 76: Исправления двухосевого ревью тикетов 36–74

**What to build:** Устранить находки ревью по двум осям (Standards и Spec) для диффа `026128c...HEAD`: привести обработчики Minimal API к правилу `CONTEXT.md` (никаких `static`-методов с `AppDbContext`, только scoped-сервисы через DI), убрать дублирование и спекулятивные обёртки, починить приоритет закреплённого релиза над шаблоном конфигурации и защитить sharing-состояние рецепта от поздних ответов чужого ресурса.

**Blocked by:** None (can start immediately)

**Status:** resolved (commits 0835180, b4f126e)

## Standards

- [x] Правило `CONTEXT.md` («БД-зависимый сервис — scoped-класс через DI») соблюдено: `AppDbContext` убран из обработчиков и приватных static-хелперов; чтения вынесены в scoped-сервисы.
  - [x] `Auth/EmailVerificationEndpoints.cs` — `CurrentUserAsync(principal, db)` заменён на scoped `CurrentUserContext.UserAsync`.
  - [x] `Recipes/RecipeEndpoints.cs` — `RepetitionCountsAsync(...)` перенесён в `RepetitionCounter.CountForUserAsync`; запросы списка/детали/подбора — в scoped `RecipeReader`; создание — в `RecipeMutationService.CreateAsync`.
  - [x] `Auth/AuthEndpoints.cs` — `MeAsync` через `CurrentUserContext`; email-запросы через scoped `UserAccountStore`.
- [x] Дедупликация live-source проекции рецепта: `RecipeEndpoints` использует `ExternalRecipeContentResolver.Resolve`, а не инлайн 9-полевую проекцию.
- [x] `RecipeValidation` — общий guard `IsBlankIngredient` и нормализация `NormalizeSeasons` вместо дублей.
- [x] `PlanView.vue` — литерал фильтров вынесен в `emptyFilters()`.
- [x] Общий `LEAVE_MESSAGE` черновиков вынесен в `composables/draftMessages.js` (LOAD/SAVE-сообщения остаются ресурсными).
- [x] Провайдерная развилка сведена к одному seam `DatabaseCapabilities.SupportsRelationalLocking` (блокировки кодов, ссылок, outbox, промоушена, bootstrap).
- [x] `EmailVerificationService` / `PasswordResetService` — общий расчёт cooldown `AuthCodePolicy.CooldownSecondsLeft`.
- [x] `RecipeMutationService` — guard «load/not-found/external» вынесен в один `FindAsync`.

## Spec

- [x] #38: `server.conf.example` больше не задаёт `IMAGE_TAG=latest`; закреплённый `/opt/menu/current-release` остаётся тегом обычного перезапуска, env/явный `server.conf` по-прежнему приоритетнее.
- [x] #54: `useRecipeShare` привязан к identity ресурса (номер запроса + id); поздний `GET /share` старого рецепта не заполняет состояние нового, смена маршрута вызывает `reset`. Покрыто тестами `useRecipeShare.test.js`.

## Notes

- Партия ревью — один worktree `ticket/76-review-fixes-36-74`.
- Проверки: backend `dotnet test` — 421 passed (50 skipped); PostgreSQL-suite `scripts/test-postgres.sh` — 53 passed; frontend `vitest run` — 107 passed, `npm run build` — ok; deploy `python3 -B -m unittest discover -s deploy/tests` — 128 passed.
- Обработчики не получают `AppDbContext`: `RecipeEndpoints`, `AuthEndpoints`, `EmailVerificationEndpoints` работают только через scoped-сервисы.
