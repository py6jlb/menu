# 51: Последовательные права на ссылки на рецепт

**What to build:** Подтверждённый участник создаёт ссылку на рецепт, Владелец семьи отзывает или перегенерирует её; чтение существующей ссылки не создаёт публичный доступ. Неподтверждённый пользователь действительно остаётся в режиме чтения.

**Blocked by:** 37 — Проверка критических гарантий на PostgreSQL.

**Status:** resolved (ticket/51-consistent-share-permissions; commit d7e90ed)

- [x] Чтение существующей ссылки и явное создание разделены; GET не создаёт новую запись и не открывает публичный доступ.
- [x] Создание/отзыв/перегенерация требуют подтверждённой почты и актуального членства; отзыв/перегенерация дополнительно проверяют Владельца семьи.
- [x] Внешний рецепт нельзя шарить транзитивно; чужой рецепт и пользователь без семьи не раскрывают его содержимое.
- [x] UI использует явную операцию создания, показывает ограничения неподтверждённой почты и не создаёт ссылку при автоматической загрузке страницы.
- [x] Параллельное первое создание сохраняет одну ссылку и даёт предсказуемый ответ вместо 500; повтор и конкуренция revoke/regenerate имеют согласованный результат.
- [x] Сохраняется семантика ADR: отзыв/перегенерация запрещают новые добавления по старому токену, существующий внешний рецепт продолжает читать живой источник с предупреждением.
- [x] Матрица anonymous/unverified/member/owner/чужая семья/external проверена сквозными сценариями, уникальность и гонка создания — на PostgreSQL.
- [x] Документация HTTP-контракта и смысла отзыва согласована с интерфейсом.

## Evidence

- Права и жизненный цикл вынесены в `backend/src/MenuPlanner.Api/Recipes/RecipeSharingService.cs` (scoped в DI): `GetAsync` только читает, `CreateAsync` создаёт явно и переживает гонку уникального индекса `RecipeId`, `RevokeAsync` идемпотентен, `RegenerateAsync` на реляционной БД — условное обновление по наблюдённому токену (проигравшие не выпускают свой токен).
- `RecipeShareEndpoints.cs`: `GET` — чтение, `POST /share` — создание, `DELETE` и `POST /regenerate` — под `RequireVerifiedEmail()`; внешний рецепт → `403`, чужой/без семьи → `404`.
- Frontend: `frontend/src/api/recipes.js` — `createRecipeShare`; `useRecipeShare` в `frontend/src/composables/useRecipeShare.js`; `RecipeDetailView.vue` грузит существующую ссылку на mount, создаёт по кнопке, дизейблит создание без подтверждённой почты.
- Матрица (сквозные HTTP-сценарии, `RecipeShareFlowTests.cs`): anonymous/без токена → `401`; unverified create/revoke/regenerate → `403`; member create, но revoke/regenerate → `403`; owner revoke/regenerate; чужая семья и без семьи → `404`; external (транзитивно) → `403`; GET без ссылки → `404` и ноль записей.
- PostgreSQL (`Postgres/PostgresRecipeShareTests.cs`, `AsyncBarrier` без sleep): параллельное первое создание → одна ссылка, один `Created`, одинаковый токен; конкурентные revoke сходятся к `revoked`; конкурентные regenerate — к одному токену; revoke+regenerate оставляют одну согласованную ссылку. Гонка на endpoint также покрыта in-memory инъекцией (`Create_RaceOnUniqueIndex_ReturnsExistingLinkInsteadOfError`).
- Семантика ADR-0005 сохранена: тесты `ExternalRecipeStateFlowTests`/`ExternalRecipeImportTests` подтверждают, что после отзыва/перегенерации новые добавления по старому токену невозможны, а существующий внешний рецепт продолжает читать живой источник с предупреждением (`state=warning`).
- Документация: `README.md` (таблица `/api/recipes/{id}/share`) и `docs/adr/0005-external-recipes-across-families.md` приведены в соответствие интерфейсу.

### Команды и результаты

- `dotnet test` (SDK-контейнер, fast): `Passed: 403, Skipped: 36, Total: 439`.
- `scripts/test-postgres.sh`: `Passed: 39, Skipped: 0, Total: 39`.
- frontend (`node:24-alpine`, `npm ci && npm test && npm run build`): 23 теста (8 новых `useRecipeShare`), сборка успешна.
- `dotnet ef migrations has-pending-model-changes`: `No changes have been made to the model since the last migration.` (миграция не требуется).
