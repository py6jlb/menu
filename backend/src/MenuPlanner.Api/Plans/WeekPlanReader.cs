using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.External;

namespace MenuPlanner.Api.Plans;

/// <summary>
/// Чтение недельного плана: загрузка записей с рецептами, состояния внешних
/// источников и живой контент источников — всё в одном месте. GET, успех PUT
/// и сборка конфликта используют один и тот же проектор, поэтому карточка плана
/// не расходится между сценариями. Scoped-сервис, читает БД.
/// </summary>
public sealed class WeekPlanReader
{
    private const string WeekStartFormat = "yyyy-MM-dd";

    private readonly AppDbContext _db;
    private readonly ExternalRecipeStateResolver _stateResolver;
    private readonly ExternalRecipeSourceLoader _sourceLoader;

    public WeekPlanReader(
        AppDbContext db,
        ExternalRecipeStateResolver stateResolver,
        ExternalRecipeSourceLoader sourceLoader)
    {
        _db = db;
        _stateResolver = stateResolver;
        _sourceLoader = sourceLoader;
    }

    /// <summary>План недели или null, если он ещё не создан.</summary>
    public async Task<WeekPlanDto?> ReadAsync(
        Guid familyId, DateOnly weekStart, CancellationToken cancellationToken = default)
    {
        var plan = await _db.WeekPlans
            .AsNoTracking()
            .Include(p => p.Entries)
            .ThenInclude(e => e.Recipe)
            .FirstOrDefaultAsync(p => p.FamilyId == familyId && p.WeekStart == weekStart, cancellationToken);
        if (plan is null)
            return null;

        var states = await _stateResolver.ResolveManyAsync(ExternalPlanContent.SourceLinks(plan.Entries));
        var liveSources = await _sourceLoader.LoadSourcesAsync(ExternalPlanContent.SourceRecipeIds(plan.Entries));
        return ToDto(plan, states, liveSources);
    }

    /// <summary>Существует ли уже план на эту неделю (для идемпотентного удаления).</summary>
    public Task<bool> ExistsAsync(Guid familyId, DateOnly weekStart, CancellationToken cancellationToken = default) =>
        _db.WeekPlans.AnyAsync(p => p.FamilyId == familyId && p.WeekStart == weekStart, cancellationToken);

    /// <summary>Пустая карточка недели с исходной ревизией — условием создания.</summary>
    public static WeekPlanDto Empty(DateOnly monday) =>
        new(Format(monday), WeekPlanRevisions.Initial, Array.Empty<PlanEntryDto>());

    private static WeekPlanDto ToDto(
        WeekPlan plan,
        IReadOnlyDictionary<Guid, ExternalRecipeState> states,
        IReadOnlyDictionary<Guid, Recipe> liveSources)
    {
        var entries = plan.Entries
            .OrderBy(e => e.Day)
            .ThenBy(e => PlanningCatalog.OrderOf(e.MealType))
            .Select(e => new PlanEntryDto(
                e.Day,
                PlanningCatalog.CodeOf(e.MealType),
                e.RecipeId,
                e.Recipe is null
                    ? ""
                    : ExternalRecipeContentResolver.Resolve(e.Recipe, liveSources).Name,
                e.Portions,
                states.TryGetValue(e.RecipeId, out var state)
                    ? ExternalRecipeStateRules.Code(state)
                    : null))
            .ToList();

        return new WeekPlanDto(Format(plan.WeekStart), plan.Revision, entries);
    }

    internal static string Format(DateOnly date) =>
        date.ToString(WeekStartFormat, CultureInfo.InvariantCulture);
}
