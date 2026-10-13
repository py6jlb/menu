using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Recipes;

/// <summary>
/// Текущая ревизия рецепта из БД — единая точка чтения для сервисов, которые
/// сообщают актуальную версию при конфликте. Scoped-сервис, читает БД.
/// </summary>
public sealed class RecipeRevisionReader
{
    private readonly AppDbContext _db;

    public RecipeRevisionReader(AppDbContext db) => _db = db;

    /// <summary>
    /// Текущая ревизия рецепта. Для отсутствующего рецепта возвращается 0 —
    /// значение-маркер «записи нет», не пересекающееся с реальной ревизией (от 1).
    /// </summary>
    public async Task<int> CurrentAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _db.Recipes
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => (int?)r.Revision)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;
}
