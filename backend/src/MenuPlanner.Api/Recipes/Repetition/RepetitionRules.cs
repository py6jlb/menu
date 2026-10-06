namespace MenuPlanner.Api.Recipes.Repetition;

public static class RepetitionRules
{
    public const int DefaultWindowWeeks = 3;
    public const int MinWindowWeeks = 1;
    public const int MaxWindowWeeks = 52;

    public static DateOnly CurrentWeekStart() => CurrentWeekStart(DateTime.UtcNow);

    public static DateOnly CurrentWeekStart(DateTime now) => WeekStart(DateOnly.FromDateTime(now));

    /// <summary>Понедельник недели, которой принадлежит дата. Неделя начинается в понедельник.</summary>
    public static DateOnly WeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    /// <summary>
    /// Окно из <paramref name="weeks"/> недель включительно, заканчивающееся выбранной
    /// неделей. Конец — сама выбранная неделя, начало — минус (weeks − 1) недель.
    /// </summary>
    public static (DateOnly Start, DateOnly End) Window(DateOnly selectedWeekStart, int weeks)
    {
        var start = selectedWeekStart.AddDays(-(weeks - 1) * 7);
        return (start, selectedWeekStart);
    }
}
