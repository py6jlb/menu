using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Скалярная проекция живого рецепта-источника: всё, что нужно краткому списку,
/// без дочерних коллекций шагов и ингредиентов.
/// </summary>
public sealed record RecipeSourceSummary(
    Guid Id,
    string Name,
    int Difficulty,
    int? Calories,
    int CookTimeMinutes,
    int Servings,
    List<string> Tags,
    List<string> Seasonality,
    List<string> Diet,
    string? PhotoPath);

/// <summary>
/// Загружает живые рецепты-источники из БД. Scoped-сервис; чистая проекция
/// контента живёт отдельно в <see cref="ExternalRecipeContentResolver"/>.
/// </summary>
public sealed class ExternalRecipeSourceLoader
{
    private readonly AppDbContext _db;

    public ExternalRecipeSourceLoader(AppDbContext db) => _db = db;

    /// <summary>
    /// Загружает источники по их id вместе с ингредиентами и шагами, без отслеживания.
    /// Возвращает карту «id источника → рецепт-источник»; отсутствующие в БД просто не попадают.
    /// </summary>
    public async Task<Dictionary<Guid, Recipe>> LoadSourcesAsync(
        IEnumerable<Guid> sourceRecipeIds, CancellationToken cancellationToken = default)
    {
        var ids = sourceRecipeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, Recipe>();

        var sources = await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(cancellationToken);

        return sources.ToDictionary(s => s.Id);
    }

    /// <summary>
    /// Источники для расчёта закупки: только ингредиенты и порции, шаги не загружаются.
    /// Отсутствующие в БД в карту не попадают.
    /// </summary>
    public async Task<Dictionary<Guid, Recipe>> LoadIngredientSourcesAsync(
        IEnumerable<Guid> sourceRecipeIds, CancellationToken cancellationToken = default)
    {
        var ids = sourceRecipeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, Recipe>();

        var sources = await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(cancellationToken);

        return sources.ToDictionary(s => s.Id);
    }

    /// <summary>
    /// Подробное чтение одного источника с шагами и ингредиентами; null, если источник удалён.
    /// </summary>
    public async Task<Recipe?> LoadFullAsync(
        Guid sourceRecipeId, CancellationToken cancellationToken = default)
    {
        return await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == sourceRecipeId, cancellationToken);
    }

    /// <summary>
    /// Скалярная проекция источников одним запросом, без дочерних коллекций. Используется
    /// кратким чтением, которому имя/метаданные не требуют шагов и ингредиентов.
    /// </summary>
    public async Task<Dictionary<Guid, RecipeSourceSummary>> LoadSummariesAsync(
        IEnumerable<Guid> sourceRecipeIds, CancellationToken cancellationToken = default)
    {
        var ids = sourceRecipeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, RecipeSourceSummary>();

        var summaries = await _db.Recipes
            .AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new RecipeSourceSummary(
                r.Id,
                r.Name,
                r.Difficulty,
                r.Calories,
                r.CookTimeMinutes,
                r.Servings,
                r.Tags,
                r.Seasonality,
                r.Diet,
                r.PhotoPath))
            .ToListAsync(cancellationToken);

        return summaries.ToDictionary(s => s.Id);
    }
}
