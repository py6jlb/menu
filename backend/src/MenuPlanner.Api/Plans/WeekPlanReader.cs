using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.External;

namespace MenuPlanner.Api.Plans;

/// <summary>Проекция контента плана: только имена (карточка плана) или ингредиенты (закупка).</summary>
public enum PlanProjection
{
    Names,
    Ingredients
}

/// <summary>
/// Актуальная запись плана: локальная identity записи, разрешённое состояние ссылки
/// и, если запрошена проекция ингредиентов, контент, материализованный на живой источник.
/// Для сломанной ссылки контент не выдумывается: остаётся кэш имени без ингредиентов.
/// </summary>
public sealed record PlanContentEntry(
    int Day,
    string MealType,
    Guid RecipeId,
    int Portions,
    string RecipeName,
    ExternalRecipeState? State,
    Recipe? Content);

/// <summary>Разрешённый контент недели: записи с состоянием и (по проекции) живым контентом.</summary>
public sealed record WeekPlanContent(
    DateOnly WeekStart,
    int Revision,
    IReadOnlyList<PlanContentEntry> Entries);

/// <summary>
/// Единое чтение недельного плана: и карточка плана, и список покупок получают
/// записи, состояние внешних ссылок и живой контент из этого модуля, поэтому
/// правила не расходятся между сценариями. Проекция отличается: карточке нужны
/// только имена (без дочерних коллекций источника), закупке — ингредиенты и порции.
/// Scoped-сервис, читает БД.
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

    /// <summary>Карточка плана недели или null, если он ещё не создан.</summary>
    public async Task<WeekPlanDto?> ReadAsync(
        Guid familyId, DateOnly weekStart, CancellationToken cancellationToken = default)
    {
        var content = await LoadAsync(familyId, weekStart, PlanProjection.Names, cancellationToken);
        return content is null ? null : ToDto(content);
    }

    /// <summary>
    /// Контент недели для закупки: живые ингредиенты и порции источников, состояние
    /// ссылок. Null, если плана на неделю нет. Сломанные записи остаются без контента.
    /// </summary>
    public Task<WeekPlanContent?> ReadContentAsync(
        Guid familyId, DateOnly weekStart, CancellationToken cancellationToken = default) =>
        LoadAsync(familyId, weekStart, PlanProjection.Ingredients, cancellationToken);

    /// <summary>Существует ли уже план на эту неделю (для идемпотентного удаления).</summary>
    public Task<bool> ExistsAsync(Guid familyId, DateOnly weekStart, CancellationToken cancellationToken = default) =>
        _db.WeekPlans.AnyAsync(p => p.FamilyId == familyId && p.WeekStart == weekStart, cancellationToken);

    /// <summary>Пустая карточка недели с исходной ревизией — условием создания.</summary>
    public static WeekPlanDto Empty(DateOnly monday) =>
        new(Format(monday), WeekPlanRevisions.Initial, Array.Empty<PlanEntryDto>());

    private async Task<WeekPlanContent?> LoadAsync(
        Guid familyId, DateOnly weekStart, PlanProjection projection, CancellationToken cancellationToken)
    {
        IQueryable<WeekPlan> query = _db.WeekPlans
            .AsNoTracking()
            .Include(p => p.Entries)
            .ThenInclude(e => e.Recipe);

        if (projection == PlanProjection.Ingredients)
        {
            query = _db.WeekPlans
                .AsNoTracking()
                .Include(p => p.Entries)
                .ThenInclude(e => e.Recipe)
                .ThenInclude(r => r!.Ingredients);
        }

        var plan = await query.FirstOrDefaultAsync(
            p => p.FamilyId == familyId && p.WeekStart == weekStart, cancellationToken);
        if (plan is null)
            return null;

        var entries = plan.Entries
            .OrderBy(e => e.Day)
            .ThenBy(e => PlanningCatalog.OrderOf(e.MealType))
            .ToList();

        var states = await _stateResolver.ResolveManyAsync(ExternalPlanContent.SourceLinks(entries));

        var content = projection == PlanProjection.Ingredients
            ? await ReadIngredientsAsync(entries, states, cancellationToken)
            : ReadNames(entries, states, await LoadLiveNamesAsync(entries, cancellationToken));

        return new WeekPlanContent(plan.WeekStart, plan.Revision, content);
    }

    private async Task<List<PlanContentEntry>> ReadIngredientsAsync(
        IReadOnlyList<PlanEntry> entries,
        IReadOnlyDictionary<Guid, ExternalRecipeState> states,
        CancellationToken cancellationToken)
    {
        var sources = await _sourceLoader.LoadIngredientSourcesAsync(
            ExternalPlanContent.SourceRecipeIds(entries), cancellationToken);

        var result = new List<PlanContentEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var recipe = entry.Recipe;
            var isExternal = recipe?.SourceRecipeId is not null;
            var state = StateOf(states, recipe, isExternal);

            string name;
            Recipe? effective = null;

            if (recipe is null)
            {
                name = "";
            }
            else if (!isExternal)
            {
                name = recipe.Name;
                effective = recipe;
            }
            else if (sources.TryGetValue(recipe.SourceRecipeId!.Value, out var source))
            {
                name = source.Name;
                effective = ExternalRecipeContentResolver.Materialize(recipe, source);
            }
            else
            {
                // Сломанная ссылка: контент недоступен, устаревшие ингредиенты не подставляются.
                name = recipe.Name;
                effective = StripContent(recipe);
            }

            result.Add(NewEntry(entry, name, state, effective));
        }

        return result;
    }

    private static List<PlanContentEntry> ReadNames(
        IReadOnlyList<PlanEntry> entries,
        IReadOnlyDictionary<Guid, ExternalRecipeState> states,
        IReadOnlyDictionary<Guid, string> liveNames)
    {
        var result = new List<PlanContentEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var recipe = entry.Recipe;
            var isExternal = recipe?.SourceRecipeId is not null;
            var state = StateOf(states, recipe, isExternal);

            var name = recipe is null
                ? ""
                : isExternal && liveNames.TryGetValue(recipe.SourceRecipeId!.Value, out var live)
                    ? live
                    : recipe.Name;

            result.Add(NewEntry(entry, name, state, content: null));
        }

        return result;
    }

    private async Task<Dictionary<Guid, string>> LoadLiveNamesAsync(
        IReadOnlyList<PlanEntry> entries, CancellationToken cancellationToken)
    {
        var summaries = await _sourceLoader.LoadSummariesAsync(
            ExternalPlanContent.SourceRecipeIds(entries), cancellationToken);
        return summaries.ToDictionary(x => x.Key, x => x.Value.Name);
    }

    private static ExternalRecipeState? StateOf(
        IReadOnlyDictionary<Guid, ExternalRecipeState> states, Recipe? recipe, bool isExternal) =>
        isExternal && recipe is not null && states.TryGetValue(recipe.Id, out var state)
            ? state
            : null;

    /// <summary>Копия обёртки без контента: у сломанной ссылки ингредиенты/шаги недоступны.</summary>
    private static Recipe StripContent(Recipe recipe) => new()
    {
        Id = recipe.Id,
        FamilyId = recipe.FamilyId,
        Name = recipe.Name,
        Description = recipe.Description,
        PhotoPath = recipe.PhotoPath,
        CookTimeMinutes = recipe.CookTimeMinutes,
        Servings = recipe.Servings,
        Difficulty = recipe.Difficulty,
        Calories = recipe.Calories,
        Tags = recipe.Tags,
        Seasonality = recipe.Seasonality,
        Diet = recipe.Diet,
        CreatedAt = recipe.CreatedAt,
        UpdatedAt = recipe.UpdatedAt,
        Revision = recipe.Revision,
        SourceRecipeId = recipe.SourceRecipeId,
        SourceFamilyId = recipe.SourceFamilyId,
        SourceToken = recipe.SourceToken,
        CopiedFromFamilyName = recipe.CopiedFromFamilyName
    };

    private static PlanContentEntry NewEntry(
        PlanEntry entry, string name, ExternalRecipeState? state, Recipe? content) =>
        new(entry.Day, PlanningCatalog.CodeOf(entry.MealType), entry.RecipeId, entry.Portions, name, state, content);

    private static WeekPlanDto ToDto(WeekPlanContent content) =>
        new(
            Format(content.WeekStart),
            content.Revision,
            content.Entries
                .Select(e => new PlanEntryDto(
                    e.Day,
                    e.MealType,
                    e.RecipeId,
                    e.RecipeName,
                    e.Portions,
                    e.State is null ? null : ExternalRecipeStateRules.Code(e.State.Value)))
                .ToList());

    internal static string Format(DateOnly date) =>
        date.ToString(WeekStartFormat, CultureInfo.InvariantCulture);
}
