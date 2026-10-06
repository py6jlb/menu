using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes;

/// <summary>
/// Чтение рецептов семьи: список, рецепт с шагами и ингредиентами, живой источник,
/// кандидаты для подбора. Scoped-сервис, читает БД; обработчики получают его
/// параметром, а не работают с <see cref="AppDbContext"/> напрямую.
/// </summary>
public sealed class RecipeReader
{
    private readonly AppDbContext _db;

    public RecipeReader(AppDbContext db) => _db = db;

    /// <summary>Рецепты семьи с фильтром по происхождению (own/external/все).</summary>
    public Task<List<Recipe>> ListAsync(
        Guid familyId, string? scope, CancellationToken cancellationToken = default)
    {
        var query = _db.Recipes
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId);

        query = scope switch
        {
            "own" => query.Where(r => r.SourceRecipeId == null),
            "external" => query.Where(r => r.SourceRecipeId != null),
            _ => query
        };

        return query.OrderBy(r => r.Name).ToListAsync(cancellationToken);
    }

    /// <summary>Собственный рецепт семьи с шагами и ингредиентами или null.</summary>
    public Task<Recipe?> GetWithContentAsync(
        Guid id, Guid familyId, CancellationToken cancellationToken = default) =>
        _db.Recipes
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == id && r.FamilyId == familyId, cancellationToken);

    /// <summary>Живой источник внешнего рецепта с шагами и ингредиентами или null.</summary>
    public Task<Recipe?> GetSourceWithContentAsync(
        Guid sourceId, CancellationToken cancellationToken = default) =>
        _db.Recipes
            .AsNoTracking()
            .Include(r => r.Steps)
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.Id == sourceId, cancellationToken);

    /// <summary>Рецепты семьи как кандидаты подбора (с ингредиентами).</summary>
    public Task<List<Recipe>> MatchCandidatesAsync(
        Guid familyId, CancellationToken cancellationToken = default) =>
        _db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Where(r => r.FamilyId == familyId)
            .ToListAsync(cancellationToken);
}
