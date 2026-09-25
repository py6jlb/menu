using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Разрешает имя семьи-источника для меток «из семьи X» (внешний рецепт) и
/// «скопировано из семьи X» (после промоушена). Одно место для проекции имени семьи.
/// </summary>
public static class SourceFamilyNameResolver
{
    public static async Task<string?> ResolveAsync(AppDbContext db, Guid? familyId) =>
        familyId is Guid id
            ? await db.Families
                .AsNoTracking()
                .Where(f => f.Id == id)
                .Select(f => f.Name)
                .FirstOrDefaultAsync()
            : null;

    public static async Task<Dictionary<Guid, string>> ResolveManyAsync(
        AppDbContext db, IEnumerable<Guid> familyIds)
    {
        var ids = familyIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await db.Families
            .AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name);
    }
}
