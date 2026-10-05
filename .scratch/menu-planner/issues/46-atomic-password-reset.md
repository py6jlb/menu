# 46: Атомарный сброс пароля без внешне навязанной блокировки

**What to build:** Пользователь восстанавливает доступ одним действующим кодом; смена пароля и отзыв старых токенов происходят целиком. Посторонний, знающий email, не может запросами без действующего кода навязать длительную блокировку восстановления.

**Blocked by:** 45 — Атомарное подтверждение почты и повторная выдача кода.

**Status:** resolved

- [x] Сброс использует общий атомарный lifecycle кодов; выдача reset соблюдает cooldown, срок действия и единственный действующий challenge.
  - `Auth/Codes/AuthCodeLifecycle.cs` — общий lifecycle (критическая секция по строке пользователя, единственный действующий код, выдача, выбор последнего, коммит); `AuthCodeIssuer` удалён, `EmailVerificationService` переведён на тот же lifecycle, `Auth/PasswordResetService.cs` владеет reset-выдачей через него.
- [x] Потребление кода, запись нового хэша пароля и изменение версии токенов составляют одну защищённую операцию.
  - `PasswordResetService.ResetAsync` в одной транзакции с `FOR UPDATE`: `Burn` кода + `PasswordHash` + `TokenVersion++` + commit; endpoint только транслирует `PasswordResetOutcome`.
- [x] Два reset одним кодом не дают двух успешных смен; последовательные реальные смены корректно увеличивают версию, старые JWT перестают работать.
  - `Postgres/PostgresPasswordResetTests.ConcurrentReset_ConsumesCodeExactlyOnce_AndIncrementsVersionOnce` (один `Reset`, один `CodeAlreadyUsed`, `TokenVersion == 1`); `PasswordResetServiceTests.SequentialResets_IncrementTokenVersion`; flow `Reset_WithCorrectCode_ChangesPassword_AndInvalidatesIssuedTokens` (старый JWT → 401, вход старым паролем → 401).
- [x] Неверные попытки относятся к реально действующему challenge: отсутствие, истечение или уже потреблённый код не создают длительную блокировку аккаунта от анонимных запросов.
  - Счётчик `AuthCode.Attempts`; `PasswordResetServiceTests.Reset_WithoutChallenge_IsInvalid_AndDoesNotLockAccount` и `Reset_WithExpiredOrUsedChallenge_DoesNotCountAttemptsNorLock`; PostgreSQL `FiveResetsWithoutChallenge_DoNotLockAccount`.
- [x] Ограничение действующего перебора не ослаблено: превышение попыток закрывает конкретный challenge; после штатного cooldown пользователь может получить новый, без навязанного трёхдневного lockout восстановления.
  - `AuthCodeService.RecordChallengeAttempt`/`IsClosedByAttempts`; `PasswordResetServiceTests.Reset_FifthWrongAttempt_ClosesChallenge_AndRejectsCorrectCode` и `Reset_AfterChallengeClosed_NewCodeAfterCooldownWorks`; flow `Reset_AfterFiveWrongAttempts_ClosesChallenge_AndNewCodeWorks` (`code: closed`, новый код после таймера).
- [x] Подтверждение почты сохраняет своё согласованное правило блокировки; сброс не создаёт случайную общую блокировку разных операций. Административная разблокировка сохраняет понятный результат.
  - Verify по-прежнему использует `User.VerificationAttempts`/`LockedUntil = now + 3 дня`; reset их не трогает (`PasswordResetServiceTests.Request_IgnoresVerificationLock_AndIssuesCode`), admin unlock сбрасывает только блокировку подтверждения (тесты 45 и `AdminUnlockTests` зелёные).
- [x] Forgot сохраняет нейтральный ответ для неизвестного/неподтверждённого email; UI корректно показывает допустимость повторной выдачи и снимает pending при сбое.
  - Endpoint всегда отдаёт нейтральный `200`; `ForgotPasswordView.vue` маппит маркеры `invalid`/`expired`/`used`/`closed`, таймер повторной выдачи, pending снимается в `finally`.
- [x] Проверены параллельное потребление, счётчик, resend и сценарий пяти запросов без challenge на PostgreSQL; семантика изменённых блокировок документирована.
  - `Postgres/PostgresPasswordResetTests` (4 сценария через `AsyncBarrier`, без sleep); `docs/adr/0009-challenge-scoped-auth-locks.md`, обновлены `CONTEXT.md`, `spec.md`, `README.md`.

## Evidence

- Fast: `docker run --rm -u "$(id -u):$(id -g)" -e DOTNET_CLI_HOME=/tmp/dotnet-home -e NUGET_PACKAGES=/tmp/nuget -v "$PWD":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test` → Failed: 0, Passed: 301, Skipped: 25 (Postgres-тесты пропущены без сервера).
- PostgreSQL: `scripts/test-postgres.sh` → Failed: 0, Passed: 28.
- Frontend: `node:24-alpine npm ci && npm run build` → built successfully.
- Схема: миграция `20261005144039_AddAuthCodeAttempts` (`AuthCodes.Attempts`); `dotnet ef migrations has-pending-model-changes` → «No changes have been made to the model since the last migration».
