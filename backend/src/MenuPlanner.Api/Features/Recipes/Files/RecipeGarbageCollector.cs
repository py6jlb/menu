using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MenuPlanner.Infrastructure.Data;

namespace MenuPlanner.Api.Features.Recipes.Files;

/// <summary>Итог прохода уборки одного вида: что проверено и что с этим сделано.</summary>
public sealed record RecipeFileCleanupResult(
    int Scanned,
    int Deleted,
    int KeptReferenced,
    int KeptFresh,
    int Failed);

/// <summary>
/// Безопасная уборка бесхозных файлов рецепта. Удаляет только управляемые имена
/// вида (см. <see cref="RecipeFileNames"/>), не упомянутые ни одним рецептом и
/// старше безопасного возраста. Возраст защищает незавершённую загрузку/копирование
/// (файл пишется до commit) и файлы, уже снятые в backup-набор. Уборка выполняется
/// в backend, а backup кратко останавливает backend, поэтому уборка и снятие
/// набора не пересекаются. Scoped-сервис, читает БД и файлы.
/// </summary>
public sealed class RecipeGarbageCollector
{
    private readonly AppDbContext _db;
    private readonly IRecipeFileStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<RecipeGarbageCollector> _logger;

    public RecipeGarbageCollector(
        AppDbContext db, IRecipeFileStore store, TimeProvider clock, ILogger<RecipeGarbageCollector> logger)
    {
        _db = db;
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RecipeFileCleanupResult> CollectAsync(
        RecipeFileKind kind, TimeSpan minimumAge, CancellationToken cancellationToken = default)
    {
        var catalog = RecipeFileCatalog.For(kind);
        var referenced = await _db.Recipes
            .AsNoTracking()
            .Select(catalog.Link)
            .ToListAsync(cancellationToken);
        var referencedNames = referenced
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => Path.GetFileName(name!))
            .ToHashSet(StringComparer.Ordinal);

        var cutoff = _clock.GetUtcNow().UtcDateTime - minimumAge;
        var scanned = 0;
        var deleted = 0;
        var keptReferenced = 0;
        var keptFresh = 0;
        var failed = 0;

        foreach (var file in _store.List(kind))
        {
            if (!RecipeFileNames.IsManaged(kind, file.Name)) continue;
            scanned++;
            if (referencedNames.Contains(file.Name))
            {
                keptReferenced++;
                continue;
            }

            if (file.LastWriteUtc > cutoff)
            {
                keptFresh++;
                continue;
            }

            try
            {
                _store.Delete(kind, file.Name);
                deleted++;
            }
            catch (Exception exception)
            {
                failed++;
                _logger.LogError(
                    exception, catalog.LogPrefix + " не удалось убрать бесхозный файл {File}.", file.Name);
            }
        }

        return new RecipeFileCleanupResult(scanned, deleted, keptReferenced, keptFresh, failed);
    }
}
