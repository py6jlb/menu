# 93: Frontend — composables по фичам

**What to build:** `frontend/src/composables/` (30 файлов) раскладывается по фичам; импорты в композаблах, вью и тестах обновляются. Логика не меняется.

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

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

- [ ] Модули и их `.test.js` перемещены по карте.
- [ ] Импорты обновлены во всех потребителях (`views/`, другие composables).
- [ ] Логика и видимые тексты не изменены.
- [ ] `npm run test` (тот же счёт) и `npm run build` зелёные.

## Evidence

_(заполняется при реализации)_
