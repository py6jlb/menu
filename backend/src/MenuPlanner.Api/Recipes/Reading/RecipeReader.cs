using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Recipes.External;
using MenuPlanner.Api.Domain.Recipes;

namespace MenuPlanner.Api.Recipes.Reading;

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
/// Подсказка названия ингредиента с категорией продукта (или null для «Прочего»).
/// Категория — та же, что показал бы список покупок: по большинству.
/// </summary>
public sealed record IngredientSuggestion(string Name, string? Category);

/// <summary>
/// Единое предметное чтение рецепта для списка и деталей. Прячет от потребителей связь
/// локального рецепта с источником, доступность источника и происхождение: вызывающий
/// получает уже разрешённые состояние, подпись семьи-источника и правильный контент.
/// Scoped-сервис: читает БД и обновляет кэш имени при чтении.
/// </summary>
public sealed class RecipeReader
{
    private const int MaxIngredientSuggestions = 10;

    private readonly AppDbContext _db;
    private readonly ExternalRecipeContext _context;
    private readonly ExternalRecipeSourceLoader _sources;

    public RecipeReader(
        AppDbContext db,
        ExternalRecipeContext context,
        ExternalRecipeSourceLoader sources)
    {
        _db = db;
        _context = context;
        _sources = sources;
    }

    /// <summary>
    /// Краткие рецепты семьи выбранного среза. Источники читаются скалярной проекцией
    /// одним запросом через <see cref="ExternalRecipeContext"/>: шаги и ингредиенты для
    /// списка не загружаются, пофайлового запроса на каждый внешний рецепт нет. Кэш имени
    /// обновляется, если источник изменился.
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
            .Select(r => new Recipe
            {
                Id = r.Id,
                FamilyId = r.FamilyId,
                Name = r.Name,
                Difficulty = r.Difficulty,
                Calories = r.Calories,
                CookTimeMinutes = r.CookTimeMinutes,
                Servings = r.Servings,
                Tags = r.Tags,
                Seasonality = r.Seasonality,
                Diet = r.Diet,
                PhotoPath = r.PhotoPath,
                SourceRecipeId = r.SourceRecipeId,
                SourceFamilyId = r.SourceFamilyId,
                SourceToken = r.SourceToken,
                CopiedFromFamilyName = r.CopiedFromFamilyName
            })
            .ToListAsync(cancellationToken);

        if (stored.Count == 0)
            return Array.Empty<RecipeSummary>();

        var external = stored
            .Where(r => r.SourceRecipeId is not null)
            .ToList();

        var resolved = external.Count == 0
            ? new Dictionary<Guid, ResolvedExternalRecipe>()
            : await _context.ResolveAsync(
                external.Select(r => new ExternalRecipeLink(r)).ToList(),
                ExternalRecipeProjection.Summary,
                cancellationToken);

        return stored
            .Select(r => Summarize(r, resolved))
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

        var resolved = external.Count == 0
            ? new Dictionary<Guid, ResolvedExternalRecipe>()
            : await _context.ResolveAsync(
                external.Select(r => new ExternalRecipeLink(r)).ToList(),
                ExternalRecipeProjection.Full,
                cancellationToken);

        var candidates = new List<RecipeMatchCandidate>(recipes.Count);
        foreach (var recipe in recipes)
        {
            if (recipe.SourceRecipeId is null)
            {
                candidates.Add(new RecipeMatchCandidate(recipe, IsExternal: false, null, State: null));
                continue;
            }

            var resolvedRecipe = resolved[recipe.Id];
            if (resolvedRecipe.State == ExternalRecipeState.Broken)
                continue;

            // Контент уже материализован на живой источник; локальные id и ревизия сохранены.
            candidates.Add(new RecipeMatchCandidate(
                resolvedRecipe.Content ?? recipe,
                IsExternal: true,
                resolvedRecipe.SourceFamilyName,
                resolvedRecipe.State));
        }

        return candidates;
    }

    /// <summary>
    /// Актуальные названия ингредиентов семьи для автодополнения: свои рецепты плюс живой
    /// контент доступных внешних (warning включён — контент ещё читается; broken исключён,
    /// устаревших ингредиентов не даёт). Живые источники отбирает единый
    /// <see cref="ExternalRecipeContext"/>, поэтому правило «сломанной ссылки» не дублируется.
    /// Поиск по префиксу и группировка по нормализованному названию идут в SQL
    /// (<see cref="IngredientUsageQuery"/>), поэтому в память попадают только различимые
    /// названия с частотами, а не полное содержимое коллекции; пофайлового запроса на
    /// каждый внешний рецепт нет. Ранжирование по частоте, затем названию, и ограничение
    /// выдачи — здесь, единообразно для обоих источников.
    /// </summary>
    public async Task<IReadOnlyList<IngredientSuggestion>> ReadIngredientSuggestionsAsync(
        Guid familyId, string? query, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query?.Trim().ToLowerInvariant() ?? "";

        var own = await IngredientUsageQuery
            .Build(
                _db.Recipes
                    .AsNoTracking()
                    .Where(r => r.FamilyId == familyId && r.SourceRecipeId == null)
                    .SelectMany(r => r.Ingredients),
                normalizedQuery)
            .ToListAsync(cancellationToken);

        var external = await _db.Recipes
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId && r.SourceRecipeId != null)
            .Select(r => new Recipe
            {
                Id = r.Id,
                FamilyId = r.FamilyId,
                Name = r.Name,
                SourceRecipeId = r.SourceRecipeId,
                SourceFamilyId = r.SourceFamilyId,
                SourceToken = r.SourceToken
            })
            .ToListAsync(cancellationToken);

        IReadOnlyList<Guid> liveSourceIds = Array.Empty<Guid>();
        if (external.Count > 0)
        {
            var resolved = await _context.ResolveAsync(
                external.Select(r => new ExternalRecipeLink(r)).ToList(),
                ExternalRecipeProjection.Summary,
                cancellationToken);

            liveSourceIds = external
                .Where(r => resolved[r.Id].SourceAlive)
                .Select(r => r.SourceRecipeId!.Value)
                .ToList();
        }

        var externalUsages = liveSourceIds.Count == 0
            ? new List<IngredientUsage>()
            : await _sources.LoadIngredientSuggestionsAsync(liveSourceIds, normalizedQuery, cancellationToken);

        return own
            .Concat(externalUsages)
            .GroupBy(x => x.Normalized)
            .Select(g => new
            {
                Name = g.First().Name,
                Usage = g.Sum(x => x.Usage),
                Category = ResolveSuggestionCategory(g)
            })
            .OrderByDescending(x => x.Usage)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxIngredientSuggestions)
            .Select(x => new IngredientSuggestion(x.Name, x.Category))
            .ToList();
    }

    /// <summary>
    /// Категория подсказки — по большинству частот среди вариантов использования продукта,
    /// тем же правилом, что и категория позиции списка покупок.
    /// </summary>
    private static string? ResolveSuggestionCategory(IEnumerable<IngredientUsage> usages)
    {
        var votes = usages
            .Where(u => !string.IsNullOrEmpty(u.Category))
            .GroupBy(u => u.Category!)
            .ToDictionary(g => g.Key, g => g.Sum(u => u.Usage));
        return IngredientCategoryRules.ResolveMajority(votes);
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

        if (recipe.SourceRecipeId is null)
            return new RecipeDetail(recipe, IsExternal: false, null, null, State: null);

        var resolved = await _context.ResolveAsync(
            new[] { new ExternalRecipeLink(recipe) },
            ExternalRecipeProjection.Full,
            cancellationToken);

        var resolvedRecipe = resolved[recipe.Id];
        return new RecipeDetail(
            resolvedRecipe.Content ?? recipe,
            IsExternal: true,
            resolvedRecipe.SourceFamilyName,
            recipe.SourceFamilyId,
            resolvedRecipe.State);
    }

    private static RecipeSummary Summarize(
        Recipe stored, IReadOnlyDictionary<Guid, ResolvedExternalRecipe> resolved)
    {
        if (stored.SourceRecipeId is null)
        {
            return new RecipeSummary(
                stored.Id,
                stored.Name,
                stored.Difficulty,
                stored.Calories,
                stored.CookTimeMinutes,
                stored.Servings,
                stored.Tags,
                stored.Seasonality,
                stored.Diet,
                stored.PhotoPath,
                IsExternal: false,
                SourceFamilyName: null,
                State: null,
                stored.CopiedFromFamilyName);
        }

        // Модуль уже слил живой контент с кэшем (live?.X ?? stored.X): внешний рецепт
        // показывает актуальные поля источника, а при сломанной ссылке — свои кэшированные.
        var resolvedRecipe = resolved[stored.Id];
        var content = resolvedRecipe.Content ?? stored;
        return new RecipeSummary(
            content.Id,
            content.Name,
            content.Difficulty,
            content.Calories,
            content.CookTimeMinutes,
            content.Servings,
            content.Tags,
            content.Seasonality,
            content.Diet,
            content.PhotoPath,
            IsExternal: true,
            resolvedRecipe.SourceFamilyName,
            resolvedRecipe.State,
            content.CopiedFromFamilyName);
    }
}
