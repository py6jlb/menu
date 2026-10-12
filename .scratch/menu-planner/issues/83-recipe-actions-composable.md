# 83: Вынести оркестрацию действий рецепта в `useRecipeAction`

**What to build:** Четыре почти одинаковых обработчика действий рецепта (`onDelete`, `onRemoveExternal`, `onCopy`, `onAdd` в `SharedRecipeView`) переезжают из `.vue` в один deep composable `useRecipeAction`, покрытый тестами; view остаётся разметкой, подтверждениями и навигацией.

**Blocked by:** 82 — Единый трекер запросов и словарь сообщений на фронте.

**Status:** ready-for-agent

## Design (из grilling)

- `useRecipeAction()` — **инстанс на действие**; держит свои `pending`/`error`/`conflict`/`revision`, поэтому три разных места показа `copyError` не ломаются, а состояние и его тесты локализованы.
- Interface: `run(request, { key, onSuccess, acceptedStatuses }) → outcome`, где `request` — async-вызов, `key` — identity ресурса (id/токен, страж через `useRequestTracker` из #82), `onSuccess(data)` вызывается на принятых статусах.
- Потребляет `useRequestTracker`, `revisionOf`, `constants/messages.js` из #82.
- Конфликт `409` → `conflict = { revision: revisionOf(data) }` + текст; кнопка «перечитать» зовёт существующий `load()` из `useRouteResource`.
- View владеет `window.confirm`, `router.push`/заменой ресурса (через `onSuccess`) и размещением ошибок.
- Четыре инстанса: delete, removeExternal, copy в `RecipeDetailView`; import в `SharedRecipeView`.

## Checklist

- [x] `composables/useRecipeAction.js` покрыт тестами: принятые статусы → `onSuccess`; `409` → `conflict` + ошибка; прочий статус → серверный текст/фолбэк; сеть → `SERVER_UNAVAILABLE`; устаревший `key` → результат игнорируется; `pending` переключается.
- [x] `RecipeDetailView` использует три инстанса: `onDelete`/`onRemoveExternal` (успех → `router.push('/recipes')`, конфликт → баннер), `onCopy` (успех → замена `recipe`, конфликт → `copyError`).
- [x] `SharedRecipeView.onAdd` использует инстанс (успех `201`/`409` → `router.push('/recipes/{id}')`, ошибка → `addError`; без ревизии/конфликта).
- [x] `markActionConflict`/`reloadAfterConflict` и дублирующие обработчики удалены из view; `resetResourceState` чистит инстансы.
- [x] `npm run test` и `npm run build` зелёные; поведение и тексты не изменились (кроме канонизации из #82).

## Evidence

Реализовано в worktree `.worktrees/83-recipe-actions-composable`, ветка `ticket/83-recipe-actions-composable`.

**Файлы:**
- Добавлен `frontend/src/composables/useRecipeAction.js` — инстанс на действие: `pending`/`error`/`conflict`, `run(request, { key, acceptedStatuses, onSuccess, fallbackMessage })`, `reset()`. Страж identity — `useRequestTracker` + запомненный `activeKey`; `409` → `conflict = { revision: revisionOf(data) }` + `data?.error || CONFLICT`; сеть → `SERVER_UNAVAILABLE`; принятый статус → `onSuccess(data)`; прочий → `data?.error || fallbackMessage`.
- Добавлен `frontend/src/composables/useRecipeAction.test.js` — 13 тестов (принятые статусы, 200/201, 409 с ревизией/без, сеть, `fallbackMessage`, `pending`, страж старого ключа, `reset`).
- Изменён `frontend/src/views/RecipeDetailView.vue` — три инстанса (delete/removeExternal/copy), `actionError`/`actionConflict`/`actionRevision` — computed над инстансами; `markActionConflict`/`reloadAfterConflict` удалены, кнопка «перечитать» зовёт `load()` из `useRouteResource`; `resetResourceState` чистит инстансы; три места `copyError` сохранены.
- Изменён `frontend/src/views/SharedRecipeView.vue` — `onAdd` через инстанс (`acceptedStatuses: [201, 409]`, `onSuccess` → `router.push('/recipes/{id}')`); `resetResourceState` чистит инстанс.

**Проверки (Node v22.21.1, хост-Node v18 обойдён через nvm; `npm ci` из-за отсутствия `node_modules` в worktree):**
- `npm run test` — 22 файла, 273 теста, все зелёные (13 новых из `useRecipeAction.test.js`).
- `npm run build` — успешно, включая PWA-шаг (`precache 22 entries`, `dist/sw.js`). На PWA-шаге не падало; сборка подтверждена Node ≥ 20 (v22.21.1).

## Comments

