
# AGENTS.md

Репозиторий пустой: кода, README и системы сборки пока нет. Сейчас здесь только инфраструктура навыков OpenCode.

## Структура

- `skills-lock.json` — источник истины по установленным навыкам (кто, откуда, hash).
- `.agents/skills/` — материализованные копии навыков. Это управляемые файлы: не редактировать вручную, изменения делаются через механизм установки навыков (см. `skills-lock.json`).

## Навыки

- `grill-me` — доступен через Skill tool. Установлен из `mattpocock/skills` (`skills/productivity/grill-me`). Управляется через `skills-lock.json`.
- Обновление/добавление навыков меняет `skills-lock.json`, а не файлы в `.agents/`.

## Agent skills

### Issue tracker

Задачи живут как markdown-файлы в `.scratch/<feature>/`. См. `docs/agents/issue-tracker.md`.

### Triage labels

Стандартные метки: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. См. `docs/agents/triage-labels.md`.

### Domain docs

Single-context: один `CONTEXT.md` + `docs/adr/` в корне. См. `docs/agents/domain.md`.

## Прочее

- Git-репозитория пока нет (`git init` ещё не выполнялся).
- Язык общения с пользователем — русский.