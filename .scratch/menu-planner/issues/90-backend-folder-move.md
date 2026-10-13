# 90: Backend — разложить файлы фич по подпапкам (чистые моки)

**What to build:** Файлы `backend/src/MenuPlanner.Api` раскладываются по подпапкам внутри фич согласно карте ниже. Только перемещение файлов — логика, имена классов, namespace и HTTP-контракты не меняются.

**Blocked by:** None (can start immediately)

**Status:** resolved (ticket/90-backend-folder-move)

## Карта

```
Auth/
  Accounts/       AuthEndpoints, UserAccountStore
  Codes/          AuthCodeGenerator, AuthCodeLifecycle, AuthCodePolicy, AuthCodeOptions
  Verification/   EmailVerificationEndpoints, EmailVerificationService
  PasswordReset/  PasswordResetEndpoints, PasswordResetService
  Admin/          AdminEndpoints, AdminBootstrap, AdminBootstrapCommand
  Sessions/       JwtOptions, JwtTokenService, AuthSessionValidator,
                  CurrentUser, CurrentUserContext, RequireVerifiedEmail
  RateLimiting/   AuthRateLimitOptions, AuthRateLimitPolicy, FixedWindowRateLimiter, ClientIpResolver
  (корень)        Contracts.cs, EmailPolicy.cs, PasswordPolicy.cs

Recipes/
  Crud/       RecipeEndpoints, RecipeMutationService, RecipeValidation,
              RecipeErrors, RecipeTarget, Contracts.cs
  Reading/    RecipeReader, RecipeRevisionReader, RecipeRevisionRules
  Sharing/    RecipeSharingService, RecipeShareEndpoints, SharedRecipeEndpoints,
              SharedRecipeImportService, ShareOptions
  Files/      (из #80) + RecipeImageValidator, RecipePdfValidator
  External/   (из #81)
  Repetition/ (как есть)

Plans/ ShoppingList/ Families/ Emails/  → по роли (Endpoints/, Services/, Rules/, Options/, Contracts/),
                                          только если ≥ ~8 файлов; Emails/Outbox/ остаётся
Settings/ Ingredients/ Health/ Data/ Configuration/ Observability/  → не трогаем
Program.cs  → в корне
```

## Checklist

- [x] Файлы перемещены через `git mv` по карте; `namespace` НЕ меняются (это проход #91).
- [x] `Program.cs`, `.csproj`, миграции и `bin/obj` не затронуты содержательно.
- [x] Логика, имена классов, HTTP-контракты не изменены.
- [x] `dotnet test` и `scripts/test-postgres.sh` зелёные.

## Evidence

**Перемещения:** 46 файлов, все — чистые rename (100% similarity): `46 files changed, 0 insertions(+), 0 deletions(-)`. Содержимое, `namespace`, имена классов и HTTP-контракты не менялись; `using` править не потребовалось; SDK-стиль `.csproj` подхватывает новые пути, `csproj` не трогали.

- **Auth/** (20): `Accounts/` — AuthEndpoints, UserAccountStore; `Codes/` — AuthCodeOptions (к трём существующим); `Verification/` — EmailVerificationEndpoints, EmailVerificationService; `PasswordReset/` — PasswordResetEndpoints, PasswordResetService; `Admin/` — AdminEndpoints, AdminBootstrap, AdminBootstrapCommand; `Sessions/` — JwtOptions, JwtTokenService, AuthSessionValidator, CurrentUser, CurrentUserContext, RequireVerifiedEmail; `RateLimiting/` — AuthRateLimitOptions, AuthRateLimitPolicy, FixedWindowRateLimiter, ClientIpResolver. В корне остались `Contracts.cs`, `EmailPolicy.cs`, `PasswordPolicy.cs`.
- **Recipes/** (16): `Crud/` — RecipeEndpoints, RecipeMutationService, RecipeValidation, RecipeErrors, RecipeTarget, Contracts.cs; `Reading/` — RecipeReader, RecipeRevisionReader, RecipeRevisionRules; `Sharing/` — RecipeSharingService, RecipeShareEndpoints, SharedRecipeEndpoints, SharedRecipeImportService, ShareOptions; `Files/` — RecipeImageValidator, RecipePdfValidator. `External/` и `Repetition/` — как есть.
- **Emails/** (10): `Contracts/` — EmailMessage, IEmailTransport, EmailDeliveryFailure, EmailDeliveryException; `Options/` — EmailOptions, EmailConfiguration; `Services/` — EmailSender, EmailTemplates, LoggingEmailTransport, SmtpEmailTransport. `Emails/Outbox/` — как есть.
- **Оставлены плоско** (в фиче < ~8 файлов, дробление по ролям не оправдано): `Plans/` (6: Contracts, PlanEndpoints, PlanValidation, WeekPlanReader, WeekPlanRevisions, WeekPlanSaver), `ShoppingList/` (4: Contracts, ShoppingListBuilder, ShoppingListContentBuilder, ShoppingListEndpoints), `Families/` (4: Contracts, FamilyEndpoints, FamilyService, InviteCodeGenerator).
- **Не трогали:** `Settings/`, `Ingredients/`, `Health/`, `Data/`, `Configuration/`, `Observability/`, `Migrations/`, `Domain/`, `Program.cs` (в корне проекта).

**Тесты:**

- `docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test` → `Failed: 0, Passed: 684, Skipped: 62, Total: 746`.
- `scripts/test-postgres.sh` → `Failed: 0, Passed: 70, Skipped: 0, Total: 70`.
- 62 skip в первом прогоне — PostgreSQL-тесты без `MENU_PLANNER_TEST_POSTGRES` (покрыты вторым прогоном). Для второго прогона root-owned `bin/obj` от первого снесены контейнером (как в `AGENTS.md`).
