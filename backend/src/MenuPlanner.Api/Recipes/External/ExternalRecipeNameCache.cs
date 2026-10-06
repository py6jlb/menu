using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;
using MenuPlanner.Api.Domain;

namespace MenuPlanner.Api.Recipes.External;

/// <summary>
/// Обновление кэша имени внешнего рецепта: у обёртки хранится только Name
/// источника, и он обновляется при каждом чтении до его актуального значения.
/// Обновление идёт через токен ревизии, поэтому гонка с правкой обёртки не
/// затирает чужую работу: при конфликте чтение повторяется один раз, и лишь
/// затем кэш остаётся как есть. Scoped-сервис, читает БД.
/// </summary>
public sealed class ExternalRecipeNameCache
{
    private const int MaxAttempts = 2;

    private readonly AppDbContext _db;

    public ExternalRecipeNameCache(AppDbContext db) => _db = db;

    public async Task RefreshAsync(
        IReadOnlyList<Guid> wrapperIds,
        IReadOnlyDictionary<Guid, Recipe> liveSources,
        CancellationToken cancellationToken = default)
    {
        if (wrapperIds.Count == 0)
            return;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var wrappers = await _db.Recipes
                .Where(r => wrapperIds.Contains(r.Id) && r.SourceRecipeId != null)
                .ToListAsync(cancellationToken);

            var changed = false;
            foreach (var wrapper in wrappers)
            {
                if (wrapper.SourceRecipeId is Guid sourceId
                    && liveSources.TryGetValue(sourceId, out var live)
                    && !string.Equals(wrapper.Name, live.Name, StringComparison.Ordinal))
                {
                    wrapper.Name = live.Name;
                    changed = true;
                }
            }

            if (!changed)
                return;

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Кто-то изменил обёртку между чтением и записью: перечитываем
                // и повторяем один раз, чтобы кэш не остался устаревшим молча.
                _db.ChangeTracker.Clear();
            }
        }
    }
}
