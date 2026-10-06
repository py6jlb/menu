using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

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

    public static async Task<Dictionary<Guid, int>> CountForFamilyAsync(
        AppDbContext db,
        Guid familyId,
        DateOnly windowStart,
        DateOnly windowEnd)
    {
        var weekPlanIds = await db.WeekPlans
            .AsNoTracking()
            .Where(w => w.FamilyId == familyId
                && w.WeekStart >= windowStart
                && w.WeekStart <= windowEnd)
            .Select(w => w.Id)
            .ToListAsync();

        if (weekPlanIds.Count == 0)
            return new Dictionary<Guid, int>();

        var rows = await db.PlanEntries
            .AsNoTracking()
            .Where(e => weekPlanIds.Contains(e.WeekPlanId))
            .Select(e => new { e.RecipeId, e.WeekPlanId })
            .ToListAsync();

        return rows
            .Distinct()
            .GroupBy(x => x.RecipeId)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}