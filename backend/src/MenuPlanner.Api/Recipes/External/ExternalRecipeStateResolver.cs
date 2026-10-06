using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Ссылка внешнего рецепта на источник: минимум данных, нужный для вычисления состояния.
/// </summary>
public readonly record struct ExternalSourceLink(
    Guid WrapperId,
    Guid SourceRecipeId,
    string? SourceToken);

/// <summary>
/// Загружает из БД факты (жив ли источник, каков текущий токен/отзыв шеринга)
/// и применяет к ним чистое правило <see cref="ExternalRecipeStateRules.Resolve"/>.
/// </summary>
public static class ExternalRecipeStateResolver
{
    public static async Task<Dictionary<Guid, ExternalRecipeState>> ResolveManyAsync(
        AppDbContext db,
        IReadOnlyCollection<ExternalSourceLink> links)
    {
        var result = new Dictionary<Guid, ExternalRecipeState>();
        var sourceIds = links
            .Select(l => l.SourceRecipeId)
            .Distinct()
            .ToList();

        if (sourceIds.Count == 0)
            return result;

        var alive = (await db.Recipes
            .AsNoTracking()
            .Where(r => sourceIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync()).ToHashSet();

        var shares = await db.RecipeShares
            .AsNoTracking()
            .Where(s => sourceIds.Contains(s.RecipeId))
            .ToDictionaryAsync(s => s.RecipeId, s => s);

        foreach (var link in links)
        {
            var sourceExists = alive.Contains(link.SourceRecipeId);
            var share = shares.GetValueOrDefault(link.SourceRecipeId);
            var tokenMatches = share is not null
                && string.Equals(share.Token, link.SourceToken, StringComparison.Ordinal);
            var revoked = share?.RevokedAt is not null;

            result[link.WrapperId] = ExternalRecipeStateRules.Resolve(
                sourceExists, tokenMatches, revoked);
        }

        return result;
    }
}
