# 47: Ограничение злоупотреблений без неограниченного роста памяти

**What to build:** Вход, регистрация и восстановление сохраняют доступность при потоке злоупотребляющих запросов; пользователь получает понятный срок повтора, а лимитер не накапливает произвольные email бесконечно и не доверяет поддельному IP.

**Blocked by:** None (can start immediately)

**Status:** resolved (commit 899c216; code-review: строгий self-review по двум осям, critical findings нет)

- [x] Ограничения покрывают login, register, reset и существующие forgot/resend/unlock; значения и правила конфигурации документированы и валидируются. `AuthRateLimitOptions` + `AuthCodeOptions.Validate` читают секции/env и падают понятной ошибкой без секретов; карта — в `README.md`.
- [x] Порядок проверки не создаёт новый email-ключ для каждого запроса с уже ограниченного IP; число ключей и срок их хранения ограничены. `AuthRateLimitPolicy.Check` сначала потребляет IP и возвращает `429`, не трогая ключ операции; тест `Check_WhenIpLimited_DoesNotCreateNewIdentityKey`. Ключи ≤ `AUTH_RATE_LIMIT_MAX_TRACKED_KEYS`, окна истекают.
- [x] Устаревшие окна удаляются, многопоточное потребление безопасно; поведение переполненного хранилища определено и не допускает неограниченного использования памяти. `FixedWindowRateLimiter` под `lock`, прореживание по счётчику/таймеру; при переполнении новые ключи отклоняются, отслеживаемые остаются. Тесты `TryConsume_IsThreadSafe_UnderContention`, `TryConsume_RejectsNewKeysWhenFull_ButKeepsTrackedOnes`, `TryConsume_AtCapacity_FreesExpiredWindowsForNewKeys`, `UniqueEmailStream_KeepsMemoryBounded`.
- [x] Клиентский IP определяется после доверенного proxy middleware; сырые X-Forwarded-For не считаются доверенными, список доверенных прокси соответствует dev/prod-входу. `ClientIpResolver` читает только `RemoteIpAddress`; `ForwardedHeaderConfiguration` подключает middleware лишь при непустом `TRUSTED_PROXY_ADDRESSES`/`TRUSTED_PROXY_NETWORKS` (пустой список ASP.NET трактует как «доверять всем»). prod — Caddy `172.29.0.10`, dev — nginx `172.28.0.10`.
- [x] Учитывается общий NAT семьи: IP-ограничение сочетается с ограничением операции/пользователя, правила не объявлены распределёнными для нескольких реплик. Каждая операция проверяет IP и identity; ADR-0010 и `deploy/README.md` фиксируют одну реплику.
- [x] Отказ возвращает согласованный 429 и Retry-After; UI ограничиваемых операций объясняет ожидание и не остаётся в pending. `RateLimitResults` пишет `Retry-After`; фронтенд-хелперы `retryAfterSeconds`/`rateLimitMessage` во всех ограничиваемых экранах, `pending` сбрасывается в `finally`.
- [x] Проверены граница/истечение окна, конкуренция, поток уникальных email и поддельные proxy-заголовки, в том числе через настроенный край. `RateLimiterTests`, `AuthRateLimitPolicyTests`, `ForwardedHeaderConfigurationTests` (middleware: недоверенный peer игнорирует XFF, доверенный край учитывает), `AuthRateLimitFlowTests` (`Forgot_ForgedForwardedFor_DoesNotBypassIpLimit`).
- [x] Для одной реплики не требуется внешний Redis; переход к общему хранилищу при масштабировании описан как отдельное ограничение. ADR-0010, `README.md`, `deploy/README.md`: локальный in-memory store; масштабирование требует общего store (например, Redis) — отдельная задача.

### Evidence

- Backend fast: `dotnet test` в SDK-контейнере — `327 passed, 25 skipped`.
- PostgreSQL-suite: `scripts/test-postgres.sh` — `28 passed`.
- Deploy-suite: `python3 -B -m unittest discover -s deploy/tests` — `128 passed` (включая `ProxyTrustTests`, 18 в `test_production_hardening.py`).
- Frontend: `npm ci && npm run build` в `node:24-alpine` — сборка успешна.
- Compose: `docker compose config -q` для dev и prod — валидно.
