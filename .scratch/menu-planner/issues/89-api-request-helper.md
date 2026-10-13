# 89: Один helper запроса в `api/` (Speculative)

**What to build:** Формирование запроса (JSON-тело, revision-query) сводится к хелперам `api/client.js`; вью перестают ходить мимо `api/` с хардкодом путей.

**Blocked by:** None (can start immediately)

**Status:** resolved (ticket/89-api-request-helper)

## Design (из grilling)

- `api/client.js`: `postJson(path, body)` / `putJson(path, body)` (сами делают `JSON.stringify`) над `apiJson`; `revisionQuery(param, revision)` (`''` или `?param=value`).
- `api/auth.js`: добавить `login(email, password)`, `register(email, password)`.
- `recipes.js` → `revisionQuery('revision', r)`, `plans.js` → `revisionQuery('expectedRevision', r)`; `families.js`, `settings.js`, `auth.js`, `admin.js` — на `postJson`/`putJson`.
- `LoginView`/`RegisterView` зовут `login`/`register` из `api/auth.js` (никаких хардкодных путей); `HomeView` использует `apiJson('/health')` вместо сырого `fetch`.
- Надёжность/поведение не меняются — это консистентность (кандидат Speculative).

## Checklist

- [x] `postJson`/`putJson`/`revisionQuery` в `api/client.js`.
- [x] `login`/`register` в `api/auth.js`; `LoginView`/`RegisterView` переведены.
- [x] `HomeView` ходит через client.
- [x] `recipes.js`/`plans.js`/`families.js`/`settings.js`/`auth.js`/`admin.js` используют хелперы; `JSON.stringify` и inline revision-query не дублируются.
- [x] `api/client.test.js`: `revisionQuery` (чистый), `postJson` кладёт JSON + `Content-Type: application/json`, `FormData` не оборачивается (мок `fetch`).
- [x] `npm run test` и `npm run build` зелёные.

## Evidence

- `api/client.js`: `postJson`/`putJson` (JSON-тело поверх `apiJson`), `revisionQuery(param, revision)` (`''`/`?param=value`; `0` — валидная ревизия).
- `api/auth.js`: `login`/`register`; `LoginView`/`RegisterView` без хардкода путей; `HomeView` — `apiJson('/health')` вместо сырого `fetch`.
- Модули `recipes.js` (`revisionQuery('revision', …)`)/`plans.js` (`revisionQuery('expectedRevision', …)`)/`families.js`/`settings.js`/`auth.js`/`admin.js` переведены; `FormData`-загрузки не оборачиваются.
- Тесты: `api/client.test.js` (5). `npm run test` — 283 passed (24 files); `npm run build` — успешно (Node 22.21.1, PWA SW собран).

## Comments
