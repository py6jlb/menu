using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.External;

namespace MenuPlanner.Api.Plans;

/// <summary>Проекция контента плана: только имена (карточка плана) или ингредиенты (список покупок).</summary>
public enum PlanProjection
{
    Names,
    Ingredients
}

/// <summary>
/// Актуальная запись плана: локальная identity записи, разрешённое состояние ссылки
/// и, если запрошена проекция ингредиентов, контент, материализованный на живой источник.
/// Для сломанной ссылки контент отсутствует: устаревшие ингредиенты не подставляются.
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
/// только имена (без дочерних коллекций источника), списку покупок — ингредиенты
/// и порции. Наличие живого источника — единственный факт, определяющий сломанную
/// ссылку: если контент прочитать не удалось, запись честно помечается broken.
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
    /// Контент недели для списка покупок: живые ингредиенты и порции источников,
    /// состояние ссылок. Null, если плана на неделю нет. Сломанные записи приходят
    /// без контента и попадают в диагностику полноты у потребителя.
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
        IQueryable<WeekPlan> query = _db.WeekPlans.AsNoTracking();
        query = projection == PlanProjection.Ingredients
            ? query
                .Include(p => p.Entries)
                .ThenInclude(e => e.Recipe)
                .ThenInclude(r => r!.Ingredients)
            : query
                .Include(p => p.Entries)
                .ThenInclude(e => e.Recipe);

        var plan = await query.FirstOrDefaultAsync(
            p => p.FamilyId == familyId && p.WeekStart == weekStart, cancellationToken);
        if (plan is null)
            return null;

        var entries = plan.Entries
            .OrderBy(e => e.Day)
            .ThenBy(e => PlanningCatalog.OrderOf(e.MealType))
            .ToList();

        var states = await _stateResolver.ResolveManyAsync(ExternalPlanContent.SourceLinks(entries));
        var sourceIds = ExternalPlanContent.SourceRecipeIds(entries);

        // Живой источник — единственный источник истины: и имя, и контент, и признак
        // сломанной ссылки берутся из одной загрузки, поэтому гонка с удалением источника
        // не даёт молча потерять запись.
        Dictionary<Guid, Recipe>? liveSources = null;
        Dictionary<Guid, string> liveNames;
        if (projection == PlanProjection.Ingredients)
        {
            liveSources = await _sourceLoader.LoadIngredientSourcesAsync(sourceIds, cancellationToken);
            liveNames = liveSources.ToDictionary(x => x.Key, x => x.Value.Name);
        }
        else
        {
            var summaries = await _sourceLoader.LoadSummariesAsync(sourceIds, cancellationToken);
            liveNames = summaries.ToDictionary(x => x.Key, x => x.Value.Name);
        }

        var content = new List<PlanContentEntry>(entries.Count);
        foreach (var entry in entries)
            content.Add(Resolve(entry, projection, states, liveNames, liveSources));

        return new WeekPlanContent(plan.WeekStart, plan.Revision, content);
    }

    private static PlanContentEntry Resolve(
        PlanEntry entry,
        PlanProjection projection,
        IReadOnlyDictionary<Guid, ExternalRecipeState> states,
        IReadOnlyDictionary<Guid, string> liveNames,
        IReadOnlyDictionary<Guid, Recipe>? liveSources)
    {
        var recipe = entry.Recipe;
        if (recipe is null)
            return NewEntry(entry, "", state: null, content: null);

        if (recipe.SourceRecipeId is not Guid sourceId)
        {
            // Свой рецепт: контент уже загружен на строке записи.
            var own = projection == PlanProjection.Ingredients ? recipe : null;
            return NewEntry(entry, recipe.Name, state: null, content: own);
        }

        var sourceAlive = liveNames.TryGetValue(sourceId, out var liveName);

        // Сломанная ссылка определяется наличием живого источника, а не отдельным
        // чтением: источник, исчезнувший между разрешением состояния и загрузкой
        // контента, не оставит запись без диагностики.
        ExternalRecipeState state;
        if (!sourceAlive)
        {
            state = ExternalRecipeState.Broken;
        }
        else
        {
            state = states.TryGetValue(recipe.Id, out var resolved)
                && resolved != ExternalRecipeState.Broken
                ? resolved
                : ExternalRecipeState.Warning;
        }

        var name = sourceAlive ? liveName! : recipe.Name;

        var effective = projection == PlanProjection.Ingredients && sourceAlive
            ? ExternalRecipeContentResolver.Materialize(recipe, liveSources![sourceId])
            : null;

        return NewEntry(entry, name, state, effective);
    }

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
