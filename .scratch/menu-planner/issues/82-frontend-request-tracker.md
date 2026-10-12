# 82: Единый трекер запросов и словарь сообщений на фронте

**What to build:** Восемь реализаций «поздний ответ не должен применяться» сводятся к одному module `useRequestTracker`; тексты ошибок — к одному словарю `constants/messages.js`; разбор ревизии — к `api/revision.js`. `loading`/`error` и их различия между сценариями не меняются.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

## Design (из grilling)

- `useRequestTracker()`: `begin(key?) → token`, `isCurrent(token)`, `reset(key?)`. Монотонный счётчик **на ключ** (Map), поэтому одним interface покрываются «нет ключа» (settings/picker), «неделя» (shopping/week), «id» (share/draft) и «строка» (autocomplete).
- `constants/messages.js`: канонические `LOAD_FAILED`, `SAVE_FAILED`, `SERVER_UNAVAILABLE`, `NOT_FOUND`, `CONFLICT`. По-модульные константы (`SHOPPING_LOAD_ERROR_MESSAGE`, `SETTINGS_SAVE_ERROR`, `RESOURCE_LOAD_ERROR`, …) удаляются.
- `api/revision.js`: `revisionOf(data)` вместо ~10 копий `Number.isInteger(x?.revision) ? x.revision : null`.
- Scope — только composables + `RecipeDetailView` в части `revisionOf`; auth-экраны не трогаем.

## Checklist

- [ ] `composables/useRequestTracker.js` с unit-тестами: поздний ответ отбрасывается, смена ключа обесценивает старый, `reset()` обесценивает все, изоляция по ключу (строки).
- [ ] `constants/messages.js` (канон) и `api/revision.js` (`revisionOf`).
- [ ] `useRouteResource`, `useShoppingList`, `useSettings`, `useRecipeShare`, `useIngredientAutocomplete`, `useWeekDraft`, `useRecipeDraft`, `useRecipePicker` переведены на трекер; приватные `isCurrent*` удалены.
- [ ] `useRecipeDraft` и `RecipeDetailView` используют `revisionOf`; по-модульные тексты заменены каноническими.
- [ ] `loading`/`error` и их семантика (сохранение прежнего результата vs обнуление) не изменились; меняется только формулировка текстов.
- [ ] Существующие suite'ы композаблов зелёные (`npm run test`), сборка `npm run build` успешна.

## Evidence

_(заполняется при реализации)_

## Comments
