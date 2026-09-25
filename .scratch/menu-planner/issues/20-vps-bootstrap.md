# 20: VPS — bootstrap и харденинг сервера

**What to build:** `deploy/bootstrap.sh` — идемпотентный скрипт первичной настройки чистого Ubuntu 24.04 LTS (запуск от root). Создаёт non-root sudo-пользователя, ставит SSH-ключ, меняет SSH-порт на `8822` со «страховкой» (старый порт остаётся открыт, пока не подтверждён вход по новому), выключает вход по паролю и root-логин, настраивает `ufw` (8822/80/443), `fail2ban`, security-only `unattended-upgrades` с авто-перезагрузкой ночью, swap 2 ГБ, TZ `Europe/Moscow` + NTP, Docker Engine + compose-plugin, каталог `/opt/menu`, `rclone`. Провайдер-агностично: любой VPS/VDS с Ubuntu 24.04.

**Blocked by:** —

**Status:** ready-for-agent

- [ ] Создан `deploy/bootstrap.sh` (bash, идемпотентный, `set -euo pipefail`)
- [ ] Non-root sudo-пользователь + SSH-ключ
- [ ] SSH-порт `8822` со «страховкой» (старый порт закрывается отдельным шагом), `PasswordAuthentication no`, `PermitRootLogin no`
- [ ] `ufw` (8822/80/443), `fail2ban`
- [ ] `unattended-upgrades` только security + авто-перезагрузка ночью, TZ Europe/Moscow, NTP
- [ ] swap 2 ГБ
- [ ] Docker Engine + compose plugin, каталог `/opt/menu`, `rclone`
- [ ] Скрипт проверен `bash -n` (и повторным прогоном — идемпотентность)
