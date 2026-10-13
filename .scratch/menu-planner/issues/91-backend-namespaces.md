# 91: Backend — namespace = папка

**What to build:** После перемещения (#90) namespace'ы приводятся к папкам (`MenuPlanner.Api.<Feature>.<Sub>`), обновляются `using` в src и tests. Логика и имена классов не меняются.

**Blocked by:** 90 — Backend — разложить файлы фич по подпапкам.

**Status:** resolved (ticket/91-backend-namespaces)

## Checklist

- [x] `namespace` в перемещённых файлах соответствует папке; корневые файлы фич (`Contracts.cs`, `EmailPolicy`, `PasswordPolicy`) остаются в `MenuPlanner.Api.<Feature>`.
- [x] `using` в src и tests обновлены; сборка без ошибок.
- [x] Логика и HTTP-контракты не изменены.
- [x] `dotnet test` и `scripts/test-postgres.sh` зелёные.

## Evidence

Правило: файл в подпапке `X/Y/` → `MenuPlanner.Api.X.Y`, файл прямо в папке фичи `X/` → `MenuPlanner.Api.X`.

### Новые/уточнённые namespace (46 файлов сменили namespace)

| Папка | Namespace | Файлов |
| --- | --- | --- |
| `Auth/Accounts` | `MenuPlanner.Api.Auth.Accounts` | 2 |
| `Auth/Admin` | `MenuPlanner.Api.Auth.Admin` | 3 |
| `Auth/Codes` | `MenuPlanner.Api.Auth.Codes` | 1 (`AuthCodeOptions.cs` переехал из `MenuPlanner.Api.Auth`) |
| `Auth/PasswordReset` | `MenuPlanner.Api.Auth.PasswordReset` | 2 |
| `Auth/RateLimiting` | `MenuPlanner.Api.Auth.RateLimiting` | 4 |
| `Auth/Sessions` | `MenuPlanner.Api.Auth.Sessions` | 6 |
| `Auth/Verification` | `MenuPlanner.Api.Auth.Verification` | 2 |
| `Recipes/Crud` | `MenuPlanner.Api.Recipes.Crud` | 6 |
| `Recipes/Reading` | `MenuPlanner.Api.Recipes.Reading` | 3 |
| `Recipes/Sharing` | `MenuPlanner.Api.Recipes.Sharing` | 5 |
| `Recipes/Files` | `MenuPlanner.Api.Recipes.Files` | 2 (`RecipeImageValidator`, `RecipePdfValidator` были `...Recipes`) |
| `Emails/Contracts` | `MenuPlanner.Api.Emails.Contracts` | 4 |
| `Emails/Options` | `MenuPlanner.Api.Emails.Options` | 2 |
| `Emails/Services` | `MenuPlanner.Api.Emails.Services` | 4 |

Корневые файлы фич остались как были: `Auth/Contracts.cs`, `Auth/EmailPolicy.cs`, `Auth/PasswordPolicy.cs` → `MenuPlanner.Api.Auth`. `Recipes/External`, `Recipes/Repetition`, `Emails/Outbox` уже совпадали с папками. Тестовый проект не реорганизован (#92): namespace тестов — `MenuPlanner.Api.Tests`.

Проверка соответствия namespace папкам скриптом — расхождений нет.

### `using`

Добавлено **117** директив `using MenuPlanner.Api.<Feature>.<Sub>;` (src + tests), включая `Program.cs` (12 шт.). Вставка — в существующую группу `MenuPlanner.Api.*` по алфавиту (родитель перед детьми); прочие `using` не переставлялись, дублей нет. Имена классов, сигнатуры, HTTP-контракты не менялись.

### Проверка

```bash
docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet build
# → Build succeeded. 0 errors; 40 warnings — все предсуществующие NU1902/NU1903 (SixLabors.ImageSharp)

docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
# → Passed!  - Failed: 0, Passed: 684, Skipped: 62, Total: 746

bash scripts/test-postgres.sh
# → Passed!  - Failed: 0, Passed: 70, Skipped: 0, Total: 70
```

`git diff --check` — чисто. Единственные удалённые строки — 46 старых объявлений `namespace`.
