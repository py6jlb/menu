# 48: Управляемое первоначальное назначение администратора

**What to build:** Оператор закрытой процедурой создаёт первоначального администратора до публичной регистрации. Первый случайный посетитель и два параллельных регистрационных запроса не могут занять системную роль.

**Blocked by:** 37 — Проверка критических гарантий на PostgreSQL.

**Status:** resolved (commit 2ac65d4, docs afea080)

- [x] Правило «первый публично зарегистрированный становится Admin» заменено явным закрытым bootstrap; обычная регистрация выдаёт только роль Пользователя. `AuthEndpoints.RegisterAsync` всегда `UserRole.User`; `AdminBootstrap.cs`.
- [x] Bootstrap вызывается оператором вне публичного регистрационного маршрута, использует безопасный ввод и не выводит пароль в логи. CLI `admin-bootstrap` в `Program.cs` + `AdminBootstrapCommand` (пароль скрыто из stdin, никогда не логируется; тест `Command_CreatesAdmin_AndNeverPrintsPassword`).
- [x] Повторный запуск предсказуем и не создаёт второго первоначального администратора; конкурентный запуск защищён средствами PostgreSQL. `pg_advisory_xact_lock` (MENUADMN) + проверка `AnyAsync(Admin)`; тесты `Bootstrap_Repeat_*` и `PostgresAdminBootstrapTests.ConcurrentBootstrap_CreatesExactlyOneAdmin`.
- [x] Существующие администраторы при обновлении сохраняются; bootstrap не пересоздаёт пользователя и не перезаписывает пароль существующего email без явной отдельной операции. `AlreadyInitialized`/`EmailTaken` без изменений; тесты `Bootstrap_WithExistingAdmin_*`, `Bootstrap_WhenEmailBelongsToRegularUser_LeavesUserUntouched`.
- [x] Администратор подтверждает почту по общим правилам; административные изменяющие операции недоступны до подтверждения. Создаётся неподтверждённым, код через `EmailVerificationService.IssueInitialCodeAsync`; `/api/admin/unlock` получил `.RequireVerifiedEmail()`; тесты `BootstrapAdmin_VerifiesEmailThroughCommonFlow`, `Unlock_ByUnverifiedAdmin_ReturnsForbidden`.
- [x] Роль Администратора не трактуется как владение семьёй; интерфейс и инструкция различают системную роль и Владелец семьи. `App.vue` показывает «Администратор»/«Пользователь»; CONTEXT/README/deploy разъясняют, что Владелец семьи — `Family.Owner`.
- [x] Проверены чистая установка, повтор, конкуренция и обновление установки с существующим Admin; публичная регистрация не приобретает роль даже в пустой БД. `AdminBootstrapTests`, `PostgresAdminBootstrapTests`, `AuthFlowTests.Registration_AlwaysAssignsUserRole_EvenInEmptyDatabase`.
- [x] Изменённое правило записано в доменных документах и инструкции первого запуска. `CONTEXT.md`, `README.md`, `deploy/README.md` (commit afea080).
