# Map: Меню для домохозяек

Карта усилия «меню-планировщик». Указатель, а не хранилище: детали живут в тикетах, здесь — только ориентир и однострочные итоги.

## Destination

Спека `.scratch/menu-planner/spec.md` реализована и работает в проде: приложение развёрнуто на своём VPS, релиз воспроизводим и наблюдаем. Конец пути — когда закрыты операторские тикеты #20, #22, #23 и пройдена приёмка #24.

## Notes

- Домен: `CONTEXT.md` + `docs/adr/`. Спека: `.scratch/menu-planner/spec.md`. Граф задач: `issues/`.
- Стек: .NET 10 Minimal APIs + EF Core/Npgsql (`backend/`), Vue 3 + Vite (`frontend/`), VPS + Docker Compose + Caddy + OpenTelemetry (`deploy/`).
- Трекер — локальный markdown (`docs/agents/issue-tracker.md`). Фронтир = открытые, незаблокированные, невзятые тикеты.
- Конвенция: реализация тикета всегда через git-worktree `ticket/NN-<slug>`, затем merge в `main`. Уборка worktree/ветки сразу после merge.
- Проверки: backend `docker run … dotnet test`; PostgreSQL-гарантии `scripts/test-postgres.sh`; фронт `npm run test` + `npm run build`; deploy `python3 -B -m unittest discover -s deploy/tests`.
- Фронтир не хранится здесь: открытые тикеты ищутся по `Status:`/`Blocked by:` в `issues/`.

## Decisions so far

### Фундамент (01–11)

- [01: Инфраструктура и сборка](issues/01-infrastructure.md): скелет решений, Docker Compose dev, health.
- [02: Аутентификация](issues/02-authentication.md): регистрация/вход по email+паролю, JWT.
- [03: Семья](issues/03-family.md): создание семьи, инвайт-код, членство не более чем в одной.
- [04: CRUD рецептов](issues/04-recipe-crud.md): рецепты семьи, ингредиенты, шаги, права.
- [05: Автодополнение ингредиентов](issues/05-ingredient-autocomplete.md): подсказки названий из своих данных.
- [06: Планирование недели](issues/06-week-planning.md): сетка 7×5, порции на приём.
- [07: Подбор блюд с фильтрами](issues/07-recipe-matching.md): жёсткие фильтры + мягкие предпочтения.
- [08: Повторяемость блюд](issues/08-repetition.md): вычисляемое число за окно N недель.
- [09: Список покупок](issues/09-shopping-list.md): агрегация, конвертация внутри групп, масштабирование.
- [10: Фото рецептов](issues/10-recipe-photos.md): загрузка/замена/удаление файла.
- [11: Редизайн UI и адаптив](issues/11-ui-redesign.md): палитра, токены, мобильная навигация.

### Аккаунты, почта, доступ (12–19)

- [12: Миграция на .NET 10](issues/12-net10-migration.md): обновление рантайма и образов.
- [13: SMTP-инфраструктура и письма](issues/13-smtp-emails.md): шаблоны подтверждения и сброса, dev-лог.
- [14: Модель AuthCode и поля User](issues/14-authcode-model.md): хэш кода, срок, попытки, TokenVersion.
- [15: Подтверждение почты (бэкенд)](issues/15-email-verification.md): выдача/проверка кода, блокировка.
- [16: Read-only для неподтверждённого](issues/16-readonly-unverified.md): 403 на мутации, просмотр разрешён.
- [17: Восстановление пароля (бэкенд)](issues/17-password-recovery.md): нейтральный ответ, код+новый пароль.
- [18: Админ-разблокировка (бэкенд)](issues/18-admin-unlock.md): сброс попыток и блокировки по email.
- [19: Фронтенд аутентификации](issues/19-frontend-auth-ui.md): подтверждение, восстановление, баннер, админ-раздел.

### Деплой и эксплуатация (20–24, 32, 36–44)

- [21: Prod-конфигурация](issues/21-prod-compose-caddy.md): compose, Caddy, сеть, ForwardedHeaders.
- [32: OpenTelemetry — логи и trace-id](issues/32-opentelemetry-logs.md): сбор и ротация логов.
- [36: Безопасный формат конфигурации скриптов](issues/36-safe-script-config.md): literal `KEY=value`, без shell-инъекций.
- [37: Критические гарантии на PostgreSQL](issues/37-postgres-critical-suite.md): отдельный suite миграций, индексов, гонок.
- [38: Воспроизводимый релиз](issues/38-reproducible-release.md): manifest, digests, неизменяемый выбор версии.
- [39: Установка резервного копирования](issues/39-backup-installation.md): rclone, ротация 7+4.
- [40: Согласованный backup-набор](issues/40-complete-backup-restore.md): полное проверочное восстановление.
- [41: Откат с учётом схемы](issues/41-schema-aware-rollback.md): совместимость схемы при возврате версии.
- [42: Readiness и smoke публичного пути](issues/42-readiness-release-smoke.md): проверка через край.
- [43: Повторяемый bootstrap VPS](issues/43-repeatable-vps-bootstrap.md): проверка sshd до применения.
- [44: Безопасный production-запуск и ресурсы](issues/44-production-config-resources.md): обязательные секреты, лимиты.

### Шаринг между семьями (25–31)

- [25: Шаринг рецептом по ссылке](issues/25-recipe-share-link.md): токен, отзыв, перегенерация.
- [26: Просмотр рецепта по ссылке](issues/26-shared-recipe-view.md): анонимный живой просмотр.
- [27: Добавление внешнего рецепта](issues/27-import-external-recipe.md): обёртка-ссылка в своей семье.
- [28: Состояния «отозвана»/«сломана»](issues/28-external-states.md): единое правило предупреждения.
- [29: Промоушен в копию](issues/29-promote-to-copy.md): обёртка становится редактируемой, фото копируется.
- [30: Интеграция внешних рецептов](issues/30-external-integration.md): план, покупки, автодополнение, повторяемость, подбор.
- [31: ADR 0005, словарь и README](issues/31-adr-docs.md): термины и контракты шаринга.

### Надёжность и ревью (33–35)

- [33: Правки code-review (19, 25–31)](issues/33-code-review-fixes.md): первый двухосевой прогон.
- [34: EF Core миграции вместо EnsureCreated](issues/34-ef-migrations.md): схема через миграции.
- [35: Правки второго code-review (25–34)](issues/35-code-review-fixes-2.md): второй прогон.

### Гарантии конкурентности (45–55)

- [45: Атомарное подтверждение почты](issues/45-atomic-email-verification.md): блокировка строки, единый код.
- [46: Атомарный сброс пароля](issues/46-atomic-password-reset.md): счёт на challenge, без lockout аккаунта.
- [47: Ограниченные auth-лимиты](issues/47-bounded-auth-rate-limits.md): окна и число ключей ограничены.
- [48: Управляемый bootstrap администратора](issues/48-explicit-admin-bootstrap.md): один админ, advisory-lock.
- [49: Обязательный защищённый SMTP](issues/49-required-smtp-tls.md): TLS, коды не в логах.
- [50: Надёжная доставка писем](issues/50-durable-email-delivery.md): outbox, переживает отказ SMTP.
- [51: Последовательные права на ссылки](issues/51-consistent-share-permissions.md): чтение и создание ссылки, гонка.
- [52: Race-safe недельный черновик](issues/52-race-safe-week-draft.md): identity недели, поздние ответы.
- [53: Ревизии недельного плана](issues/53-week-plan-revisions.md): конфликты совместного сохранения.
- [54: Identity-safe черновик рецепта](issues/54-identity-safe-recipe-draft.md): поздний ответ не подменяет ресурс.
- [55: Ревизия рецепта и промоушен](issues/55-recipe-revisions-promotion.md): однократность копии, токен конкурентности.

### Чтение, UI и точность (56–73)

- [56: Явный частичный успех фото](issues/56-explicit-photo-partial-success.md): текст сохранён, фото повторяемо.
- [57: Восстановление действий с семьёй](issues/57-family-action-recovery.md): понятные ошибки и повтор.
- [58: Стабильное автодополнение](issues/58-stable-ingredient-autocomplete.md): identity строки, а не индекса.
- [59: Согласованная сессия](issues/59-consistent-session-lifecycle.md): поздний 401 гасит только свой токен.
- [60: Единое чтение рецепта](issues/60-effective-recipe-reading.md): `RecipeReader` для списка и деталей.
- [61: Подбор доступных блюд](issues/61-available-recipe-picker.md): актуальные фильтры и поиск.
- [62: Согласованные план и покупки](issues/62-consistent-plan-shopping-content.md): диагностика полноты.
- [63: Ингредиенты внешних рецептов](issues/63-effective-ingredient-suggestions.md): без лишней загрузки.
- [64: Канонические диеты](issues/64-canonical-diets.md): единые коды и подписи.
- [65: Валидация, совпадающая с хранением](issues/65-storage-aligned-validation.md): точное сохранение.
- [66: Согласованный жизненный цикл фото](issues/66-consistent-photo-lifecycle.md): нет сирот, единая защита.
- [67: Проверка настоящего изображения](issues/67-real-image-validation.md): по содержимому, не только content-type.
- [68: Доступность с клавиатуры](issues/68-keyboard-accessible-planning.md): планирование и формы.
- [69: Настройки без побочных эффектов](issues/69-side-effect-free-settings.md): чтение не меняет состояние.
- [70: Повторяемость для выбранной недели](issues/70-repetition-for-selected-week.md): окно вокруг выбираемой недели.
- [71: Рабочая диагностика и trace-id](issues/71-actionable-log-diagnostics.md): единый trace-id, release-id.
- [72: Алерты по доступности и ресурсам](issues/72-availability-backup-resource-alerts.md): недоступность, старые бэкапы, лимиты.
- [73: Автоматический гейт релиза](issues/73-automated-release-gate.md): проверки перед публикацией.

### Ревью-правки (74–76)

- [74: Stateless-правила и db-сервисы](issues/74-split-stateless-rules-and-db-services.md): чистое = static, БД = scoped DI.
- [75: Правки ревью 53/55](issues/75-review-fixes-53-55.md): `WeekPlanReader`, `RecipeRevisionReader`, фото через ревизионный сервис.
- [76: Правки ревью 36–74](issues/76-review-fixes-36-74.md): `AppDbContext` убран из эндпоинтов, дедупликация, #38 (pinned release), #54 (identity шаринга).

## Not yet specified

- Что именно вскроет первый реальный прогон на VPS (#20/#22/#23) — параметры провайдера, DNS, лимиты — пока туман: формулируется по факту.
- Объём приёмки #24 (`Проверка флоу, документация, ADR`) уточнится, когда операторские тикеты закроются.

## Out of scope

Из спеки, раздел «Out of Scope» (вне этого усилия; вернётся только при переопределении назначения):

- Импорт рецептов из файла/заметок; полный справочник продуктов.
- Шаблоны недели, автоматическая генерация плана, совместное редактирование в реальном времени.
- Округление количеств в списке покупок.
- Фаза «б» наблюдаемости: трейсы и метрики OpenTelemetry, дашборды, внешний алертинг (сейчас — логи + trace-id, #32/#71/#72).
- Смена пароля со знанием старого в настройках; форма запроса пользователя на разблокировку (сейчас только админ, #18).
- Уведомления об отзыве/перегенерации/удалении источника (только пассивные предупреждения).
- Транзитивный шаринг, публичный каталог/поиск между семьями, множественные ссылки на рецепт.
- Интернационализация интерфейса (только задел в структуре).
