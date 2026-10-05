# 49: Обязательный защищённый SMTP и отсутствие кодов в production-логах

**What to build:** Пользователь получает код по защищённому почтовому соединению; production не может незаметно отправить его открытым текстом или заменить доставку записью секрета в журналы.

**Blocked by:** None (can start immediately)

**Status:** resolved (commit `626f788`)

- [x] Настройка TLS означает обязательную защиту: выбран явный требуемый STARTTLS либо TLS-соединение, без downgrade «когда доступно» и без отключения проверки сертификата.
  - `SmtpEmailTransport.ResolveSocketOptions`: `SMTP_SECURITY=starttls` → `SecureSocketOptions.StartTls` (требуемый), `ssl` → `SslOnConnect`; `StartTlsWhenAvailable`/`None` не используются, `ServerCertificateValidationCallback` не задаётся.
- [x] Несовместимые host/port/TLS-настройки и отсутствие обязательных полей дают понятную ошибку конфигурации без секретов.
  - `EmailConfiguration.Read/Validate`: `starttls` на 465 и `ssl` на 587, поры вне 1–65535, `SMTP_FROM`, непарный `SMTP_USER`/`SMTP_PASSWORD`, `SMTP_TIMEOUT_SECONDS` вне 1–300, неизвестный `SMTP_SECURITY`/`EMAIL_TRANSPORT`; `SMTP_ENABLE_STARTTLS=false` отвергается. Тесты `EmailConfigurationTests.ConfigurationErrors_DoNotRevealPassword`.
- [x] Пустой SMTP в production не выбирает logging transport автоматически; dev/test-режим полного письма доступен только явно.
  - `ProductionConfiguration` требует непустые `SMTP_HOST`/`SMTP_FROM` и отклоняет `EMAIL_TRANSPORT=log`; транcпорт выбирается по `EmailOptions.Transport`, log — только явная опция/lab (`EmailWiringTests`).
- [x] Коды и полное HTML-тело не попадают в production-журналы, диагностические исключения также не раскрывают credentials или письмо.
  - Полное письмо пишет только `LoggingEmailTransport`, недоступный в production; `EmailDeliveryException` несёт `Reason`/`FailureType` без `InnerException`, сообщение без адреса, кода и тела. `EmailDeliveryFailure.LogSafe` логирует только причину.
- [x] Транспортная ошибка имеет ограниченное время ожидания и диагностируемый результат; она не считается доставкой. Надёжная очередь добавляется следующим тикетом.
  - `SmtpClient.Timeout = SMTP_TIMEOUT_SECONDS`; сбои отображаются в `EmailFailureReason` (`Timeout`/`TlsUnavailable`/`Certificate`/`Authentication`/`Connection`/`Protocol`/`Unknown`); ответ считается принятым только после `SendAsync`. Очередь (тикет 50) не реализована.
- [x] Проверены сервер без требуемого STARTTLS, ошибки сертификата, успешный защищённый сценарий и выбранный dev/test transport.
  - `SmtpEmailTransportTests` поверх in-process `SmtpTestServer` (loopback, без внешней сети): нет STARTTLS → `TlsUnavailable`; self-signed серт при включённой проверке → `Certificate`; успешный требуемый STARTTLS с тестовым доверием → доставка; «молчащий» сервер → `Timeout`; `EmailWiringTests.WithoutSmtpConfigured_ResolvesLoggingTransport`.
- [x] Пользовательские сообщения при неудачной отправке понятны; инструкция SMTP отражает фактические гарантии.
  - Register/resend → `503` «Не удалось отправить письмо. Попробуйте позже.» (показывается фронтендом из `data.error`); forgot остаётся нейтральным. README, `deploy/README.md`, `deploy/server.conf.example`, `docs/adr/0006` обновлены; ключи прокинуты в `docker-compose*.yml` и `deploy/config.sh`.

## Evidence

- Fast: `docker run --rm -u "$(id -u):$(id -g)" -e DOTNET_CLI_HOME=/tmp/dotnet-home -e NUGET_PACKAGES=/tmp/nuget -v "$PWD":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test` → Failed: 0, Passed: 329, Skipped: 25 (Postgres-тесты пропущены без сервера).
- Deploy: `python3 -B -m unittest discover -s deploy/tests` → Ran 125 tests, OK.
- Frontend не изменялся; сообщение о сбое уже выводится из `data.error`.
- Ограничения: надёжная очередь/повторы — тикет 50; при сбое SMTP после `SaveChanges` аккаунт уже создан (общая ошибка повторной регистрации — предмет тикета 50).
