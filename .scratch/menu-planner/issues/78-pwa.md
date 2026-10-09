# 78: PWA — установка, офлайн-оболочка и офлайн-чтение

**What to build:** Приложение устанавливается на телефон/десктоп, открывается без сети и показывает последние просмотренные данные (список покупок, план, рецепты) только для чтения; версия обновляется по запросу, кэш данных чистится при выходе.

**Blocked by:** None (can start immediately)

**Status:** resolved (ticket/78-pwa)

- [x] Web App Manifest и иконки: устанавливаемое приложение («Меню», standalone, кремовый theme/background).
- [x] Service worker (vite-plugin-pwa, generateSW) прекэширует оболочку и шрифты; обновление по запросу.
- [x] Офлайн-чтение: network-first с fallback на кэш для allowlist читающих GET; `/api/auth/*`, `/api/admin/*` и управление ссылками не кэшируются.
- [x] Офлайн-UX: глобальный баннер «нет сети» и пометка «данные на момент последнего просмотра» на списке покупок и плане.
- [x] Кэш данных чистится при выходе и при 401; статика/оболочка не затрагиваются.
- [x] Nunito хостится локально (@fontsource) и попадает в прекэш.
- [x] Отдача `sw.js`/`manifest.webmanifest`/`index.html` — no-cache; хешированные ассеты — immutable; smoke проверяет манифест и SW.

## Evidence

- Манифест: `frontend/src/pwa/manifest.js` (`PWA_MANIFEST`) — «Меню для домохозяек»/«Меню», `standalone`, `start_url`/`scope` `/`, `theme`/`background` `#faf6f0`, `lang: ru`, иконки 192/512/maskable. Иконки — монограмма «М» из `frontend/pwa-icon.svg`, разово сгенерированы `@vite-pwa/assets-generator@2.0.0` (preset minimal) в `frontend/public/`; `index.html` получил favicon/apple-touch-icon и apple-мета, Google Fonts убран.
- Service worker: `vite-plugin-pwa` 2.0.0 (`generateSW`), `registerType: 'prompt'`, `injectRegister: false`; регистрация и баннер обновления — `UpdatePrompt.vue` через `virtual:pwa-register/vue`. Прекэш оболочки и локального Nunito (`globPatterns` с `woff2`/`png`), `navigateFallback: '/index.html'`, `cleanupOutdatedCaches`.
- Офлайн-чтение: самодостаточный предикат `isCacheableApiRequest` и `API_CACHE_NAME = 'menu-api'` (`frontend/src/pwa/apiCache.js`); runtime-caching `NetworkFirst`, `networkTimeoutSeconds: 3`, `maxEntries: 50`, `maxAgeSeconds: 7 дней`, кэшируются только 200. Аутентификация, админка, управление ссылками и подбор исключены.
- Офлайн-UX: `useOnlineStatus` + глобальный баннер в `App.vue`; пометки «данные на момент последнего просмотра» в `ShoppingView` и `PlanView`.
- Очистка кэша: `clearApiCache()` вызывается в `clearSession`, `invalidateSession` и при выходе в другой вкладке (`stores/auth.js`); `caches.delete('menu-api')`.
- Шрифты: `@fontsource/nunito` (кириллица + латиница, 400/600/700/800) через `src/fonts.css`; ассеты хешируются и попадают в прекэш.
- Инфраструктура: `nginx.conf` и `nginx.prod.conf` отдают `sw.js`/`manifest.webmanifest`/`index.html` с `Cache-Control: no-cache`, `/assets/` — `immutable`; `deploy/smoke.sh` проверяет манифест и SW (200 + no-cache) и 404 на отсутствующий хешированный ассет.
- Документация: ADR `docs/adr/0015-pwa-offline-read.md`, термины «Офлайн-оболочка» и «Офлайн-кэш данных» в `CONTEXT.md`, раздел «PWA и офлайн» в `README.md`.
- Тесты: frontend 240 passed (новые — `manifest.test.js`, `apiCache.test.js`, `useOnlineStatus.test.js`, `auth.test.js` +2); сборка в `node:24-alpine` даёт `sw.js` + `workbox-*.js` + `manifest.webmanifest` (22 записи прекэша), `npm ci` воспроизводим.
