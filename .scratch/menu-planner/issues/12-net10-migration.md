# 12: Миграция на .NET 10

**What to build:** Поднять backend с .NET 8 на .NET 10. Обновить `TargetFramework` на `net10.0` в обоих проектах (API и тесты), поднять версии пакетов до актуальных для .NET 10 (EF Core 10, Npgsql 10, JwtBearer 10, Mvc.Testing 10, InMemory 10 и связанные), обновить Dockerfile на `sdk:10.0`/`aspnet:10.0`. Проверить совместимость кода (Minimal APIs, EF Core) — исправить возможные breaking changes. Все последующие тикеты (13-23) реализуются уже на .NET 10.

**Blocked by:** None (can start immediately)

**Status:** resolved (commit `eb7ec7f`)

- [x] `TargetFramework` net10.0 в `MenuPlanner.Api.csproj` и `MenuPlanner.Api.Tests.csproj`
- [x] Пакеты подняты до актуальных версий для .NET 10 (EF Core 10.0.11, Npgsql 10.0.3, JwtBearer 10.0.11, Mvc.Testing 10.0.11, InMemory 10.0.11, Test SDK 17.14.1, xunit 2.9.3)
- [x] `backend/Dockerfile`: `sdk:10.0` и `aspnet:10.0`
- [x] Код совместим с .NET 10 (breaking change EF Core 10: два провайдера БД — исправлено в ApiFactory)
- [x] Все тесты зелёные в контейнере на `sdk:10.0` (109/109)