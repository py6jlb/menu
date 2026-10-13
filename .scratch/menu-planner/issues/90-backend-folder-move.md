# 90: Backend — разложить файлы фич по подпапкам (чистые моки)

**What to build:** Файлы `backend/src/MenuPlanner.Api` раскладываются по подпапкам внутри фич согласно карте ниже. Только перемещение файлов — логика, имена классов, namespace и HTTP-контракты не меняются.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

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

- [ ] Файлы перемещены через `git mv` по карте; `namespace` НЕ меняются (это проход #91).
- [ ] `Program.cs`, `.csproj`, миграции и `bin/obj` не затронуты содержательно.
- [ ] Логика, имена классов, HTTP-контракты не изменены.
- [ ] `dotnet test` и `scripts/test-postgres.sh` зелёные.

## Evidence

_(заполняется при реализации)_
