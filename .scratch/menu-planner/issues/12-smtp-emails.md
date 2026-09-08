# 12: SMTP-инфраструктура и письма

**What to build:** Backend умеет «отправлять» письма. Конфигурация SMTP через env, сервис отправки, в dev без SMTP письма пишутся в лог backend. Два простых HTML-шаблона писем на русском: подтверждение почты и сброс пароля. Без вложений.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] SMTP-конфиг через env: `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM`, `SMTP_FROM_NAME`, `SMTP_ENABLE_STARTTLS`
- [ ] Сервис отправки email; в dev без SMTP — письма в лог backend (используется в тестах)
- [ ] Два HTML-шаблона на русском: подтверждение почты, сброс пароля
- [ ] Юнит/интеграционный тест отправки (в лог)