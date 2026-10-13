# 93: Frontend — composables по фичам

**What to build:** `frontend/src/composables/` (30 файлов) раскладывается по фичам; импорты в композаблах, вью и тестах обновляются. Логика не меняется.

**Blocked by:** None (can start immediately).

**Status:** resolved (ticket/93-frontend-composables-folders)

## Карта

```
composables/
  recipes/   useRecipeDraft, useRecipePicker, useIngredientAutocomplete
  plan/      useWeekDraft
  shopping/  useShoppingList
  family/    useFamily
  settings/  useSettings
  session/   useSession
  share/     useRecipeShare
  common/    useRequestTracker, useRouteResource, useDialog, useCooldown,
             useOnlineStatus, draftMessages
```
`.test.js` переезжают рядом с модулями. `api/`, `views/`, `constants/` не трогаем (кроме обновления путей импорта composables).

## Checklist

- [x] Модули и их `.test.js` перемещены по карте.
- [x] Импорты обновлены во всех потребителях (`views/`, другие composables).
- [x] Логика и видимые тексты не изменены.
- [x] `npm run test` (тот же счёт) и `npm run build` зелёные.

## Evidence

Перемещение (через `git mv`, 30 файлов, `.test.js` рядом с модулями):

```
composables/
  recipes/   useRecipeDraft(+test), useRecipePicker(+test),
             useIngredientAutocomplete(+test), useRecipeAction(+test)
  plan/      useWeekDraft(+test)
  shopping/  useShoppingList(+test)
  family/    useFamily(+test)
  settings/  useSettings(+test)
  session/   useSession(+test)
  share/     useRecipeShare(+test)
  common/    useRequestTracker(+test), useRouteResource(+test), useDialog(+test),
             useCooldown, useOnlineStatus(+test), draftMessages
```

Отклонение от карты: `useRecipeAction` в тикетной карте пропущен, но лежит в
`composables/` и по смыслу (оркестрация действия над рецептом) отнесён в
`recipes/` вместе со своим `.test.js`. Иначе остался бы единственный модуль вне
фичевых папок.

Обновлённые импорты (diff — только строки import/vi.mock/dynamic import):

- Внутри отсутствовавших фичевых папок относительные пути углубились на уровень:
  `../api/**`, `../constants/**`, `../stores/**` → `../../…`.
- Кросс-импорты composables: `./useRequestTracker` → `../common/useRequestTracker`
  (5 модулей), `./draftMessages` → `../common/draftMessages`
  (`useRecipeDraft`, `useWeekDraft`). В `common/useRouteResource.js`
  `./useRequestTracker` остался как есть (тот же каталог).
- `vi.mock('../api/**')` в `useFamily.test.js`, `useRecipeShare.test.js`,
  `useSession.test.js` и dynamic `import('../stores/auth')`/`import('../api/auth')`
  в `useSession.test.js` — переписаны на `../../`.
- Потребители вне composables: `App.vue`, `router/index.js`,
  `views/*.vue` (13 файлов) — `composables/<useX>` → `composables/<feature>/<useX>`.
- `api/`, `constants/`, `stores/`, `views/` логика и тексты не менялись.

Проверки (из `frontend/`, `nvm use 22`, Node v22.21.1):

- `npm ci` — зависимости установлены.
- `npm run test` → `Test Files 24 passed (24)`, `Tests 283 passed (283)`.
- `npm run build` → `✓ built`, PWA SW сгенерирован, ошибок сборки нет.

Примечание: Node 25 ломает `localStorage.clear` в jsdom-окружении (19 падений в
`useSession.test.js`/`stores/auth.test.js`) — это не связано с переносом;
на Node 22 всё зелёное.
