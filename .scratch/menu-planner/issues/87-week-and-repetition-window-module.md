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

- [x] `Domain/WeekStart.cs` с `TryParse`/`Format`/`Start`/`Window` и прямыми тестами.
- [x] `PlanEndpoints`, `ShoppingListEndpoints`, `WeekPlanReader` переведены на `WeekStart`; `WeekStartFormat`/`TryParseWeekStart`/`Format` удалены.
- [x] `RepetitionRules` удалён; `RepetitionCounter` читает окно через `SettingsService`; `SettingsCatalog` — единственный источник констант.
- [x] `RepetitionRulesTests` переориентированы в `WeekStartTests` (включая `TryParse`/`Format`); добавлен тест чтения окна счётчиком через настройки.
- [x] CONTEXT.md: `_Код_` терминов «Повторяемость» и «Окно повторяемости» (`RepetitionRules` → `WeekStart`), обновлён раздел «Разделение кода».
- [x] `dotnet test` и `scripts/test-postgres.sh` зелёные; HTTP-контракты и тексты ошибок не изменились.

## Evidence

- `Domain/WeekStart.cs` (static): `TryParse` (строгий `yyyy-MM-dd` + понедельник, прежняя семантика `out`), `Format`, `Start` (понедельник недели даты), `Window(selectedWeekStart, weeks)`. Формат вынесен в `public const FormatPattern`, чтобы тексты ошибок 400 не дублировали литерал.
- Удалён `Recipes/Repetition/RepetitionRules.cs`; его константы окна — в `SettingsCatalog` (`DefaultRepetitionWindowWeeks`/`Min`/`Max`), мёртвые `MinWindowWeeks`/`MaxWindowWeeks` исчезли. `RepetitionCounter` внедряет `SettingsService`, читает окно через `ReadAsync`, прямой `_db.UserSettings` убран; анонимный/без `userId` → `SettingsCatalog.DefaultRepetitionWindowWeeks`. Публичные сигнатуры `RepetitionCounter` и `RepetitionWindowResult` не менялись.
- `PlanEndpoints` (3 × `TryParseWeekStart` + `WeekStartFormat`), `ShoppingListEndpoints` (`TryParseWeekStart` + `Format` + `WeekStartFormat`), `WeekPlanReader` (`WeekStartFormat` + `Format`, включая `Empty`/`ToDto`) переведены на `WeekStart`; приватные хелперы и константы удалены. HTTP-статусы и тексты 400 («Некорректная дата начала недели… `yyyy-MM-dd`») не изменились.
- CONTEXT.md: «Повторяемость» `_Код_` → `WeekStart`, `RepetitionCounter`; «Окно повторяемости» → `SettingsCatalog.DefaultRepetitionWindowWeeks`, `WeekStart.Window`; stateless-список → `WeekStart`.
- Тесты: `RepetitionRulesTests` → `WeekStartTests` (`Start`/`Window`/`TryParse`/`Format`); новый `RepetitionCounterTests` (сохранённое значение, отсутствие строки → дефолт, анонимный → дефолт, окно в `RepetitionWindowResult`); `DbServicesTests` передаёт `SettingsService` в конструктор счётчика.
- Прогоны: `docker run --rm -v "$(pwd)":/app -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test` → Failed: 0, Passed: 684, Skipped: 62; `scripts/test-postgres.sh` → Failed: 0, Passed: 70, Skipped: 0.

## Comments
