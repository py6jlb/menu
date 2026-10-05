# 45: Атомарное подтверждение почты и повторная выдача кода

**What to build:** Пользователь подтверждает почту одноразовым кодом и запрашивает новый код по таймеру; параллельные запросы не обходят ограничения, не теряют попытки и не создают несколько действующих кодов.

**Blocked by:** 37 — Проверка критических гарантий на PostgreSQL.

**Status:** resolved (commit ca02c0f)

- [x] Lifecycle выдачи, проверки, попыток, срока действия и потребления кода сосредоточен в предметном модуле; endpoint не управляет последовательностью этих инвариантов самостоятельно.
  - `Auth/EmailVerificationService.cs` владеет verify/resend/issue/unlock; `AuthEndpoints` (регистрация), `EmailVerificationEndpoints`, `AdminEndpoints` только транслируют результат в HTTP.
- [x] Коды остаются шестизначными, хранятся хэшированными и генерируются криптографически; тесты используют управляемую генерацию и время.
  - `AuthCodeService.GenerateCode()` переведён на `RandomNumberGenerator.GetInt32` (был `Random.Shared`); генерация за швом `IAuthCodeGenerator` (`RandomAuthCodeGenerator` в проде, `QueueCodeGenerator` в тестах), время — через `TimeProvider` (`FakeTimeProvider` в тестах).
- [x] Одновременная проверка одного действующего кода даёт только одно успешное потребление и предсказуемый результат второго запроса.
  - `PostgresEmailVerificationTests.ConcurrentVerify_ConsumesCodeExactlyOnce`: ровно один `Verified`, второй `AlreadyVerified`.
- [x] Параллельные неверные попытки учитываются атомарно; действующая политика блокировки подтверждения и её истечение работают без потерянных увеличений.
  - `PostgresEmailVerificationTests.ConcurrentWrongAttempts_AreCountedWithoutLoss` (5 попыток → `VerificationAttempts == 5`, ровно один `Locked`); истечение срока — fast-тесты `AuthCodeTests`/`EmailVerificationServiceTests`.
- [x] Проверка cooldown и выдача нового кода атомарны; старые коды инвалидируются, два resend не оставляют несколько действующих challenge.
  - `PostgresEmailVerificationTests.ConcurrentResend_WithCooldown_...` (один `Sent`, один `TooSoon`) и `ConcurrentResend_WithoutCooldown_...` (единственный активный код); инвалидация старых — unit/flow-тесты.
- [x] Регистрация и административная разблокировка согласованы с теми же данными попыток/кодов; конкурентная разблокировка не возвращает утраченную старую блокировку.
  - Регистрация вызывает `IssueInitialCodeAsync`, unlock — `UnlockAsync` того же модуля; `PostgresEmailVerificationTests.ConcurrentUnlock_DoesNotRestoreLostLock`.
- [x] UI различает успех, неверный/истёкший/потреблённый код, таймер и блокировку понятными русскими сообщениями; сетевой сбой снимает pending и сохраняет введённое.
  - `VerifyErrorDto(Error, Code)` с маркерами `invalid`/`expired`/`used`; `VerifyEmailView.vue` маппит их, таймер `useCooldown`, 423/409 обработаны; pending снимается в `finally`, введённый код не очищается при ошибке.
- [x] PostgreSQL-проверки управляемо воспроизводят конкуренцию verify/resend и счётчика; быстрые проверки сроков и существующие сценарии сохранены.
  - `Postgres/BarrierNonQueryInterceptor.cs` встречает операции на `FOR UPDATE` через `AsyncBarrier`; прогоны: fast suite 261 passed / 20 skipped, PostgreSQL suite 23 passed.

## Evidence

- Fast: `docker run --rm -u "$(id -u):$(id -g)" -e DOTNET_CLI_HOME=/tmp/dotnet-home -e NUGET_PACKAGES=/tmp/nuget -v "$PWD":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test` → Failed: 0, Passed: 261, Skipped: 20 (Postgres-тесты пропущены без сервера).
- PostgreSQL: `scripts/test-postgres.sh` → Failed: 0, Passed: 23.
- Frontend: `node:24-alpine npm ci && npm run build` → built successfully.
- Схема не менялась: `dotnet ef migrations has-pending-model-changes` → «No changes have been made to the model since the last migration».
