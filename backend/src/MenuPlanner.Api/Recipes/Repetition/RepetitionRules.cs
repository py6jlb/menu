namespace MenuPlanner.Api.Recipes.Repetition;

public static class RepetitionRules
{
    public const int DefaultWindowWeeks = 3;
    public const int MinWindowWeeks = 1;
    public const int MaxWindowWeeks = 52;

    public static DateOnly CurrentWeekStart() => CurrentWeekStart(DateTime.UtcNow);

    public static DateOnly CurrentWeekStart(DateTime now)
    {
        var date = DateOnly.FromDateTime(now);
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    public static (DateOnly Start, DateOnly End) Window(DateOnly currentWeekStart, int weeks)
    {
        var start = currentWeekStart.AddDays(-(weeks - 1) * 7);
        return (start, currentWeekStart);
    }
}