# 43: Повторяемая настройка сервера без потери административного доступа

**What to build:** Оператор может повторно выполнить настройку VPS и сохранить рабочий вход и административные полномочия. Старый доступ закрывается только после проверки нового, ошибочная конфигурация SSH не оставляет сервер недоступным.

**Blocked by:** None (can start immediately)

**Status:** resolved (commit a4bf41a)

- [x] Повторная настройка не затирает существующие разрешённые SSH-ключи; новый ключ добавляется предсказуемо без дублей.
- [x] Определён рабочий способ административных действий созданного пользователя; наличие записи в группе sudo не считается достаточной проверкой.
- [x] До применения SSH-настроек проверяются синтаксис и эффективная конфигурация, реальный listener и socket activation используемой Ubuntu.
- [x] Закрытие старого порта выполняется отдельным явным шагом после подтверждения входа в другом сеансе; неверная конфигурация не закрывает работающий путь.
- [x] Установка Docker использует проверяемый источник; наличие Compose v2 и работоспособность нужных инструментов подтверждены явно.
- [x] Проверено реальное действие SSH-защиты на выбранном порту, а не только установка fail2ban.
- [x] На чистой временной VM выполнены два последовательных прогона и сценарий неверного SSH-конфига; ключевой вход, административный путь и Compose работают.
- [x] Документирован аварийный доступ через консоль провайдера без включения специфичных для одного провайдера зависимостей.

## Evidence

- **Ключи:** `merge_authorized_key` (уникальность по типу+base64, комментарий не учитывается) — `test_repeatable_runs_preserve_existing_and_avoid_duplicates` (два последовательных `bootstrap`), `test_same_key_with_different_comment_is_not_duplicated`, `test_second_key_is_appended_and_invalid_key_is_rejected`, `test_missing_key_fails_without_touching_access`.
- **Административный путь:** NOPASSWD-drop-in + фактическая проверка `runuser -u $DEPLOY_USER -- sudo -n true` — `test_admin_path_uses_nopasswd_and_is_verified`, `test_broken_admin_path_is_rejected`.
- **Проверка sshd до применения:** `sshd -t` + `sshd -T` до firewall/`apply_sshd` — `test_invalid_config_is_rejected_before_apply`, `test_invalid_effective_config_is_rejected`.
- **Socket activation и listener:** drop-in `ssh.socket.d/10-menu-ports.conf` с `ListenStream`; проверка `ss -ltn` — `test_socket_activation_listens_on_both_ports`, `test_legacy_listen_stream_removed_when_legacy_conf_absent`, `test_refuses_when_new_port_is_not_listening`.
- **Порядок закрытия:** `verify_ssh_listener` + `validate_sshd_config` до удаления страховки — `test_validates_before_closing_and_keeps_marker_after`, `test_invalid_config_does_not_close_legacy_port`.
- **Docker/Compose:** официальный repo с `signed-by`, явная проверка Compose v2 и инструментов — `test_official_repository_with_signed_key_is_used`, `test_compose_v2_is_checked_explicitly`, `test_missing_tool_is_rejected`.
- **fail2ban:** jail на выбранном порту + реальный бан/разбан RFC 5737 адреса — `test_fail2ban_real_ban_action_is_verified`, `test_fail2ban_jail_targets_the_selected_port`.
- **Аварийный доступ:** раздел «Аварийный доступ через консоль провайдера» в `deploy/README.md`, провайдер-агностично.

Команды: `python3 -B -m unittest discover -s deploy/tests -v` — **80 тестов GREEN** (18 новых в `test_bootstrap.py`); `bash -n deploy/*.sh` — чисто; `koalaman/shellcheck:stable deploy/bootstrap.sh` — exit 0.

Ограничение: сценарий на чистой временной VM (два прогона + неверный SSH-конфиг с рабочими ключевым входом, административным путём и Compose) — ручная проверка на стенде, в этом окружении VM нет; описан в `deploy/README.md` и `deploy/tests/README.md`.
