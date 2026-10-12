# 89: Один helper запроса в `api/` (Speculative)

**What to build:** Формирование запроса (JSON-тело, revision-query) сводится к хелперам `api/client.js`; вью перестают ходить мимо `api/` с хардкодом путей.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

## Design (из grilling)

- `api/client.js`: `postJson(path, body)` / `putJson(path, body)` (сами делают `JSON.stringify`) над `apiJson`; `revisionQuery(param, revision)` (`''` или `?param=value`).
- `api/auth.js`: добавить `login(email, password)`, `register(email, password)`.
- `recipes.js` → `revisionQuery('revision', r)`, `plans.js` → `revisionQuery('expectedRevision', r)`; `families.js`, `settings.js`, `auth.js`, `admin.js` — на `postJson`/`putJson`.
- `LoginView`/`RegisterView` зовут `login`/`register` из `api/auth.js` (никаких хардкодных путей); `HomeView` использует `apiJson('/health')` вместо сырого `fetch`.
- Надёжность/поведение не меняются — это консистентность (кандидат Speculative).

## Checklist

- [ ] `postJson`/`putJson`/`revisionQuery` в `api/client.js`.
- [ ] `login`/`register` в `api/auth.js`; `LoginView`/`RegisterView` переведены.
- [ ] `HomeView` ходит через client.
- [ ] `recipes.js`/`plans.js`/`families.js`/`settings.js`/`auth.js`/`admin.js` используют хелперы; `JSON.stringify` и inline revision-query не дублируются.
- [ ] `api/client.test.js`: `revisionQuery` (чистый), `postJson` кладёт JSON + `Content-Type: application/json`, `FormData` не оборачивается (мок `fetch`).
- [ ] `npm run test` и `npm run build` зелёные.

## Evidence

_(заполняется при реализации)_

## Comments
