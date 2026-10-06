using Microsoft.EntityFrameworkCore;
using MenuPlanner.Api.Data;

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
        IReadOnlyDictionary<Guid, string> liveSourceNames,
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
                    && liveSourceNames.TryGetValue(sourceId, out var liveName)
                    && !string.Equals(wrapper.Name, liveName, StringComparison.Ordinal))
                {
                    wrapper.Name = liveName;
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
