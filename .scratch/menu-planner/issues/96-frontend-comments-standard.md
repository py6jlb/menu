# 96: Frontend — комментарии к одному виду (JSDoc на экспортах)

**What to build:** Привести комментарии frontend к единому стандарту: у каждого экспортируемого модуля/composable/функции есть JSDoc-контракт; инлайн-комментарии — только «почему». Логика не меняется.

**Blocked by:** None (can start immediately)

**Status:** resolved (ticket/96-frontend-comments-standard)

## Стандарт

- `frontend/src/**`: `/** … */` на каждом экспортируемом модуле/composable/функции с описанием контракта (что гарантирует, ключевые инварианты), по-русски.
- Инлайн `//` — только «почему»/инварианты/гонки; пересказы кода («что») удалить.
- `.vue`-компоненты: док-комментарии не добавляем (только «почему»-инлайн при необходимости).
- Тесты (`.test.js`) — без док-комментариев.
- Поведение и видимые тексты не меняются.

## Checklist

- [x] Экспортируемые сущности в `composables/`, `api/`, `constants/`, `stores/`, `router/`, `pwa/` имеют JSDoc-контракт.
- [x] Пересказы кода удалены, «почему»-комментарии сохранены.
- [x] Логика не изменена; `.vue` док-комментариями не обвешаны.
- [x] `npm run test` (283) и `npm run build` зелёные.

## Evidence

**Инвентаризация.** Аудит `frontend/src/{composables,api,constants,stores,router,pwa}` (`.test.js`
исключены) нашёл экспортируемые функции/композаблы/модули без `/** … */`. Добавлено **89**
JSDoc-блоков в **26** файлах (+118 / −7 строк); все — только комментарии.

**Что добавлено.** JSDoc-контракт (1–3 строки, по-русски) на:
- `api/`: `admin`, `auth` (7 функций), `client` (`apiFetch`, `apiJson`), `families` (5), `ingredients`,
  `plans` (3), `recipes` (18 обёрток), `settings` (2), `shopping` — по одному описанию на функцию.
- `constants/`: таблицы (`DAYS`, `MEALS`, `UNITS`, `SEASONS`, `DIETS`, `CATEGORIES`, `OTHER_CATEGORY`,
  `EXTERNAL_STATES`, группы `PHOTO_*`/`DOCUMENT_*`), словарь `messages` и функции-подписи
  (`mondayOf`, `toIso`, `parseIso`, `parseList`/`joinList`, `unitLabel`/`seasonLabel`/`dietLabel`,
  `externalState`, `isValidWindowWeeks`, `repetition*` и т.д.).
- `stores/`: `useAuth`, `localStore`/`sessionStore`; `router/`: default-export `router`;
  `pwa/`: `API_CACHE_NAME`.
- `composables/`: `useCooldown`-хелперы (`RESEND_COOLDOWN_SECONDS`, `parseCooldownSeconds`),
  `draftMessages.LEAVE_MESSAGE`, `useFamily` (группа текстов ошибок), `useWeekDraft`
  (`RELOAD_CONFIRM_MESSAGE`, `slotKey`, `draftFromEntries`), `useIngredientAutocomplete`
  (`AUTOCOMPLETE_DELAY`), `useRecipeDraft` (группа текстов сохранения, `newIngredientDraft`),
  `useRecipePicker` (`BROKEN_SELECTION_MESSAGE`, `emptyFilters`, `hasAnyFilter`, `toPickerItem`).

Модули с корректным JSDoc не трогались: `api/revision`, `pwa/manifest`, `router/returnTo`,
`stores`-док `SESSION_STATUS`, `useDialog`, `useOnlineStatus`, `useRequestTracker`, `useRouteResource`,
`useRecipeAction`, `useRecipeShare`, `useSession`, `useSettings`, `useShoppingList`.

**Инлайн `//`.** Просмотрены все инлайн-комментарии в целевых каталогах и `.vue`. Чистых
пересказов кода («что») не осталось; «почему»/инварианты/гонки (поздние ответы, ревизии,
фокус-ловушка, очистка кэша, страж токена и т.п.) сохранены. **7** описательных `//`-заголовков
существующих экспортов повышены до JSDoc без потери текста: `draftMessages.LEAVE_MESSAGE` (1),
`constants/messages` (3 строки модульного описания), `constants/recipe` (`CATEGORIES`, `OTHER_CATEGORY`).
В `.vue` и тестах док-комментарии не добавлялись; их инлайн-комментарии — «почему» — не тронуты.

**Проверка** (в `frontend/`, nvm Node 22):

```
$ npm run test
 Test Files  24 passed (24)
      Tests  283 passed (283)

$ npm run build
✓ built in 875ms
PWA v2.0.0 ... files generated
```

**Границы.** Изменены только комментарии в `frontend/src/{api,composables,constants,stores,router,pwa}`.
Логика, тексты сообщений, порядок импортов, `.vue` и `*.test.js` не менялись.

**Отклонение/примечание.** `package.json` объявляет `engines.node: >=24 <25`, но на хосте есть
только 18/22/25; по указанию тикета проверки выполнены на **nvm Node 22.21.1** (Vite 6 поддерживает
Node 22), `npm ci` прошёл. Аудит-скрипт: у всех `export`-сайтов есть предшествующий `/** … */`,
кроме намеренных членов задокументированной группы (`messages`, `useRecipeDraft`-тексты).
