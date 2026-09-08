# 22: Frontend-проект menu в Amvera

**What to build:** Развернуть Vue SPA в Amvera как проект `menu` (тариф «Начальный», Москва). `amvera.yaml` с окружением Node browser (артефакт `dist/*`), привязка GitHub webhook, деплой и проверка, что SPA отдаётся с маршрутизацией.

**Blocked by:** 21 (Backend-проект menu-api в Amvera)

**Status:** ready-for-agent

- [ ] `frontend/amvera.yaml`: meta.environment node, toolchain browser, artifacts `dist/*`
- [ ] Привязан GitHub webhook к проекту `menu`
- [ ] Деплой выполнен, SPA отдаётся по домену `menu.<user>.amvera.io`
- [ ] SPA-маршрутизация (vue-router) работает при прямом переходе по URL