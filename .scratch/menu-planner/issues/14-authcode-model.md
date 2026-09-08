# 14: Модель AuthCode и поля User

**What to build:** Модель одноразовых кодов и расширение пользователя. Таблица `AuthCode` (UserId, тип verify/reset, код, expires, used), поля на `User` (IsEmailVerified/EmailVerifiedAt, VerificationAttempts, LockedUntil, TokenVersion). Генерация и хэширование 6-значного кода (PasswordHasher), общая логика создания/проверки/сгорания/expiry кодов.

**Blocked by:** 12 (Миграция на .NET 10), 13 (SMTP-инфраструктура и письма)

**Status:** resolved (commit `e0d3a6a`)

- [x] Таблица `AuthCode`: UserId, тип (`verify`/`reset`), код, `expires`, `used`
- [x] Поля на `User`: `IsEmailVerified`/`EmailVerifiedAt`, `VerificationAttempts`, `LockedUntil`, `TokenVersion`
- [x] Генерация 6-значного кода, хэширование (PasswordHasher)
- [x] Общая логика: создание, проверка, одноразовость (сгорание), срок жизни
- [x] Юнит-тесты сервиса кодов