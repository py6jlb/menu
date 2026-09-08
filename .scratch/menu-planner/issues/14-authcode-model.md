# 14: Модель AuthCode и поля User

**What to build:** Модель одноразовых кодов и расширение пользователя. Таблица `AuthCode` (UserId, тип verify/reset, код, expires, used), поля на `User` (IsEmailVerified/EmailVerifiedAt, VerificationAttempts, LockedUntil, TokenVersion). Генерация и хэширование 6-значного кода (PasswordHasher), общая логика создания/проверки/сгорания/expiry кодов.

**Blocked by:** 12 (Миграция на .NET 10), 13 (SMTP-инфраструктура и письма)

**Status:** ready-for-agent

- [ ] Таблица `AuthCode`: UserId, тип (`verify`/`reset`), код, `expires`, `used`
- [ ] Поля на `User`: `IsEmailVerified`/`EmailVerifiedAt`, `VerificationAttempts`, `LockedUntil`, `TokenVersion`
- [ ] Генерация 6-значного кода, хэширование (PasswordHasher)
- [ ] Общая логика: создание, проверка, одноразовость (сгорание), срок жизни
- [ ] Юнит-тесты сервиса кодов