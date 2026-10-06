using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.External;

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
    public async Task<Dictionary<Guid, Recipe>> LoadSourcesAsync(IEnumerable<Guid> sourceRecipeIds)
    {
        var ids = sourceRecipeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, Recipe>();

        var sources = await _db.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Where(r => ids.Contains(r.Id))
            .ToListAsync();

        return sources.ToDictionary(s => s.Id);
    }
}
