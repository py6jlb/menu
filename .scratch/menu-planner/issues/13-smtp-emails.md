# 13: SMTP-инфраструктура и письма

**What to build:** Backend умеет «отправлять» письма. Конфигурация SMTP через env, сервис отправки, в dev без SMTP письма пишутся в лог backend. Два простых HTML-шаблона писем на русском: подтверждение почты и сброс пароля. Без вложений.

**Blocked by:** 12 (Миграция на .NET 10)

**Status:** resolved (commit `6fab531`)

- [x] SMTP-конфиг через env: `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM`, `SMTP_FROM_NAME`, `SMTP_ENABLE_STARTTLS`
- [x] Сервис отправки email; в dev без SMTP — письма в лог backend (используется в тестах)
- [x] Два HTML-шаблона на русском: подтверждение почты, сброс пароля
- [x] Юнит/интеграционный тест отправки (в лог)