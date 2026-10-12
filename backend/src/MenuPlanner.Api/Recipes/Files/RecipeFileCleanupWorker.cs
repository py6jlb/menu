using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MenuPlanner.Api.Recipes.Files;

/// <summary>
/// Фоновая уборка бесхозных файлов рецептов (фото и документы) с безопасным
/// возрастом. Запускается в backend, поэтому на время согласованного
/// backup-снятия (backend остановлен) проходы не выполняются и не могут удалить
/// файлы из снимаемого набора.
/// </summary>
public sealed class RecipeFileCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RecipeFileCleanupOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<RecipeFileCleanupWorker> _logger;

    public RecipeFileCleanupWorker(
        IServiceScopeFactory scopes,
        RecipeFileCleanupOptions options,
        TimeProvider clock,
        ILogger<RecipeFileCleanupWorker> logger)
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
                var collector = scope.ServiceProvider.GetRequiredService<RecipeGarbageCollector>();
                foreach (var catalog in RecipeFileCatalog.All)
                {
                    var result = await collector.CollectAsync(catalog.Kind, _options.MinimumAge, stoppingToken);
                    if (result.Deleted > 0 || result.Failed > 0)
                        _logger.LogInformation(
                            catalog.LogPrefix + " уборка — удалено {Deleted}, ошибок {Failed}, пропущено свежих {Fresh}.",
                            result.Deleted, result.Failed, result.KeptFresh);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Фоновая уборка файлов рецептов: проход завершился ошибкой.");
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
