# 35: Правки по второму code-review (тикеты 25–34)

**What to build:** Устранить замечания ревью диапазона `1251e13..HEAD`: живой контент внешних рецептов в списке, мёртвый контракт импорта, дубли в бэкенде и фронте, общий компонент тела рецепта.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] `GET /api/recipes` для внешних рецептов отдаёт живой контент целиком (имя, фото, сложность, калорийность, время, порции, теги, сезонность, диета) — по спеке кэшируется только `Name`; тест
- [ ] Убрать мёртвое поле `RecipeImportResultDto.AlreadyAdded` (дубликаты идут через `409 RecipeImportConflictDto`); обновить контракт и README
- [ ] Метка «скопировано из семьи X» гарантированно проставляется при промоушене (не `null` в достижимом пути); тест
- [ ] `docs/adr/0007`: уточнить, что EF генерирует `Down()` автоматически, но down-миграции в политике не используются (forward-only)
- [ ] Фронт: общий компонент тела рецепта для `SharedRecipeView` и `RecipeDetailView` (hero-фото, мета-чипы, `detail-columns`, стили) — без выноса логики share/copy/external из view
- [ ] Фронт: composable для cooldown вместо дублей в `VerifyEmailView` и `ForgotPasswordView`
- [ ] Бэкенд: единый хелпер connection string для `Program` и `AppDbContextFactory`; дедуп `AuthorizeOwnerAsync`/`FindRecipeAsync` и `ExternalReadOnly`; общая проекция имени семьи источника
- [ ] Фронт: биндить `externalState(...)` один раз, а не 3–4 раза; консолидировать стили бейджей `warning`/`broken`/`revoked`
- [ ] `dotnet test` и `frontend build` зелёные
