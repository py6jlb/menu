using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MenuPlanner.Api.Data;

namespace MenuPlanner.Api.Recipes.Documents;

/// <summary>Итог прохода уборки документов: что проверено и что с этим сделано.</summary>
public sealed record DocumentCleanupResult(
    int Scanned,
    int Deleted,
    int KeptReferenced,
    int KeptFresh,
    int Failed);

/// <summary>
/// Безопасная уборка бесхозных PDF-документов. Удаляет только управляемые имена
/// (см. <see cref="DocumentFileNames"/>), не упомянутые ни одним рецептом и старше
/// безопасного возраста. Возраст защищает незавершённую загрузку/копирование
/// (файл пишется до commit) и файлы, уже снятые в backup-набор. Уборка выполняется
/// в backend, а backup кратко останавливает backend, поэтому уборка и снятие
/// набора не пересекаются. Scoped-сервис, читает БД и файлы.
/// </summary>
public sealed class DocumentGarbageCollector
{
    private readonly AppDbContext _db;
    private readonly IDocumentStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<DocumentGarbageCollector> _logger;

    public DocumentGarbageCollector(
        AppDbContext db, IDocumentStore store, TimeProvider clock, ILogger<DocumentGarbageCollector> logger)
    {
        _db = db;
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DocumentCleanupResult> CollectAsync(
        TimeSpan minimumAge, CancellationToken cancellationToken = default)
    {
        var referenced = await _db.Recipes
            .AsNoTracking()
            .Where(r => r.DocumentPath != null)
            .Select(r => r.DocumentPath!)
            .ToListAsync(cancellationToken);
        var referencedNames = referenced
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.Ordinal);

        var cutoff = _clock.GetUtcNow().UtcDateTime - minimumAge;
        var scanned = 0;
        var deleted = 0;
        var keptReferenced = 0;
        var keptFresh = 0;
        var failed = 0;

        foreach (var file in _store.List())
        {
            if (!DocumentFileNames.IsManaged(file.Name)) continue;
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
                _store.Delete(file.Name);
                deleted++;
            }
            catch (Exception exception)
            {
                failed++;
                _logger.LogError(exception, "Документ: не удалось убрать бесхозный файл {File}.", file.Name);
            }
        }

        return new DocumentCleanupResult(scanned, deleted, keptReferenced, keptFresh, failed);
    }
}
