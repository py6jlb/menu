# 74: Разделить stateless-доменные правила и db-зависимые сервисы

**What to build:** В backend один явный принцип: чистая функция/константы — `static` без состояния; всё, что читает БД или держит ресурс, — scoped-сервис через DI. Нечестные имена `*Service` у доменных правил заменены, `TokenVersionValidator` перестаёт быть service locator.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] Доменные правила переименованы по смыслу: `ExternalRecipeStateRules`, `RepetitionRules`, `AuthCodePolicy`; `static` остаётся только для чистых функций и констант.
- [ ] Хелперы, читающие БД, вынесены в scoped-сервисы с DI: `CurrentUserContext` (членство семьи), `SourceFamilyNameResolver`, загрузчик источников внешнего рецепта, `ExternalRecipeStateResolver`, `RepetitionCounter`.
- [ ] Service locator в `TokenVersionValidator` устранён: scoped `AuthSessionValidator(AppDbContext)` + чистая проверка версии токена; событие JwtBearer получает сервис явно.
- [ ] Поведение, HTTP-контракты, роуты и схема БД не меняются; существующие тесты проходят без изменения смысла.
- [ ] Добавлены unit-тесты на новые сервисы (InMemory) и на чистую проверку версии токена.
- [ ] Правило «static = stateless, БД = DI» зафиксировано в `CONTEXT.md`.
- [ ] Прогоны: backend fast suite и `scripts/test-postgres.sh` зелёные; `has-pending-model-changes` — no changes.
- [ ] A1 (переименование `AuthCodeService`) — отдельным коммитом от остального рефакторинга.
