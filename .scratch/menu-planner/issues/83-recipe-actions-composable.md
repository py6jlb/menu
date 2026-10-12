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

- [ ] `composables/useRecipeAction.js` покрыт тестами: принятые статусы → `onSuccess`; `409` → `conflict` + ошибка; прочий статус → серверный текст/фолбэк; сеть → `SERVER_UNAVAILABLE`; устаревший `key` → результат игнорируется; `pending` переключается.
- [ ] `RecipeDetailView` использует три инстанса: `onDelete`/`onRemoveExternal` (успех → `router.push('/recipes')`, конфликт → баннер), `onCopy` (успех → замена `recipe`, конфликт → `copyError`).
- [ ] `SharedRecipeView.onAdd` использует инстанс (успех `201`/`409` → `router.push('/recipes/{id}')`, ошибка → `addError`; без ревизии/конфликта).
- [ ] `markActionConflict`/`reloadAfterConflict` и дублирующие обработчики удалены из view; `resetResourceState` чистит инстансы.
- [ ] `npm run test` и `npm run build` зелёные; поведение и тексты не изменились (кроме канонизации из #82).

## Evidence

_(заполняется при реализации)_

## Comments
