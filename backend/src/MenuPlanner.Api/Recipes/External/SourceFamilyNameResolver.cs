using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Разрешает имя семьи-источника для меток «из семьи X» (внешний рецепт) и
/// «скопировано из семьи X» (после промоушена). Scoped-сервис: читает БД и
/// внедряется в обработчики через DI.
/// </summary>
public sealed class SourceFamilyNameResolver
{
    private readonly AppDbContext _db;

    public SourceFamilyNameResolver(AppDbContext db) => _db = db;

    public async Task<string?> ResolveAsync(Guid? familyId) =>
        familyId is Guid id
            ? await _db.Families
                .AsNoTracking()
                .Where(f => f.Id == id)
                .Select(f => f.Name)
                .FirstOrDefaultAsync()
            : null;

    public async Task<Dictionary<Guid, string>> ResolveManyAsync(IEnumerable<Guid> familyIds)
    {
        var ids = familyIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await _db.Families
            .AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name);
    }
}
