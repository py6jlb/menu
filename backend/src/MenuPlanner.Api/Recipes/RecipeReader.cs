using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;
using MenuPlanner.Api.Recipes.External;

namespace MenuPlanner.Api.Recipes;

/// <summary>Срез списка рецептов: все, только собственные или только внешние.</summary>
public enum RecipeScope
{
    All,
    Own,
    External
}

/// <summary>
/// Актуальный рецепт для краткого списка: id и принадлежность — локальные, контент
/// взят из живого источника, если он есть, иначе из кэша имени. Состояние/происхождение
/// уже разрешены модулем; потребитель не собирает их сам.
/// </summary>
public sealed record RecipeSummary(
    Guid Id,
    string Name,
    int Difficulty,
    int? Calories,
    int CookTimeMinutes,
    int Servings,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Seasonality,
    IReadOnlyList<string> Diet,
    string? PhotoPath,
    bool IsExternal,
    string? SourceFamilyName,
    ExternalRecipeState? State,
    string? CopiedFromFamilyName);

/// <summary>
/// Актуальный рецепт для подробной страницы: <see cref="Recipe"/> уже материализован
/// на живой источник (id и ревизия остаются локальными), а состояние и происхождение
/// разрешены модулем. Для сломанной ссылки контент пуст, остаётся кэш имени.
/// </summary>
public sealed record RecipeDetail(
    Recipe Recipe,
    bool IsExternal,
    string? SourceFamilyName,
    Guid? SourceFamilyId,
    ExternalRecipeState? State);

/// <summary>
/// Кандидат подбора: актуальный контент (внешний рецепт спроецирован на живой источник),
/// локальная identity и разрешённые состояние/происхождение. Сломанный источник сюда
/// не попадает.
/// </summary>
public sealed record RecipeMatchCandidate(
    Recipe Recipe,
    bool IsExternal,
    string? SourceFamilyName,
    ExternalRecipeState? State);

/// <summary>
/// Единое предметное чтение рецепта для списка и деталей. Прячет от потребителей связь
/// локального рецепта с источником, доступность источника и происхождение: вызывающий
/// получает уже разрешённые состояние, подпись семьи-источника и правильный контент.
/// Scoped-сервис: читает БД и обновляет кэш имени при чтении.
/// </summary>
public sealed class RecipeReader
{
    private readonly AppDbContext _db;
    private readonly ExternalRecipeSourceLoader _sources;
    private readonly ExternalRecipeStateResolver _states;
    private readonly ExternalRecipeNameCache _nameCache;
    private readonly SourceFamilyNameResolver _sourceNames;

    public RecipeReader(
        AppDbContext db,
        ExternalRecipeSourceLoader sources,
        ExternalRecipeStateResolver states,
        ExternalRecipeNameCache nameCache,
        SourceFamilyNameResolver sourceNames)
    {
        _db = db;
        _sources = sources;
        _states = states;
        _nameCache = nameCache;
        _sourceNames = sourceNames;
    }

    /// <summary>
    /// Краткие рецепты семьи выбранного среза. Источники читаются скалярной проекцией
    /// одним запросом: шаги и ингредиенты для списка не загружаются, пофайлового запроса
    /// на каждый внешний рецепт нет. Кэш имени обновляется, если источник изменился.
    /// </summary>
    public async Task<IReadOnlyList<RecipeSummary>> ReadSummariesAsync(
        Guid familyId, RecipeScope scope, CancellationToken cancellationToken = default)
    {
        var query = _db.Recipes
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId);

        query = scope switch
        {
            RecipeScope.Own => query.Where(r => r.SourceRecipeId == null),
            RecipeScope.External => query.Where(r => r.SourceRecipeId != null),
            _ => query
        };

        var stored = await query
            .OrderBy(r => r.Name)
            .Select(r => new StoredSummary(
                r.Id,
                r.Name,
                r.Difficulty,
                r.Calories,
                r.CookTimeMinutes,
                r.Servings,
                r.Tags,
                r.Seasonality,
                r.Diet,
                r.PhotoPath,
                r.SourceRecipeId,
                r.SourceFamilyId,
                r.SourceToken,
                r.CopiedFromFamilyName))
            .ToListAsync(cancellationToken);

        if (stored.Count == 0)
            return Array.Empty<RecipeSummary>();

        var external = stored
            .Where(r => r.SourceRecipeId is not null)
            .ToList();

        var liveSources = await _sources.LoadSummariesAsync(
            external.Select(r => r.SourceRecipeId!.Value), cancellationToken);

        var staleIds = stored
            .Where(r => r.SourceRecipeId is Guid sourceId
                && liveSources.TryGetValue(sourceId, out var live)
                && !string.Equals(r.Name, live.Name, StringComparison.Ordinal))
            .Select(r => r.Id)
            .ToList();
        await _nameCache.RefreshAsync(
            staleIds,
            liveSources.ToDictionary(x => x.Key, x => x.Value.Name),
            cancellationToken);

        var states = await _states.ResolveManyAsync(
            external
                .Select(r => new ExternalSourceLink(r.Id, r.SourceRecipeId!.Value, r.SourceToken))
                .ToList());

        var familyNames = await _sourceNames.ResolveManyAsync(
            stored
                .Where(r => r.SourceFamilyId is not null)
                .Select(r => r.SourceFamilyId!.Value));

        return stored
            .Select(r => Summarize(r, liveSources, states, familyNames))
            .ToList();
    }

    /// <summary>
    /// Рецепты семьи как кандидаты подбора: локальный id и принадлежность сохранены,
    /// контент внешнего рецепта проецируется на живой источник, состояние/происхождение
    /// разрешены. Сломанный источник исключён — его нельзя подтвердить как доступное блюдо.
    /// </summary>
    public async Task<IReadOnlyList<RecipeMatchCandidate>> ReadMatchCandidatesAsync(
        Guid familyId, CancellationToken cancellationToken = default)
    {
        var recipes = await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Where(r => r.FamilyId == familyId)
            .ToListAsync(cancellationToken);

        if (recipes.Count == 0)
            return Array.Empty<RecipeMatchCandidate>();

        var external = recipes.Where(r => r.SourceRecipeId is not null).ToList();

        var liveSources = await _sources.LoadSourcesAsync(
            external.Select(r => r.SourceRecipeId!.Value), cancellationToken);

        var staleIds = external
            .Where(r => liveSources.TryGetValue(r.SourceRecipeId!.Value, out var live)
                && !string.Equals(r.Name, live.Name, StringComparison.Ordinal))
            .Select(r => r.Id)
            .ToList();
        await _nameCache.RefreshAsync(
            staleIds,
            liveSources.ToDictionary(x => x.Key, x => x.Value.Name),
            cancellationToken);

        var states = await _states.ResolveManyAsync(
            external
                .Select(r => new ExternalSourceLink(r.Id, r.SourceRecipeId!.Value, r.SourceToken))
                .ToList());

        var familyNames = await _sourceNames.ResolveManyAsync(
            recipes
                .Where(r => r.SourceFamilyId is not null)
                .Select(r => r.SourceFamilyId!.Value));

        var candidates = new List<RecipeMatchCandidate>();
        foreach (var recipe in recipes)
        {
            var isExternal = recipe.SourceRecipeId is not null;
            ExternalRecipeState? state = null;
            string? sourceFamilyName = null;

            if (isExternal)
            {
                state = states.GetValueOrDefault(recipe.Id, ExternalRecipeState.Broken);
                sourceFamilyName = recipe.SourceFamilyId is Guid familyId2
                    ? familyNames.GetValueOrDefault(familyId2)
                    : null;

                if (state == ExternalRecipeState.Broken)
                    continue;
            }

            var effective = ExternalRecipeContentResolver.Resolve(recipe, liveSources);
            effective.Revision = recipe.Revision;
            candidates.Add(new RecipeMatchCandidate(effective, isExternal, sourceFamilyName, state));
        }

        return candidates;
    }

    /// <summary>
    /// Подробный рецепт семьи или null, если его нет в этой семье. Живой источник читается
    /// с шагами и ингредиентами; при удалённом источнике контент не выдумывается —
    /// возвращается кэш имени со состоянием broken.
    /// </summary>
    public async Task<RecipeDetail?> ReadDetailAsync(
        Guid familyId, Guid recipeId, CancellationToken cancellationToken = default)
    {
        var recipe = await _db.Recipes
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == recipeId && r.FamilyId == familyId, cancellationToken);
        if (recipe is null)
            return null;

        if (recipe.SourceRecipeId is not Guid sourceId)
            return new RecipeDetail(recipe, IsExternal: false, null, null, State: null);

        var states = await _states.ResolveManyAsync(
            new[] { new ExternalSourceLink(recipe.Id, sourceId, recipe.SourceToken) });
        var state = states.GetValueOrDefault(recipe.Id, ExternalRecipeState.Broken);

        var sourceFamilyName = await _sourceNames.ResolveAsync(recipe.SourceFamilyId);

        var source = await _sources.LoadFullAsync(sourceId, cancellationToken);
        if (source is null)
            return new RecipeDetail(recipe, IsExternal: true, sourceFamilyName, recipe.SourceFamilyId, state);

        await _nameCache.RefreshAsync(
            new[] { recipe.Id },
            new Dictionary<Guid, string> { [sourceId] = source.Name },
            cancellationToken);

        var effective = ExternalRecipeContentResolver.Materialize(recipe, source);
        // Контент и даты создания/правки — живого источника (как и до рефакторинга),
        // а id, принадлежность семьи, ревизия и метка копии — локальной строки-получателя.
        effective.CreatedAt = source.CreatedAt;
        effective.Revision = recipe.Revision;
        effective.CopiedFromFamilyName = recipe.CopiedFromFamilyName;

        return new RecipeDetail(effective, IsExternal: true, sourceFamilyName, recipe.SourceFamilyId, state);
    }

    private static RecipeSummary Summarize(
        StoredSummary stored,
        IReadOnlyDictionary<Guid, RecipeSourceSummary> liveSources,
        IReadOnlyDictionary<Guid, ExternalRecipeState> states,
        IReadOnlyDictionary<Guid, string> familyNames)
    {
        var isExternal = stored.SourceRecipeId is not null;

        ExternalRecipeState? state = isExternal && states.TryGetValue(stored.Id, out var resolved)
            ? resolved
            : null;

        var sourceFamilyName = isExternal && stored.SourceFamilyId is Guid sourceFamilyId
            ? familyNames.GetValueOrDefault(sourceFamilyId)
            : null;

        // Внешний рецепт показывает живой контент источника целиком; у него самого
        // кэшируется только имя, остальные поля могут быть пустыми/устаревшими.
        var live = isExternal && liveSources.GetValueOrDefault(stored.SourceRecipeId!.Value) is { } source
            ? source
            : null;

        return new RecipeSummary(
            stored.Id,
            live?.Name ?? stored.Name,
            live?.Difficulty ?? stored.Difficulty,
            live is null ? stored.Calories : live.Calories,
            live?.CookTimeMinutes ?? stored.CookTimeMinutes,
            live?.Servings ?? stored.Servings,
            live?.Tags ?? stored.Tags,
            live?.Seasonality ?? stored.Seasonality,
            live?.Diet ?? stored.Diet,
            live is null ? stored.PhotoPath : live.PhotoPath,
            isExternal,
            sourceFamilyName,
            state,
            stored.CopiedFromFamilyName);
    }

    private sealed record StoredSummary(
        Guid Id,
        string Name,
        int Difficulty,
        int? Calories,
        int CookTimeMinutes,
        int Servings,
        List<string> Tags,
        List<string> Seasonality,
        List<string> Diet,
        string? PhotoPath,
        Guid? SourceRecipeId,
        Guid? SourceFamilyId,
        string? SourceToken,
        string? CopiedFromFamilyName);
}
