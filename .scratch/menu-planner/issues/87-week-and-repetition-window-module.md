# 87: Единый module недели и окна повторяемости

**What to build:** Разбор/формат/начало недели и расчёт окна сводятся в чистый `WeekStart`; разбор недели перестаёт дублироваться в трёх местах; окно повторяемости читается через `SettingsService`, а константы живут в одном `SettingsCatalog`.

**Blocked by:** 84 — Правила плана недели (оба тикета правят `PlanEndpoints.cs`).

**Status:** ready-for-agent

## Design (из grilling)

- `Domain/WeekStart.cs` (static, чистый): `TryParse(string, out DateOnly monday)` (`yyyy-MM-dd` + «это понедельник»), `Format(DateOnly)`, `Start(DateOnly)` (понедельник недели даты), `Window(DateOnly monday, int weeks) → (Start, End)`.
- `Recipes/Repetition/RepetitionRules.cs` **удаляется**: окно → `WeekStart`, константы → `SettingsCatalog`. Мёртвые `RepetitionRules.MinWindowWeeks`/`MaxWindowWeeks` исчезают.
- `RepetitionCounter` внедряет `SettingsService` и читает окно через него; анонимный пользователь → `SettingsCatalog.DefaultRepetitionWindowWeeks`; прямой `_db.UserSettings` в счётчике убирается.
- Call sites: `PlanEndpoints`, `ShoppingListEndpoints`, `WeekPlanReader` используют `WeekStart`; три приватных `WeekStartFormat`/`TryParseWeekStart`/`Format` удаляются.

## Checklist

- [ ] `Domain/WeekStart.cs` с `TryParse`/`Format`/`Start`/`Window` и прямыми тестами.
- [ ] `PlanEndpoints`, `ShoppingListEndpoints`, `WeekPlanReader` переведены на `WeekStart`; `WeekStartFormat`/`TryParseWeekStart`/`Format` удалены.
- [ ] `RepetitionRules` удалён; `RepetitionCounter` читает окно через `SettingsService`; `SettingsCatalog` — единственный источник констант.
- [ ] `RepetitionRulesTests` переориентированы в `WeekStartTests` (включая `TryParse`/`Format`); добавлен тест чтения окна счётчиком через настройки.
- [ ] CONTEXT.md: `_Код_` терминов «Повторяемость» и «Окно повторяемости» (`RepetitionRules` → `WeekStart`), обновлён раздел «Разделение кода».
- [ ] `dotnet test` и `scripts/test-postgres.sh` зелёные; HTTP-контракты и тексты ошибок не изменились.

## Evidence

_(заполняется при реализации)_

## Comments
