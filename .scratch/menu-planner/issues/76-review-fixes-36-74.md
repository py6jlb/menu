# 76: Исправления двухосевого ревью тикетов 36–74

**What to build:** Устранить находки ревью по двум осям (Standards и Spec) для диффа `026128c...HEAD`: привести обработчики Minimal API к правилу `CONTEXT.md` (никаких `static`-методов с `AppDbContext`, только scoped-сервисы через DI), убрать дублирование и спекулятивные обёртки, починить приоритет закреплённого релиза над шаблоном конфигурации и защитить sharing-состояние рецепта от поздних ответов чужого ресурса.

**Blocked by:** None (can start immediately)

**Status:** in-progress

## Standards

- [ ] Правило `CONTEXT.md` («БД-зависимый сервис — scoped-класс через DI») соблюдено: `static`-методы, принимающие `AppDbContext`, устранены; прямые запросы в обработчиках вынесены в scoped-сервисы.
  - [ ] `Auth/EmailVerificationEndpoints.cs` — `CurrentUserAsync(principal, db)` использует `CurrentUserContext`.
  - [ ] `Recipes/RecipeEndpoints.cs` — `RepetitionCountsAsync(...)` и прямые запросы (`:51,163,352`) через сервис.
  - [ ] `Auth/AuthEndpoints.cs` — `MeAsync(principal, db)` и запрос (`:56`) через сервис.
- [ ] Дедупликация live-source проекции рецепта: `RecipeEndpoints` использует `ExternalRecipeContentResolver`, а не инлайн 9-полевую проекцию.
- [ ] `RecipeValidation` — общий guard «все ингредиенты пустые → пропустить» и понижение сезона вынесены в один хелпер.
- [ ] `PlanView.vue` — литерал фильтров не дублируется в `resetFilters()`.
- [ ] Общие сообщения черновиков вынесены из `useWeekDraft.js` / `useRecipeDraft.js`.
- [ ] Провайдерная развилка `IsRelational()` не повторяется в пяти местах — один seam.
- [ ] `EmailVerificationService` / `PasswordResetService` — общий хелпер cooldown→issue→enqueue.
- [ ] `RecipeMutationService` — guard «load-notfound-external-revision» вынесен в один метод.

## Spec

- [ ] #38: закреплённый `/opt/menu/current-release` имеет приоритет над `IMAGE_TAG` из `server.conf` (или шаблон не задаёт `IMAGE_TAG=latest`).
- [ ] #54: поздний `GET /share` старого рецепта не заполняет состояние нового ресурса (`useRecipeShare`/`RecipeDetailView`).

## Notes

- Партия ревью — один worktree `ticket/76-review-fixes-36-74`; тесты backend в SDK-контейнере, PostgreSQL-гарантии через `scripts/test-postgres.sh`, фронт через `npm run build`.
