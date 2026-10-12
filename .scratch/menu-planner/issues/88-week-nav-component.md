# 88: Общий WeekNav и подписи дня/приёма

**What to build:** Разметка недельной навигации и `aria-label` перестают дублироваться между `PlanView` и `ShoppingView`; подписи дня и приёма переезжают в `constants/plan.js`.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

## Design (из grilling)

- `constants/plan.js`: `dayLabel(day)` (`DAYS[day]?.label || \`День ${day+1}\``) и `mealLabel(code)` (`MEALS.find(...)?.label || code`).
- `components/WeekNav.vue`: презентационный, props `label`, emits `prev`/`next`; кнопки `←`/`→` с `aria-label="Предыдущая неделя"`/`"Следующая неделя"` и `<span class="week-label">`.
- `PlanView` заменяет разметку на `<WeekNav>`; её `goToWeek` (с проверкой dirty из `useWeekDraft`) остаётся, только вызывается из `@prev`/`@next`.
- `ShoppingView` то же; локальные `dayLabel`/`mealLabel` удаляются, `goToWeek` из `useShoppingList` остаётся.
- Scope: `slot*`-подписи плана и `groups` покупок не трогаем; `weekRangeLabel` уже общий.

## Checklist

- [ ] `dayLabel`/`mealLabel` в `constants/plan.js` покрыты тестами (`constants/plan.test.js`).
- [ ] `WeekNav.vue` добавлен; обе вью используют его; дублированная разметка и `aria-label` удалены.
- [ ] `ShoppingView` не имеет локальных `dayLabel`/`mealLabel`; `PlanView` берёт `mealLabel` из констант.
- [ ] Навигация плана по-прежнему уважает `dirty`-проверку (уход со страницы/смена недели).
- [ ] `npm run test` и `npm run build` зелёные; видимый текст и поведение не изменились.

## Evidence

_(заполняется при реализации)_

## Comments
