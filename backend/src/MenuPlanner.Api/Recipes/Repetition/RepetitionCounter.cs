using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Recipes.Repetition;

/// <summary>Показатель повторяемости и размер личного окна, к которому он посчитан.</summary>
public sealed record RepetitionWindowResult(int WindowWeeks, Dictionary<Guid, int> Counts);

/// <summary>
/// Считает, в скольких различных неделях планов семьи встречается рецепт за окно
/// (повторения внутри одной недели дают один вклад). Scoped-сервис: читает БД;
/// чистые правила окна — в <see cref="RepetitionRules"/>.
/// </summary>
public sealed class RepetitionCounter
{
    private readonly AppDbContext _db;

    public RepetitionCounter(AppDbContext db) => _db = db;

    /// <summary>
    /// Повторяемость семьи за окно, выбранное в настройках конкретного пользователя
    /// (по умолчанию — <see cref="RepetitionRules.DefaultWindowWeeks"/>). Окно
    /// заканчивается <paramref name="weekStart"/> включительно, если он передан
    /// (подбор для выбранной недели); иначе — текущей неделей сервера.
    /// </summary>
    public async Task<Dictionary<Guid, int>> CountForUserAsync(
        Guid? userId,
        Guid familyId,
        DateOnly? weekStart = null,
        CancellationToken cancellationToken = default)
    {
        var result = await CountWindowForUserAsync(userId, familyId, weekStart, cancellationToken);
        return result.Counts;
    }

    /// <summary>
    /// То же, что <see cref="CountForUserAsync"/>, но вместе с размером окна, чтобы
    /// вызывающий показал объяснение, не читая настройки повторно.
    /// </summary>
    public async Task<RepetitionWindowResult> CountWindowForUserAsync(
        Guid? userId,
        Guid familyId,
        DateOnly? weekStart = null,
        CancellationToken cancellationToken = default)
    {
        var weeks = await ResolveWindowWeeksAsync(userId, cancellationToken);
        var anchor = weekStart is { } selected
            ? RepetitionRules.WeekStart(selected)
            : RepetitionRules.CurrentWeekStart();

        var (windowStart, windowEnd) = RepetitionRules.Window(anchor, weeks);
        var counts = await CountForFamilyAsync(familyId, windowStart, windowEnd);
        return new RepetitionWindowResult(weeks, counts);
    }

    /// <summary>Личное окно повторяемости пользователя; default — для анонимного/нового.</summary>
    public async Task<int> ResolveWindowWeeksAsync(
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (userId is not { } id)
            return RepetitionRules.DefaultWindowWeeks;

        var settings = await _db.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == id, cancellationToken);

        return settings?.RepetitionWindowWeeks ?? RepetitionRules.DefaultWindowWeeks;
    }

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
