# Деплой на VPS

Продовый контур: Ubuntu 24.04 LTS, Docker Compose, Caddy как единый край, своя Postgres, бэкапы в объектное хранилище. Провайдер любой — инфраструктура агностична.

## Состав

| Файл | Назначение |
|---|---|
| `bootstrap.sh` | первичная настройка и харденинг сервера |
| `build-push.sh` | проверки, сборка образов и manifest; публикация только с `--publish` |
| `deploy.sh` | доставка конфигураций выпускаемого коммита и рестарт на прибитом теге, откат |
| `smoke.sh` | проверка запущенного релиза: идентичность manifest, живой путь, 404 asset |
| `Caddyfile` | маршрутизация края (`/api`, `/health`, SPA) |
| `otel-collector.yaml` | приём OTLP-логов, вывод в консоль и файл (3 дня) |
| `backup.sh` | бэкап БД и фото в объектное хранилище |
| `restore-drill.sh` | проверочное восстановление |
| `install-backup.sh` | установка и обновление systemd-units бэкапа |
| `config.sh` | доверенная библиотека чтения literal-конфигурации |
| `release.sh` | доверенная библиотека релиза: clean-tree, хэши конфигурации, manifest |
| `compose.sh` | серверный Compose без автоматического чтения `.env` |
| `remote-deploy.sh` | серверный entrypoint деплоя, вызывает Compose и записывает manifest |
| `local.conf.example` | шаблон локальных настроек публикации и доставки |
| `server.conf.example` | шаблон серверных настроек и секретов |

## Порядок

1. **Сервер.** На чистом VPS:

   ```bash
   sudo SSH_PUBLIC_KEY='ssh-ed25519 AAAA... user@host' ./deploy/bootstrap.sh
   ```

   Проверь вход на новом порту в отдельном терминале, затем закрой старый:

   ```bash
   sudo ./deploy/bootstrap.sh --close-legacy-port
   ```

2. **Конфигурация.** Локально скопируй `deploy/local.conf.example` в `deploy/local.conf`, заполни Docker Hub и SSH-адрес, выполни `chmod 600 deploy/local.conf`. На сервере отдельно создай `/opt/menu/server.conf` по `deploy/server.conf.example`, заполни серверные секреты и выполни `chmod 600 /opt/menu/server.conf`. Файлы необязательны: все настройки можно передать окружением процесса. `.conf` не читается через `source` и не передаётся как `--env-file` Compose.

3. **Сборка** (локально, из корня репозитория):

   ```bash
   ./deploy/build-push.sh            # проверки + сборка образов + manifest, без публикации
   ./deploy/build-push.sh --publish  # то же и push в Docker Hub
   ```

   Гейт: чистый checkout (без незакоммиченных изменений), `npm ci` в Node LTS по lockfile, backend-тесты, drift-guard миграций. Образы собираются с тегом `:<git-sha>`. Публикация образов выполняется только с явным `--publish`; `latest` — вспомогательный указатель, деплой на него не опирается. Manifest `deploy/release/<tag>.json` (и `current.json`) связывает commit, дайджесты backend/frontend, версии конфигурации и время сборки.

4. **Деплой:**

   ```bash
   ./deploy/deploy.sh <git-sha>
   ```

   Тег — реальный commit. Деплой требует чистый checkout ровно на этом коммите: конфигурации берутся из выпускаемого среза, а не из произвольного рабочего дерева, поэтому повторный деплой старого релиза не подмешивает новые локальные правки. Если для тега есть собранный manifest, скрипт дополнительно сверяет с ним хэши конфигураций (иначе предупреждает, что сверка не выполняется). Далее кладёт `docker-compose.prod.yml`, `Caddyfile`, `otel-collector.yaml`, общую библиотеку, серверные скрипты и systemd-шаблоны в `/opt/menu`, затем вызывает серверный `remote-deploy.sh`: `compose pull && up -d` на указанном теге, проверка `/health` и запись `/opt/menu/release.json` и `/opt/menu/current-release`. `local.conf`, `server.conf` и файлы примеров не доставляются. `server.conf` создаётся и изменяется оператором только на сервере; одинаковый `DOCKERHUB_USER` задаётся в двух окружениях явно.

5. **Проверка и откат:**

   ```bash
   ./deploy/smoke.sh              # на сервере: manifest, живой путь, 404 asset
   ./deploy/deploy.sh <предыдущий-sha>   # откат
   ```

   Повторный штатный запуск Compose (`./deploy/compose.sh up -d`) использует тег из `/opt/menu/current-release` — без временного экспорта в SSH-сессии. Откат — деплой предыдущего sha тем же путём.

## Воспроизводимый релиз

- **Manifest.** `deploy/release/<tag>.json`: `commit`, `tag`, `builtAt`, `backendDigest`, `frontendDigest`, `config` (хэши файлов среза). Хранится локально после сборки и на сервере (`/opt/menu/release.json`); используется для диагностики, отката и smoke-проверки идентичности.
- **Чистый checkout.** Сборка и деплой отклоняют незакоммиченные изменения понятной ошибкой: под идентификатором чистого commit нельзя опубликовать/выкатить чужой код.
- **Неизменяемый выбор.** Тег — sha коммита; `latest` не заменяет закреплённую версию. Предыдущие теги остаются в registry для отката.
- **Состояние сервера.** `remote-deploy.sh` пишет `/opt/menu/current-release` (`IMAGE_TAG`) и `release.json`; `config_server` читает `current-release` ниже окружения и `server.conf`.
- **Срез конфигураций.** `deploy.sh` доставляет конфигурации выпускаемого коммита (чистый checkout на нём) и, когда есть manifest, сверяет их хэши, поэтому старый релиз не смешивается с новыми локальными файлами.

### Обновление базовых образов

Базовые образы закреплены контролируемыми тегами в `backend/Dockerfile`, `frontend/Dockerfile`, `docker-compose*.yml`, `deploy/backup.sh` и перечислены в `release_base_images` (`deploy/release.sh`). Порядок обновления: изменить тег в одном коммите, прогнать `./deploy/build-push.sh` и `scripts/test-postgres.sh`, задеплоить на стенд и проверить `./deploy/smoke.sh`. Node держим на поддерживаемой LTS (`frontend/.nvmrc`, `engines`), установка — только по `frontend/package-lock.json` (`npm ci`); чистая установка не должна менять lockfile.

## Формат и приоритет

Первоначальный `bootstrap.sh` получает настройки сервера и публичный SSH-ключ напрямую из окружения (см. комментарии скрипта и команду выше). Для публикации, доставки, серверного Compose, backup и restore используется описанный ниже формат файлов.

Формат — UTF-8, строки с LF, `KEY=value`. Пустые строки и строки, начинающиеся с `#`, пропускаются. Всё после **первого** `=` сохраняется буквально, включая начальные/конечные пробелы, `=`, `$`, `#`, кавычки и обратные слеши. Кавычки — часть данных, а не обёртка. Inline-комментариев, `export`, escape-последовательностей, переносов внутри значения и shell-подстановок нет.

Пример данных (команды **не** исполняются):

```text
SMTP_FROM_NAME=Меню для домохозяек
SMTP_PASSWORD=пробел $HOME ${SMTP_USER} $$ # "двойные" 'одинарные' \ $(touch /tmp/marker) `id`
```

Приоритет: **заданная переменная окружения → файл → default скрипта/Compose**. Явно пустая переменная окружения имеет приоритет над непустым файлом; default применяется только к незаданным значениям. Для обязательного параметра пустое значение — ошибка. Тег, переданный `deploy.sh <tag>`, становится серверным `IMAGE_TAG` и имеет приоритет над серверным файлом. Окружение локального процесса автоматически через SSH не переносится: env-only на сервере должно быть задано в окружении именно серверного процесса. Для systemd это окружение службы; наш literal-файл не является systemd `EnvironmentFile`.

Неизвестные ключи, ключи другой области, дубликаты, строки без `=`, NUL, CR (включая CRLF), табуляции и другие управляющие символы отвергаются даже при env override. Диагностика содержит причину/номер строки или имя обязательного параметра, без содержимого строки и значений. Файл не может задавать `PATH`, `BASH_ENV`, `SHELLOPTS`, `COMPOSE_ENV_FILES` и прочие управляющие переменные. `source` используется только для доверенной versioned-библиотеки `config.sh`; не запускай конфигурационные скрипты с `bash -x`.

### Разрешённые ключи и обязательные значения

| Область | Разрешённые ключи |
|---|---|
| Локально (`deploy/local.conf`) | `DOCKERHUB_USER`, `IMAGE_TAG`, `VPS_HOST`, `VPS_USER`, `VPS_SSH_PORT`, `APP_DIR` |
| Сервер (`/opt/menu/server.conf`) | `DOCKERHUB_USER`, `IMAGE_TAG`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `JWT_SECRET`, `JWT_ISSUER`, `JWT_AUDIENCE`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM`, `SMTP_FROM_NAME`, `SMTP_ENABLE_STARTTLS`, `DOMAIN`, `SHARE_BASE_URL`, `BACKUP_REMOTE`, `BACKUP_KEEP_DAILY`, `BACKUP_KEEP_WEEKLY`, `COMPOSE_FILE`, `DRILL_CONTAINER` |

| Скрипт | Обязательно до внешних действий |
|---|---|
| `build-push.sh` | непустой `DOCKERHUB_USER`; `IMAGE_TAG`, если задан, допустимый тег |
| `deploy.sh` | тег-аргумент, непустой `VPS_HOST`, допустимые SSH-настройки |
| `remote-deploy.sh`, `compose.sh` | непустые `DOCKERHUB_USER`, `POSTGRES_PASSWORD`, `JWT_SECRET`; у remote ещё тег-аргумент |
| `backup.sh` | те же Compose-параметры и непустой `BACKUP_REMOTE`; корректные сроки хранения |
| `restore-drill.sh` | непустой `BACKUP_REMOTE` |
| `install-backup.sh` | непустой `BACKUP_REMOTE`; существующий пользователь службы (`DEPLOY_USER`, env) и rclone-remote, настроенный для него |

Defaults показаны в примерах и Compose. `BACKUP_KEEP_DAILY`/`BACKUP_KEEP_WEEKLY` — целые 1–9999 без ведущих нулей: произвольный текст не допускается в bash-арифметику. Операционные SSH-настройки ограничены: `VPS_HOST` — DNS/IPv4 (буквы, цифры, точки, дефисы), `VPS_USER` — обычный Unix-login, `VPS_SSH_PORT` — цифры, `APP_DIR` — абсолютный путь из букв/цифр, `/`, `.`, `_`, `-`. IPv6 и пути с пробелами для локальной доставки не поддерживаются. Это ограничения адресов/путей; текстовые SMTP/JWT-значения читаются буквально.

### Compose и переход со старых `.env`

Единый Docker-tag validator используется в build, local/remote deploy и серверных Compose/backup entrypoints до внешних действий. Длина тега — 1–128 символов; первый `[A-Za-z0-9_]`, остальные `[A-Za-z0-9._-]`. Теги с начальным `-` или `.` отвергаются локально до SSH/SCP.

Все серверные операции используют `menu_compose` из библиотеки: `docker compose --env-file /dev/null -f ...` с отключёнными `COMPOSE_ENV_FILES` и автозагрузкой `.env`. Загруженные параметры экспортируются; Compose подставляет их из окружения один раз. В prod YAML defaults используют `${VAR-default}`, сохраняя явно пустой env. Postgres healthcheck передаёт имя БД и пользователя аргументами `CMD`, без shell-исполнения данных.

Для ручных операций на сервере:

```bash
./deploy/compose.sh ps
./deploy/compose.sh config --quiet  # валидация без вывода секретов
```

`compose config` без `--quiet` выводит секреты; это не диагностическая команда для production-логов. При сериализации модели Compose экранирует `$` как `$$` (в том числе в JSON), чтобы результат можно было снова прочитать; это не изменение окружения контейнера. Проверки используют только вымышленные значения, проверяя и модель, и `config --environment`.

Старые `deploy/.env` и серверный `.env` больше не читаются скриптами. Перенеси нужные локальные ключи в `deploy/local.conf`, серверные — в `/opt/menu/server.conf`; перепроверь кавычки и `\` вручную по literal-правилам (старое shell/Compose-экранирование больше не нужно). Удали старые файлы после переноса. Автоматического fallback на shell source нет. Существующий `.env` даже при наличии игнорируется серверным wrapper. Dev-Compose (`docker-compose.yml`, обычный `docker compose up --build`) использует собственные dev-defaults и стандартные правила Compose; `.conf` предназначены для deploy-скриптов и prod-wrapper.

`DOMAIN=:80` до покупки домена (HTTP), затем `DOMAIN=example.com` — Caddy выпустит Let's Encrypt автоматически.

### Literal-параметры подключения backend

Prod-Compose передаёт backend отдельные переменные: `DB_HOST=db`, `DB_PORT=5432`, `DB_NAME` из `POSTGRES_DB`, `DB_USER` из `POSTGRES_USER`, `DB_PASSWORD` из `POSTGRES_PASSWORD`. Имена и пароль не конкатенируются в YAML-строку подключения. `DatabaseConnection.Resolve` назначает свойства `NpgsqlConnectionStringBuilder`, который сериализует их с корректным экранированием: literal `POSTGRES_PASSWORD="abc"` остаётся паролем с двумя кавычками, а `POSTGRES_PASSWORD=  abc  ` — с крайними пробелами. Аналогично сохраняются `;`, `\`, `$` и `#`. Эти данные не логируются.

Приоритет backend-конфигурации:

1. При заданном `DB_HOST` (включая пустой) используется structured-набор `DB_*`; он выше `ConnectionStrings:Default` и `DB_CONNECTION_STRING`. Поэтому встроенный JSON-default не перекрывает prod-конфигурацию.
2. Без `DB_HOST` сохраняется прежний приоритет: `ConnectionStrings:Default` (в том числе env `ConnectionStrings__Default`) → `DB_CONNECTION_STRING` → fallback `Host=localhost;Port=5432;Database=menu_planner;Username=menu;Password=menu`. JSON `appsettings.json` задаёт `ConnectionStrings:Default` с `Host=db`, поэтому в обычном runtime он также выше `DB_CONNECTION_STRING`; EF factory читает окружение без JSON.

У structured-набора defaults только для незаданных полей: порт 5432, база `menu_planner`, пользователь/пароль `menu`. `DB_PORT` при наличии должен быть десятичным целым 1–65535: пустое, некорректное или out-of-range значение приводит к понятной ошибке до подключения; значение и inner exception не выводятся. Отсутствующий `DB_PORT` даёт 5432. В prod пароль обязателен на уровне скриптов и Compose. Остальные явно пустые поля не заменяются default; Npgsql при повторном чтении строки нормализует пустые поля в `null`. Для legacy override в отдельном runtime/EF-запуске не задавай `DB_HOST`; в prod для переключения на legacy-строку нужен собственный Compose override без `DB_HOST`. Dev-Compose продолжает передавать прежнюю готовую строку подключения. Runtime и EF factory используют один resolver; tests проверяют reparsing Npgsql без подключения к БД.

### Переход и rollback на образ до тикета 36

Prod-Compose одновременно передаёт `DB_*` **и** `ConnectionStrings__Default`. Новый backend выбирает `DB_*`. Backend из `cefd943`, не знающий structured-набора, получает прежний ключ строки подключения вместо ухода на встроенные defaults.

`menu_compose` вычисляет legacy-строку из тех же effective `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`. Host остаётся `db`, port — 5432. Используется стандартная quoted-string grammar ADO.NET/Npgsql: каждое строковое поле заключено в двойные кавычки, literal `"` удвоена, остальные символы (включая крайние пробелы, `;`, `\`, `$`, `#`) сохранены. Например, literal пароль `"abc"` сериализуется как `Password="""abc"""` и Npgsql читает обратно пароль с кавычками.

Это derived env только на время вызова Compose: входящий `ConnectionStrings__Default` в prod-wrapper заменяется вычисленным значением, чтобы rollback не расходился с literal `POSTGRES_*`. Этот ключ не принимается из `.conf`; отдельного секрета/файла/настройки для него нет. Строка не логируется и не генерируется через SDK/eval: на VPS нужна только доставленная Bash-библиотека. Ручные prod-операции также запускай через `deploy/compose.sh` — YAML требует вычисленную строку. Изолированные проверки подтверждают передачи Compose и Npgsql parsing для legacy resolver без `DB_HOST`; старый образ и реальная БД не запускаются.

## Порты

Наружу открыты только `80`/`443` (Caddy) и SSH. `db`, `backend`, `frontend` доступны только внутри compose-сети.

## Бэкапы

`backup.sh` делает `pg_dump` БД и `tar` фото, выгружает их через `rclone` в объектное хранилище и чистит старое (по умолчанию 7 дневных и 4 недельных копии). Прод-базу не блокирует. Каждый шаг (дамп, архивация, выгрузка, подтверждение объектов, чистка) при ошибке завершает запуск ненулевым кодом; `Готово` печатается только после успешной доставки обоих объектов.

### Установка на сервере

Штатная процедура деплоя доставляет `backup.sh`, `restore-drill.sh`, `install-backup.sh` и шаблоны units в `/opt/menu/deploy/` (см. шаг «Деплой»). Checkout репозитория на сервере не нужен: все команды ниже используют доставленные файлы по абсолютному пути.

Служба `menu-backup.service` работает от пользователя `menu` (создаётся `bootstrap.sh`, состоит в группе `docker`). `server.conf` должен читаться этим пользователем — установщик выставляет `menu:menu` и `600`. rclone-настройки служба ищет в `$HOME/.config/rclone/rclone.conf` пользователя `menu`, поэтому remote создаётся **от его имени**, а не от root; remote root-пользователя службе не виден.

```bash
# 1. rclone-remote от имени пользователя службы
sudo -u menu -H rclone config          # создай S3-совместимый remote, например "selectel"

# 2. BACKUP_REMOTE и POSTGRES_* в /opt/menu/server.conf
#    BACKUP_REMOTE=selectel:menu-backups

# 3. Доставленные скрипты ставят и обновляют units, включают timer
sudo /opt/menu/deploy/install-backup.sh
```

`install-backup.sh` идемпотентен: рендерит units под фактического `DEPLOY_USER` и каталог приложения, ставит их в `/etc/systemd/system`, проверяет доступ к remote от имени службы, делает `daemon-reload` и `enable --now menu-backup.timer`. Если remote не настроен, установка останавливается с подсказкой.

### Проверка после установки

```bash
sudo systemctl start menu-backup.service        # ручной запуск
sudo journalctl -u menu-backup.service -n 50 --no-pager
sudo -u menu -H rclone lsf selectel:menu-backups/db/ --files-only       # объекты появились
sudo -u menu -H rclone lsf selectel:menu-backups/photos/ --files-only
systemctl list-timers menu-backup.timer --no-pager                       # активен, видно Next
/opt/menu/deploy/restore-drill.sh               # проверочное восстановление
```

`restore-drill.sh` без аргумента берёт последний дамп и последний архив фото из `BACKUP_REMOTE`, поднимает временную Postgres в изолированном контейнере (уникальное имя, `--network none`, контейнер и его volume удаляются на выходе) и восстанавливает дамп с `ON_ERROR_STOP`: ошибка SQL прерывает drill. Имя БД и роль берутся из тех же `POSTGRES_DB`/`POSTGRES_USER`, что у `backup.sh`, поэтому настройки совпадают с дампом. Отсутствие архива фото — неуспех, а не «успешный» drill. Явный аргумент — путь к скачанному дампу; архив фото всё равно сверяется. Согласование БД, фото и закреплённого релиза в одну точку восстановления — следующий тикет.

## Изолированные regression tests

Команды, требования и карта критериев — в [`tests/README.md`](tests/README.md). Быстрый запуск из корня:

```bash
python3 -B -m unittest discover -s deploy/tests -v
```
