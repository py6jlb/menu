using Microsoft.EntityFrameworkCore;
using MenuPlanner.Infrastructure.Data;
using MenuPlanner.Api.Settings;
using MenuPlanner.Domain.Plans;

namespace MenuPlanner.Api.Recipes.Repetition;

/// <summary>Показатель повторяемости и размер личного окна, к которому он посчитан.</summary>
public sealed record RepetitionWindowResult(int WindowWeeks, Dictionary<Guid, int> Counts);

/// <summary>
/// Считает, в скольких различных неделях планов семьи встречается рецепт за окно
/// (повторения внутри одной недели дают один вклад). Scoped-сервис: читает БД и
/// личные настройки; чистые правила недели и окна — в <see cref="WeekStart"/>.
/// </summary>
public sealed class RepetitionCounter
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;

    public RepetitionCounter(AppDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    /// <summary>
    /// Повторяемость семьи за окно, выбранное в настройках конкретного пользователя
    /// (по умолчанию — <see cref="SettingsCatalog.DefaultRepetitionWindowWeeks"/>). Окно
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
            ? WeekStart.Start(selected)
            : WeekStart.Start(DateOnly.FromDateTime(DateTime.UtcNow));

        var (windowStart, windowEnd) = WeekStart.Window(anchor, weeks);
        var counts = await CountForFamilyAsync(familyId, windowStart, windowEnd);
        return new RepetitionWindowResult(weeks, counts);
    }

    /// <summary>Личное окно повторяемости пользователя; default — для анонимного/нового.</summary>
    public async Task<int> ResolveWindowWeeksAsync(
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (userId is not { } id)
            return SettingsCatalog.DefaultRepetitionWindowWeeks;

        var settings = await _settings.ReadAsync(id, cancellationToken);
        return settings.RepetitionWindowWeeks;
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
