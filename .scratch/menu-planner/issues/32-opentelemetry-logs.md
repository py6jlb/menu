# 32: OpenTelemetry — логи и trace-id

**What to build:** Наблюдаемость бэкенда через OpenTelemetry. OTel SDK в backend: логи (`ILogger`) и корреляция по `Activity`/trace-id, OTLP-экспорт на коллектор. `deploy/otel-collector.yaml` — приём OTLP, вывод в консоль и в файл с ротацией 3 дня. Сервис `otel-collector` в `docker-compose.prod.yml` с volume под файлы логов и mem limit. Трейсы и метрики (фаза «б») — позже, конфигурацией, без переписывания логирования.

**Blocked by:** 21 (Prod-конфигурация — compose, Caddy, сеть, ForwardedHeaders)

**Status:** resolved (commit `18940f5`)

- [x] Backend: пакеты OpenTelemetry, OTLP-экспорт логов и trace-id
- [x] `deploy/otel-collector.yaml`: OTLP-приём, console + file exporter (`max_days: 3`)
- [x] Сервис `otel-collector` в `docker-compose.prod.yml` с volume и mem limit
- [x] Логи в консоль и в файл, файлы хранятся максимум 3 дня
- [x] `X-Forwarded-For`-IP по-прежнему корректно попадает в логи/rate limit
- [x] Backend-тесты проходят в контейнере
