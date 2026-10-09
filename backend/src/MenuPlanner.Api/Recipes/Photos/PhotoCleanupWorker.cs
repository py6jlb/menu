using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MenuPlanner.Api.Recipes.Documents;

namespace MenuPlanner.Api.Recipes.Photos;

/// <summary>
/// Фоновая уборка бесхозных фото с безопасным возрастом. Запускается в backend,
/// поэтому на время согласованного backup-снятия (backend остановлен) проходы не
/// выполняются и не могут удалить файлы из снимаемого набора.
/// </summary>
public sealed class PhotoCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PhotoCleanupOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<PhotoCleanupWorker> _logger;

    public PhotoCleanupWorker(
        IServiceScopeFactory scopes,
        PhotoCleanupOptions options,
        TimeProvider clock,
        ILogger<PhotoCleanupWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var collector = scope.ServiceProvider.GetRequiredService<PhotoGarbageCollector>();
                var result = await collector.CollectAsync(_options.MinimumAge, stoppingToken);
                if (result.Deleted > 0 || result.Failed > 0)
                    _logger.LogInformation(
                        "Фото: уборка — удалено {Deleted}, ошибок {Failed}, пропущено свежих {Fresh}.",
                        result.Deleted, result.Failed, result.KeptFresh);

                var documents = scope.ServiceProvider.GetRequiredService<DocumentGarbageCollector>();
                var documentsResult = await documents.CollectAsync(_options.MinimumAge, stoppingToken);
                if (documentsResult.Deleted > 0 || documentsResult.Failed > 0)
                    _logger.LogInformation(
                        "Документ: уборка — удалено {Deleted}, ошибок {Failed}, пропущено свежих {Fresh}.",
                        documentsResult.Deleted, documentsResult.Failed, documentsResult.KeptFresh);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Фоновая уборка фото: проход завершился ошибкой.");
            }

            try
            {
                await Task.Delay(_options.Interval, _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
