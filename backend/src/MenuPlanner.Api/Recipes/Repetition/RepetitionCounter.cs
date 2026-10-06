using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Recipes.Repetition;

/// <summary>
/// Считает, сколько раз рецепты встречаются в недельных планах семьи за окно.
/// Scoped-сервис: читает БД; чистые правила окна — в <see cref="RepetitionRules"/>.
/// </summary>
public sealed class RepetitionCounter
{
    private readonly AppDbContext _db;

    public RepetitionCounter(AppDbContext db) => _db = db;

    public async Task<Dictionary<Guid, int>> CountForFamilyAsync(
        Guid familyId,
        DateOnly windowStart,
        DateOnly windowEnd)
    {
        var weekPlanIds = await _db.WeekPlans
            .AsNoTracking()
            .Where(w => w.FamilyId == familyId
                && w.WeekStart >= windowStart
                && w.WeekStart <= windowEnd)
            .Select(w => w.Id)
            .ToListAsync();

        if (weekPlanIds.Count == 0)
            return new Dictionary<Guid, int>();

        var rows = await _db.PlanEntries
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
