# 17: Админ-разблокировка (бэкенд)

**What to build:** Endpoint для админа: сброс счётчика попыток и `LockedUntil` у пользователя по email. Роль Admin, rate limit 10/мин на админа. Форма запроса от пользователя — позже (вне рамок).

**Blocked by:** 14 (Подтверждение почты (бэкенд))

**Status:** ready-for-agent

- [ ] `POST /api/admin/unlock` (роль Admin), тело `{ email }` — сбрасывает `VerificationAttempts` и `LockedUntil`
- [ ] Rate limit 10/мин на админа
- [ ] Интеграционные тесты разблокировки (включая доступ только админа)