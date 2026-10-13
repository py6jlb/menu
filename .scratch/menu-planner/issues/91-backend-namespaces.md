# 91: Backend — namespace = папка

**What to build:** После перемещения (#90) namespace'ы приводятся к папкам (`MenuPlanner.Api.<Feature>.<Sub>`), обновляются `using` в src и tests. Логика и имена классов не меняются.

**Blocked by:** 90 — Backend — разложить файлы фич по подпапкам.

**Status:** ready-for-agent

## Checklist

- [ ] `namespace` в перемещённых файлах соответствует папке; корневые файлы фич (`Contracts.cs`, `EmailPolicy`, `PasswordPolicy`) остаются в `MenuPlanner.Api.<Feature>`.
- [ ] `using` в src и tests обновлены; сборка без ошибок.
- [ ] Логика и HTTP-контракты не изменены.
- [ ] `dotnet test` и `scripts/test-postgres.sh` зелёные.

## Evidence

_(заполняется при реализации)_
