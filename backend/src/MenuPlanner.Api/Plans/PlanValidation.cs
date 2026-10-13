using MenuPlanner.Domain.Plans;

namespace MenuPlanner.Api.Plans;

/// <summary>
/// Чистые правила записей недельного плана: статический класс без состояния,
/// не зависит от DI и БД, юнит-тестируется напрямую. Здесь только проверка
/// формы набора — принадлежность рецептов семье охраняется инвариантом
/// <see cref="WeekPlanSaver"/>.
/// </summary>
public static class PlanValidation
{
    /// <summary>
    /// Проверяет полный набор записей недели. Возвращает первую ошибку или
    /// <c>null</c>, если набор корректен. Порядок проверок задаёт, какая ошибка
    /// вернётся при нескольких нарушениях.
    /// </summary>
    public static PlanErrorDto? ValidateEntries(IReadOnlyList<PlanEntryRequest> entries)
    {
        if (entries.Count > PlanningCatalog.EntriesMax)
            return new(
                $"В плане не может быть больше {PlanningCatalog.EntriesMax} записей.",
                "plan_entries_too_many",
                "entries");

        var seen = new HashSet<(int Day, string MealType)>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var field = $"entries[{index}]";

            // Malformed payload может прислать null вместо записи: отказ должен быть
            // контролируемым, а не исключением обращения к null.
            if (entry is null)
                return new("Запись плана пуста.", "plan_entry_null", field);

            if (entry.Day < PlanningCatalog.DayMin || entry.Day > PlanningCatalog.DayMax)
                return new(
                    $"Номер дня недели должен быть от {PlanningCatalog.DayMin} до {PlanningCatalog.DayMax}.",
                    "plan_day_range",
                    $"{field}.day");
            if (!PlanningCatalog.IsKnownMealType(entry.MealType))
                return new($"Недопустимый приём пищи «{entry.MealType}».", "plan_meal_type_invalid", $"{field}.mealType");
            if (entry.Portions < PlanningCatalog.PortionsMin || entry.Portions > PlanningCatalog.PortionsMax)
                return new(
                    $"Количество порций должно быть от {PlanningCatalog.PortionsMin} до {PlanningCatalog.PortionsMax}.",
                    "plan_portions_range",
                    $"{field}.portions");
            if (!seen.Add((entry.Day, entry.MealType)))
                return new(
                    "В плане не может быть двух записей для одного дня и приёма пищи.",
                    "plan_duplicate_slot",
                    field);
        }

        return null;
    }
}
