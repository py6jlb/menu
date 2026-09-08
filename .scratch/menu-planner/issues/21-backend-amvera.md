# 21: Backend-проект menu-api в Amvera

**What to build:** Развернуть .NET 10 backend в Amvera как проект `menu-api` (тариф «Начальный Плюс», Москва). Dockerfile на порт 80 (bind 0.0.0.0), `amvera.yaml` (build.dockerfile → backend/Dockerfile, containerPort 80, persistenceMount /data), привязка GitHub webhook, env/секреты (строка подключения БД, JWT, `PHOTOS_DIR=/data/photos`, SMTP из тикетов 13-18), деплой и проверка `/health`.

**Blocked by:** 20 (Аккаунт Amvera и managed PostgreSQL), 19 (Фронтенд: подтверждение, восстановление, баннер, read-only, админ-раздел)

**Status:** ready-for-agent

- [ ] Backend слушает порт 80 (0.0.0.0) в проде, dev остаётся 8080
- [ ] `backend/amvera.yaml`: build.dockerfile, containerPort 80, persistenceMount /data
- [ ] `PHOTOS_DIR=/data/photos` (постоянное хранилище Amvera)
- [ ] Привязан GitHub webhook к проекту `menu-api`
- [ ] env/секреты: БД, JWT, PHOTOS_DIR, SMTP
- [ ] Деплой выполнен, `/health` возвращает ok
- [ ] Домен `menu-api.<user>.amvera.io` доступен по HTTPS