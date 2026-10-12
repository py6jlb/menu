using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Насколько полно нужно прочитать живой контент внешнего рецепта: краткий список
/// берёт только скалярную сводку, список покупок — ингредиенты и порции, подбор и
/// подробная страница — полный рецепт с шагами.
/// </summary>
public enum ExternalRecipeProjection
{
    Summary,
    Ingredients,
    Full
}

/// <summary>
/// Связь локального внешнего рецепта с источником в том виде, в каком она нужна
/// единому модулю: строка-обёртка (её закэшированное имя нужно для детекции
/// устаревшего кэша) плюс параметры ссылки для вычисления состояния. Один тип связи
/// на все сценарии — краткий список, карточку плана, подбор и подробную страницу.
/// </summary>
public sealed record ExternalRecipeLink(Recipe Wrapper)
{
    public Guid WrapperId => Wrapper.Id;
    public Guid SourceRecipeId => Wrapper.SourceRecipeId!.Value;
    public string? SourceToken => Wrapper.SourceToken;
    public Guid? SourceFamilyId => Wrapper.SourceFamilyId;
    public string CachedName => Wrapper.Name;

    /// <summary>Минимум, нужный чистому разрешению состояния.</summary>
    public ExternalSourceLink ToSourceLink() => new(WrapperId, SourceRecipeId, SourceToken);

    /// <summary>Ссылки внешних рецептов из записей недельного плана.</summary>
    public static IReadOnlyList<ExternalRecipeLink> FromPlanEntries(IEnumerable<PlanEntry> entries) =>
        entries
            .Where(e => e.Recipe?.SourceRecipeId is not null)
            .Select(e => new ExternalRecipeLink(e.Recipe!))
            .ToList();
}

/// <summary>
/// Разрешённый внешний рецепт — единый ответ модуля: состояние ссылки, признак
/// живого источника, подпись семьи-источника и материализованный контент (либо сама
/// обёртка с кэшем имени, если источника больше нет).
/// </summary>
public sealed record ResolvedExternalRecipe(
    Guid WrapperId,
    ExternalRecipeState State,
    bool SourceAlive,
    string? SourceFamilyName,
    Recipe? Content);

/// <summary>
/// Единый module внешнего рецепта: по связи с источником разрешает состояние,
/// обновляет кэш имени, находит подпись семьи-источника и читает живой контент под
/// нужную проекцию. Композирует <see cref="ExternalRecipeStateResolver"/>,
/// <see cref="ExternalRecipeSourceLoader"/>, <see cref="ExternalRecipeNameCache"/> и
/// <see cref="SourceFamilyNameResolver"/>, поэтому правило «сломанной ссылки»
/// (ADR-0005) живёт в одном месте: источник считается живым по факту успешной
/// загрузки, а несовпавший токен или отзыв шеринга дают только предупреждение.
/// Источник, исчезнувший между разрешением состояния и загрузкой контента, честно
/// помечается сломанным. Scoped-сервис, читает БД и обновляет кэш имени.
/// </summary>
public sealed class ExternalRecipeContext
{
    private readonly ExternalRecipeStateResolver _states;
    private readonly ExternalRecipeSourceLoader _sources;
    private readonly ExternalRecipeNameCache _nameCache;
    private readonly SourceFamilyNameResolver _sourceNames;

    public ExternalRecipeContext(
        ExternalRecipeStateResolver states,
        ExternalRecipeSourceLoader sources,
        ExternalRecipeNameCache nameCache,
        SourceFamilyNameResolver sourceNames)
    {
        _states = states;
        _sources = sources;
        _nameCache = nameCache;
        _sourceNames = sourceNames;
    }

    /// <summary>
    /// Разрешает каждую связь: состояние, имя семьи-источника и контент нужной
    /// проекции по локальному id внешнего рецепта. Живой источник — единственный
    /// источник истины для контента и для признака «сломанной ссылки».
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, ResolvedExternalRecipe>> ResolveAsync(
        IReadOnlyList<ExternalRecipeLink> links,
        ExternalRecipeProjection projection,
        CancellationToken cancellationToken = default)
    {
        if (links.Count == 0)
            return new Dictionary<Guid, ResolvedExternalRecipe>();

        // Состояние разрешается до загрузки: если источник исчезнет между этими
        // шагами, загрузка его не найдёт, и запись честно станет сломанной.
        var states = await _states.ResolveManyAsync(
            links.Select(l => l.ToSourceLink()).ToList());

        var (liveNames, alive, summaries, sources) =
            await LoadAsync(links, projection, cancellationToken);

        var staleIds = links
            .Where(l => alive.Contains(l.SourceRecipeId)
                && liveNames.TryGetValue(l.SourceRecipeId, out var liveName)
                && !string.Equals(l.CachedName, liveName, StringComparison.Ordinal))
            .Select(l => l.WrapperId)
            .ToList();
        await _nameCache.RefreshAsync(staleIds, liveNames, cancellationToken);

        var familyNames = await _sourceNames.ResolveManyAsync(
            links
                .Where(l => l.SourceFamilyId is not null)
                .Select(l => l.SourceFamilyId!.Value));

        var result = new Dictionary<Guid, ResolvedExternalRecipe>(links.Count);
        foreach (var link in links)
        {
            var sourceAlive = alive.Contains(link.SourceRecipeId);
            var resolvedState = states.GetValueOrDefault(link.WrapperId, ExternalRecipeState.Broken);

            // Живой источник — обязательное условие: разрешение состояния могло
            // отработать до удаления источника, поэтому Broken из него понижается
            // до Warning, а недоступный при загрузке источник делает ссылку Broken.
            var state = !sourceAlive
                ? ExternalRecipeState.Broken
                : resolvedState == ExternalRecipeState.Broken
                    ? ExternalRecipeState.Warning
                    : resolvedState;

            var content = projection == ExternalRecipeProjection.Summary
                ? ExternalRecipeContentResolver.Project(
                    link.Wrapper, summaries.GetValueOrDefault(link.SourceRecipeId))
                : ExternalRecipeContentResolver.Materialize(
                    link.Wrapper, sources.GetValueOrDefault(link.SourceRecipeId));

            var familyName = link.SourceFamilyId is Guid familyId
                ? familyNames.GetValueOrDefault(familyId)
                : null;

            result[link.WrapperId] = new ResolvedExternalRecipe(
                link.WrapperId, state, sourceAlive, familyName, content);
        }

        return result;
    }

    /// <summary>
    /// Выбирает загрузчик по проекции: сводка — скалярно, ингредиенты — без шагов,
    /// полный контент — с шагами. Возвращает живое имя источников, признаки живости
    /// и загруженные проекции для материализации.
    /// </summary>
    private async Task<(
        Dictionary<Guid, string> LiveNames,
        HashSet<Guid> Alive,
        Dictionary<Guid, RecipeSourceSummary> Summaries,
        Dictionary<Guid, Recipe> Sources)> LoadAsync(
        IReadOnlyList<ExternalRecipeLink> links,
        ExternalRecipeProjection projection,
        CancellationToken cancellationToken)
    {
        var sourceIds = links.Select(l => l.SourceRecipeId).Distinct().ToList();

        switch (projection)
        {
            case ExternalRecipeProjection.Summary:
            {
                var summaries = await _sources.LoadSummariesAsync(sourceIds, cancellationToken);
                return (
                    summaries.ToDictionary(x => x.Key, x => x.Value.Name),
                    summaries.Keys.ToHashSet(),
                    summaries,
                    new Dictionary<Guid, Recipe>());
            }
            case ExternalRecipeProjection.Ingredients:
            {
                var sources = await _sources.LoadIngredientSourcesAsync(sourceIds, cancellationToken);
                return (
                    sources.ToDictionary(x => x.Key, x => x.Value.Name),
                    sources.Keys.ToHashSet(),
                    new Dictionary<Guid, RecipeSourceSummary>(),
                    sources);
            }
            default:
            {
                var sources = await LoadFullSourcesAsync(sourceIds, cancellationToken);
                return (
                    sources.ToDictionary(x => x.Key, x => x.Value.Name),
                    sources.Keys.ToHashSet(),
                    new Dictionary<Guid, RecipeSourceSummary>(),
                    sources);
            }
        }
    }

    /// <summary>
    /// Полные источники с шагами: подробная страница читает ровно один адресный
    /// источник, а подбор — пачку одним запросом, без пофайлового обращения к БД
    /// на каждый внешний рецепт.
    /// </summary>
    private async Task<Dictionary<Guid, Recipe>> LoadFullSourcesAsync(
        IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken)
    {
        if (sourceIds.Count == 1)
        {
            var single = await _sources.LoadFullAsync(sourceIds[0], cancellationToken);
            return single is null
                ? new Dictionary<Guid, Recipe>()
                : new Dictionary<Guid, Recipe> { [single.Id] = single };
        }

        return await _sources.LoadSourcesAsync(sourceIds, cancellationToken);
    }
}
